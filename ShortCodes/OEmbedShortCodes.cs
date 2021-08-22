using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Mime;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BlogGenerator.Converters;
using BlogGenerator.ShortCodes.Models;
using Hnx8.ReadJEnc;
using Microsoft.Extensions.Logging;
using Statiq.Common;
using Statiq.Web.Shortcodes;

namespace BlogGenerator.ShortCodes
{

    public class OEmbedShortCodes : EmbedShortcode
    {
        private const string TargetUrl = nameof(TargetUrl);
        private const string EnableDiscovery = nameof(EnableDiscovery);
        private const string HideMedia = nameof(HideMedia);
        private const string HideThread = nameof(HideThread);
        private const string Theme = nameof(Theme);
        private const string OmitScript = nameof(OmitScript);

        private static volatile bool _initialized;

        private static readonly SemaphoreSlim Semaphore = new(1, 1);

        private static List<OEmbedProviderJson> _jsonData;
        private static readonly Dictionary<string, List<string>> OembedProviderDic = new();


        public override async Task<ShortcodeResult> ExecuteAsync(KeyValuePair<string, string>[] args, IDocument document, IExecutionContext context)
        {
            await Initialize(context);

            var arguments = args.ToDictionary(
                TargetUrl,
                HideMedia,
                HideThread,
                Theme,
                OmitScript);
            arguments.RequireKeys(TargetUrl);

            var url = arguments.GetString(TargetUrl);

            //GistはoEmbedが提供されていないので直接生成
            if (url.Contains("gist.github.com"))
            {
                var gistEmbed = new StringBuilder()
                    .Append($"<p>")
                    .Append($"<script src=\"")
                    .Append($"{url}.js")
                    .Append($"\">")
                    .Append($"</script>")
                    .Append($"</p>");

                return new ShortcodeResult(gistEmbed.ToString());
            }


            var defaultLink = new StringBuilder()
                .Append($"<p>")
                .Append($"<a href=\"")
                .Append($"{url}")
                .Append($"\" target=\"_blank\">")
                .Append($"{url}")
                .Append($"</a>")
                .Append($"</p>");

            var result = new ShortcodeResult(defaultLink.ToString());

            var existProviderName = "";

            //oEmbed Providerリストチェック
            {
                foreach (var dic in OembedProviderDic.Where(dic => dic.Value.Select(pattern => Regex.IsMatch(url, pattern)).Any(isMatch => isMatch)))
                {
                    existProviderName = dic.Key;
                }

                if (!string.IsNullOrEmpty(existProviderName))
                {
                    //oEmbedプロバイダに存在する

                    var oembedEndPointUrl = string.Empty;

                    var providerData = _jsonData.Where(r => r.ProviderName == existProviderName);

                    foreach (var data in providerData)
                    {
                        foreach (var endPoint in data.EndPoints.Where(endPoint =>
                            endPoint.Schemes.Select(regexUrl => regexUrl.Replace("*", @".*"))
                                .Any(r => Regex.IsMatch(url, r))))
                        {
                            oembedEndPointUrl = endPoint.Url;
                        }
                    }

                    if (!string.IsNullOrEmpty(oembedEndPointUrl))
                    {
                        try
                        {
                            var embedResult = await GetEmbedResultAsync(arguments, oembedEndPointUrl, url, new List<string>(), context);

                            var resultString = new StringBuilder()
                                .Append($"<p>")
                                .Append($"{embedResult}")
                                .Append($"</p>").ToString();

                            return new ShortcodeResult(resultString);
                        }
                        catch (Exception e)
                        {
                            context.LogError($"Error:{e.Message} Url:{url} EndPoint:{oembedEndPointUrl}");
                            return result;
                        }
                    }
                }
            }

            {
                string content;
                {
                    var (isSuccess, contentHtml) = await GetWebsiteContentAsync(context, url);

                    if (!isSuccess)
                    {
                        //取得できなかったのでURLをそのままリンクとして表示
                        return result;
                    }

                    content = contentHtml;
                }

                try
                {
                    var parser = new AngleSharp.Html.Parser.HtmlParser();
                    var parseDoc = parser.ParseDocument(content);

                    var ogpData = new OgpTagData
                    {
                        Url = url,
                        Title = parseDoc.QuerySelector("title")?.TextContent,
                        OgTitle = parseDoc.QuerySelector("meta[property='og:title']")?.GetAttribute("content"),
                        OgImage = parseDoc.QuerySelector("meta[property='og:image']")?.GetAttribute("content"),
                        OgDescription = parseDoc.QuerySelector("meta[property='og:description']")?.GetAttribute("content"),
                        OgType = parseDoc.QuerySelector("meta[property='og:type']")?.GetAttribute("content"),
                        OgUrl = parseDoc.QuerySelector("meta[property='og:url']")?.GetAttribute("content"),
                        OgSiteName = parseDoc.QuerySelector("meta[property='og:site_name']")?.GetAttribute("content"),
                        OembedJson = parseDoc.QuerySelector("link[type='application/json+oembed']")?.GetAttribute("href")
                    };

                    if (arguments.GetBool(EnableDiscovery))
                    {
                        if (!string.IsNullOrEmpty(ogpData.OembedJson))
                        {
                            var (isSuccess, jsonContent) = await GetWebsiteContentAsync(context, ogpData.OembedJson);

                            if (isSuccess)
                            {
                                var deserializeOptions = new JsonSerializerOptions();
                                deserializeOptions.Converters.Add(new AutoNumberToStringConverter());

                                var jsonData = JsonSerializer.Deserialize<ProviderResponse>(jsonContent, deserializeOptions);

                                switch (jsonData?.Type)
                                {
                                    case "link":
                                        break;

                                    case "photo":
                                        break;

                                    case "video":
                                    case "rich":
                                        return new ShortcodeResult(jsonData.Html);
                                }
                            }
                        }
                    }

                    if (!string.IsNullOrEmpty(ogpData.OgTitle) && !string.IsNullOrEmpty(ogpData.OgUrl))
                    {
                        var ogpRichLinkGenerate = new StringBuilder()
                            .Append($"<div class=\"bcard-wrapper\">")
                            .Append($"<span class=\"bcard-header withgfav\">")
                            .Append($"<div class=\"bcard-favicon\" style=\"background-image: url(https://www.google.com/s2/favicons?domain={url})\"></div>")
                            .Append($"<div class=\"bcard-site\">")
                            .Append($"<a href=\"{url}\" rel=\"nofollow\" target=\"_blank\">{ogpData.OgSiteName}</a>")
                            .Append($"</div>")
                            .Append($"<div class=\"bcard-url\">")
                            .Append($"<a href=\"{url}\" rel=\"nofollow\" target=\"_blank\">{url}</a>")
                            .Append($"</div>")
                            .Append($"</span>")
                            .Append($"<span class=\"bcard-main withogimg\">")
                            .Append($"<div class=\"bcard-title\">")
                            .Append($"<a href=\"{url}\" rel=\"nofollow\" target=\"_blank\">")
                            .Append($"{ogpData.Title}")
                            .Append($"</a>")
                            .Append($"</div>")
                            .Append($"<div class=\"bcard-description\">")
                            .Append($"{ogpData.OgDescription}")
                            .Append($"</div>")
                            .Append($"<a href=\"{url}\" rel=\"nofollow\" target=\"_blank\">")
                            .Append($"<div class=\"bcard-img\" style=\"background-image: url({ogpData.OgImage})\"></div>")
                            .Append($"</a>")
                            .Append($"</span>")
                            .Append($"</div>");

                        return new ShortcodeResult(ogpRichLinkGenerate.ToString());
                    }
                }
                catch (Exception e)
                {
                    context.LogError($"Error:{e.Message} Url:{url} Content:{content}");

                    //HTMLパースに失敗したらURLをそのままリンクとして表示
                    return result;
                }
            }
            return result;
        }

        private async Task Initialize(IExecutionContext context)
        {
            if (_initialized)
            {
                return;
            }

            await Semaphore.WaitAsync();

            try
            {
                if (_initialized)
                {
                    return;
                }

                try
                {
                    var (isSuccess, content) =
                        await GetWebsiteContentAsync(context, "https://oembed.com/providers.json");

                    if (isSuccess)
                    {
                        var jsonData = JsonSerializer.Deserialize<List<OEmbedProviderJson>>(content);

                        if (jsonData != null)
                        {
                            _jsonData = jsonData;

                            foreach (var oEmbedProviderJson in jsonData)
                            {
                                var providerName = oEmbedProviderJson.ProviderName;

                                var list = oEmbedProviderJson.EndPoints.SelectMany(r => r.Schemes)
                                    .Select(url => url.Replace("*", @".*")).ToList();

                                list.Add($"{oEmbedProviderJson.ProviderUrl}.*");

                                OembedProviderDic.Add(providerName, list);
                            }
                        }
                    }
                    _initialized = true;
                }
                catch (Exception ex)
                {
                    context.LogWarning($"Error getting for{ex.Message}");
                }
            }
            finally
            {
                Semaphore.Release();
            }
        }


        /// <summary>
        /// 指定されたURLのコンテンツを取得して文字列型で返す
        /// </summary>
        /// <param name="context"></param>
        /// <param name="url"></param>
        /// <returns></returns>
        private async Task<(bool IsSuccess, string content)> GetWebsiteContentAsync(IExecutionContext context, string url)
        {
            using var httpClient = context.CreateHttpClient();
            httpClient.Timeout = TimeSpan.FromMilliseconds(5000);

            try
            {
                var response = await httpClient.GetAsync(url);
                if (response.StatusCode == HttpStatusCode.Redirect ||
                    response.StatusCode == HttpStatusCode.MovedPermanently)
                {
                    url = response.Headers.Location?.OriginalString;
                    response = await httpClient.GetAsync(url);
                }

                if (response.IsSuccessStatusCode)
                {
                    var byteArray = await response.Content.ReadAsByteArrayAsync();

                    ReadJEnc.JP.GetEncoding(byteArray, byteArray.Length, out var content);

                    return (true, content);
                }
            }
            catch (Exception e)
            {
                context.LogError($"{e.Message}");
                context.LogError($"ErrorUrl:{url}");
                return (false, null);
            }

            return (false, null);
        }



        private async Task<string> GetEmbedResultAsync(IMetadataDictionary arguments, string endpoint, string url, IEnumerable<string> query, IExecutionContext context)
        {
            // Get the oEmbed response
            EmbedResponse embedResponse;
            using (HttpClient httpClient = context.CreateHttpClient())
            {
                string request = $"{endpoint}?url={WebUtility.UrlEncode(url)}";
                if (arguments.ContainsKey(Format))
                {
                    request += $"&format={arguments.GetString(Format)}";
                }
                if (arguments.ContainsKey(MaxWidth))
                {
                    request += $"&maxwidth={arguments.GetString(MaxWidth)}";
                }
                if (arguments.ContainsKey(MaxHeight))
                {
                    request += $"&maxheight={arguments.GetString(MaxHeight)}";
                }
                if (query is object)
                {
                    request += "&" + string.Join("&", query);
                }
                HttpResponseMessage response = await httpClient.GetAsync(request);
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    context.LogError($"Received 404 not found for oEmbed at {request}");
                }
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentType?.MediaType is MediaTypeNames.Application.Json or MediaTypeNames.Text.Plain or MediaTypeNames.Text.Html)
                {
                    DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(EmbedResponse));
                    using (Stream stream = await response.Content.ReadAsStreamAsync())
                    {
                        embedResponse = (EmbedResponse)serializer.ReadObject(stream);
                    }
                }
                else if (response.Content.Headers.ContentType.MediaType is MediaTypeNames.Application.Xml or MediaTypeNames.Text.Xml)
                {
                    DataContractSerializer serializer = new DataContractSerializer(typeof(EmbedResponse));
                    using (Stream stream = await response.Content.ReadAsStreamAsync())
                    {
                        embedResponse = (EmbedResponse)serializer.ReadObject(stream);
                    }
                }
                else
                {
                    throw new InvalidDataException("Unknown content type for oEmbed response");
                }
            }

            // Switch based on type
            if (!string.IsNullOrEmpty(embedResponse.Html))
            {
                return embedResponse.Html;
            }
            else if (embedResponse.Type == "photo")
            {
                if (string.IsNullOrEmpty(embedResponse.Url)
                    || string.IsNullOrEmpty(embedResponse.Width)
                    || string.IsNullOrEmpty(embedResponse.Height))
                {
                    throw new InvalidDataException("Did not receive required oEmbed values for image type");
                }
                return $"<img src=\"{embedResponse.Url}\" width=\"{embedResponse.Width}\" height=\"{embedResponse.Height}\" />";
            }
            else if (embedResponse.Type == "link")
            {
                if (!string.IsNullOrEmpty(embedResponse.Title))
                {
                    return $"<a href=\"{url}\">{embedResponse.Title}</a>";
                }
                return $"<a href=\"{url}\">{url}</a>";
            }

            throw new InvalidDataException("Could not determine embedded content for oEmbed response");
        }
    }
}