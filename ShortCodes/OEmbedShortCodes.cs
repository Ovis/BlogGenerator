using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mime;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Serialization;
using AngleSharp.Html.Parser;
using BlogGenerator.Converters;
using BlogGenerator.ShortCodes.Models;
using Hnx8.ReadJEnc;
using Microsoft.Extensions.Logging;
using Statiq.Common;

namespace BlogGenerator.ShortCodes
{

    public class OEmbedShortCodes : Shortcode
    {
        private const string TargetUrl = nameof(TargetUrl);
        private const string EnableDiscovery = nameof(EnableDiscovery);
        private const string IsVideo = nameof(IsVideo);

        private const string OEmbedProviderList = "https://oembed.com/providers.json";

        private static volatile bool _initialized;

        private static readonly SemaphoreSlim Semaphore = new(1, 1);

        private static List<OEmbedProviderJson> _jsonData;
        private static readonly Dictionary<string, List<string>> OembedProviderDic = new();


        private async Task Initialize(IExecutionContext context)
        {
            await Semaphore.WaitAsync();

            try
            {
                if (_initialized)
                {
                    return;
                }

                try
                {
                    var (isSuccess, content, _) =
                        await GetWebsiteContentAsync(context, OEmbedProviderList);

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



        public override async Task<ShortcodeResult> ExecuteAsync(
            KeyValuePair<string, string>[] args,
            string content,
            IDocument document,
            IExecutionContext context) =>
            await ExecuteAsync(args, document, context);



        public async Task<ShortcodeResult> ExecuteAsync(KeyValuePair<string, string>[] args, IDocument document,
            IExecutionContext context)
        {
            if (!_initialized)
            {
                await Initialize(context);
            }

            var arguments = args.ToDictionary(
                TargetUrl,
                IsVideo
            );
            arguments.RequireKeys(TargetUrl);

            var url = arguments.GetString(TargetUrl);

            //GistはoEmbedが提供されていないので直接生成
            if (url.Contains("gist.github.com"))
            {
                return new ShortcodeResult(GetGistEmbedContent(url));
            }

            //oEmbed Providerリストチェック
            {
                var (isGetLinkSuccess, richLinkHtml) = await GetRichLinkByOEmbedProviderAsync(url, context);

                if (isGetLinkSuccess)
                {
                    return new ShortcodeResult(richLinkHtml);
                }
            }

            SiteMetaData metaData;
            {
                var (isGetDataSuccess, data) = await GetSiteMetaDataAsync(url, context);

                if (!isGetDataSuccess)
                {
                    return new ShortcodeResult(GetDefaultLink(url));
                }

                metaData = data;
            }

            if (arguments.GetBool(EnableDiscovery))
            {
                var oEmbedEndPoint = string.Empty;

                if (!string.IsNullOrEmpty(metaData.OembedJson))
                {
                    oEmbedEndPoint = metaData.OembedJson;
                }
                else if (!string.IsNullOrEmpty(metaData.OembedXml))
                {
                    oEmbedEndPoint = metaData.OembedXml;
                }

                var (isSuccess, richLinkString, _, _) = await GetEmbedResultAsync(oEmbedEndPoint, null, context);

                if (isSuccess)
                {
                    return new ShortcodeResult(richLinkString);
                }
            }


            if (!string.IsNullOrEmpty(metaData.OgTitle) && !string.IsNullOrEmpty(metaData.OgUrl))
            {
                return new ShortcodeResult(GetOgpRichLink(url, metaData));
            }


            return new ShortcodeResult(GetDefaultLink(url));
        }

        private string GetDefaultLink(string url) =>
            new StringBuilder()
                .Append($"<p>")
                .Append($"<a href=\"")
                .Append($"{url}")
                .Append($"\" target=\"_blank\">")
                .Append($"{url}")
                .Append($"</a>")
                .Append($"</p>").ToString();

        private string GetGistEmbedContent(string url) =>
            new StringBuilder()
                .Append($"<p>")
                .Append($"<script src=\"")
                .Append($"{url}.js")
                .Append($"\">")
                .Append($"</script>")
                .Append($"</p>").ToString();

        private async Task<(bool IsGetLinkSuccess, string RichLinkHtml)> GetRichLinkByOEmbedProviderAsync(string url, IExecutionContext context)
        {
            var existProviderName = "";

            //oEmbed Providerリストチェック
            {
                foreach (var dic in OembedProviderDic.Where(dic =>
                    dic.Value.Select(pattern => Regex.IsMatch(url, pattern)).Any(isMatch => isMatch)))
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
                        var (isSuccess, richLinkString, _, error) = await GetEmbedResultAsync(oembedEndPointUrl, url, context);

                        if (!isSuccess)
                        {
                            context.LogError($"Error:{error} Url:{url} EndPoint:{oembedEndPointUrl}");
                            return (false, null);
                        }

                        return (true, richLinkString);
                    }
                }
            }

            return (false, null);
        }

        private async Task<(bool IsGetDataSuccess, SiteMetaData Data)> GetSiteMetaDataAsync(string url,
            IExecutionContext context)
        {
            {
                string content;
                {
                    var (isSuccess, contentHtml, _) = await GetWebsiteContentAsync(context, url);

                    if (!isSuccess)
                    {
                        //取得できなかったのでURLをそのままリンクとして表示
                        return (false, null);
                    }

                    content = contentHtml;
                }

                try
                {
                    var parseDoc = new HtmlParser().ParseDocument(content);

                    var ogpData = new SiteMetaData
                    {
                        Url = url,
                        Title = parseDoc.QuerySelector("title")?.TextContent,
                        OgTitle = parseDoc.QuerySelector("meta[property='og:title']")?.GetAttribute("content"),
                        OgImage = parseDoc.QuerySelector("meta[property='og:image']")?.GetAttribute("content"),
                        OgDescription = parseDoc.QuerySelector("meta[property='og:description']")
                            ?.GetAttribute("content"),
                        OgType = parseDoc.QuerySelector("meta[property='og:type']")?.GetAttribute("content"),
                        OgUrl = parseDoc.QuerySelector("meta[property='og:url']")?.GetAttribute("content"),
                        OgSiteName = parseDoc.QuerySelector("meta[property='og:site_name']")?.GetAttribute("content"),
                        OembedJson = parseDoc.QuerySelector("link[type='application/json+oembed']")?.GetAttribute("href"),
                        OembedXml = string.IsNullOrEmpty(parseDoc.QuerySelector("link[type='application/xml+oembed']")?.GetAttribute("href"))
                            ? parseDoc.QuerySelector("link[type='application/xml+oembed']")?.GetAttribute("href") :
                            parseDoc.QuerySelector("link[type='text/xml+oembed']")?.GetAttribute("href")
                    };

                    return (true, ogpData);
                }
                catch (Exception e)
                {
                    context.LogError($"Error:{e.Message} Url:{url} Content:{content}");

                    //HTMLパースに失敗したらURLをそのままリンクとして表示
                    return (false, null);
                }
            }
        }



        private string GetOgpRichLink(string url, SiteMetaData metaData)
        {
            var noSchemeUrl = url.Replace($"{new Uri(url).Scheme}://", "");

            var ogpRichLinkGenerate = new StringBuilder()
                .Append($"<div class=\"bcard-wrapper\">")
                .Append($"<span class=\"bcard-header withgfav\">")
                .Append(
                    $"<div class=\"bcard-favicon\" style=\"background-image: url(https://www.google.com/s2/favicons?domain={url})\"></div>")
                .Append($"<div class=\"bcard-site\">")
                .Append($"<a href=\"{url}\" rel=\"nofollow\" target=\"_blank\">{metaData.OgSiteName}</a>")
                .Append($"</div>")
                .Append($"<div class=\"bcard-url\">")
                .Append($"<a href=\"{url}\" rel=\"nofollow\" target=\"_blank\">{url}</a>")
                .Append($"</div>")
                .Append($"</span>")
                .Append($"<span class=\"bcard-main withogimg\">")
                .Append($"<div class=\"bcard-title\">")
                .Append($"<a href=\"{url}\" rel=\"nofollow\" target=\"_blank\">")
                .Append($"{metaData.Title}")
                .Append($"</a>")
                .Append($"</div>")
                .Append($"<div class=\"bcard-description\">")
                .Append($"{metaData.OgDescription}")
                .Append($"</div>")
                .Append($"<a href=\"{url}\" rel=\"nofollow\" target=\"_blank\">")
                .Append($"<div class=\"bcard-img\" style=\"background-image: url({metaData.OgImage})\"></div>")
                .Append($"</a>")
                .Append($"</span>")
                .Append($"<span>")
                .Append(
                    $"<a href=\"//b.hatena.ne.jp/entry/s/{noSchemeUrl}\" ref=\"nofollow\" target=\"_blank\"><img src=\"//b.st-hatena.com/entry/image/{url}\" alt=\"[はてなブックマークで表示]\"></a>")
                .Append($"</span>")
                .Append($"</div>");

            return ogpRichLinkGenerate.ToString();
        }



        /// <summary>
        /// 指定されたURLのコンテンツを取得して文字列型で返す
        /// </summary>
        /// <param name="context"></param>
        /// <param name="url"></param>
        /// <returns></returns>
        private async Task<(bool IsSuccess, string Content, string MediaType)> GetWebsiteContentAsync(
            IExecutionContext context, string url)
        {
            using var httpClient = context.CreateHttpClient();

            //処理の都合10秒でタイムアウト
            httpClient.Timeout = TimeSpan.FromMilliseconds(10000);

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
                    var mediaType = response.Content.Headers.ContentType?.MediaType;

                    var byteArray = await response.Content.ReadAsByteArrayAsync();

                    ReadJEnc.JP.GetEncoding(byteArray, byteArray.Length, out var content);

                    return (true, content, mediaType);
                }
            }
            catch (Exception e)
            {
                context.LogError($"{e.Message}");
                context.LogError($"ErrorUrl:{url}");
                return (false, null, null);
            }

            return (false, null, null);
        }



        private async Task<(bool IsSuccess, string RichLinkString, bool IsVideo, Exception Error)> GetEmbedResultAsync(string endpoint, string url, IExecutionContext context)
        {
            var request = string.IsNullOrEmpty(url) ? endpoint : $"{endpoint}?url={WebUtility.UrlEncode(url)}";

            try
            {
                var (isSuccess, content, mediaType) = await GetWebsiteContentAsync(context, request);

                if (!isSuccess)
                {
                    return (false, null, false, null);
                }

                EmbedResponse embedResponse;
                switch (mediaType)
                {
                    case MediaTypeNames.Application.Json or MediaTypeNames.Text.Plain or MediaTypeNames.Text.Html:
                        {
                            var deserializeOptions = new JsonSerializerOptions();
                            deserializeOptions.Converters.Add(new AutoNumberToStringConverter());

                            embedResponse = JsonSerializer.Deserialize<EmbedResponse>(content, deserializeOptions);
                            break;
                        }
                    case MediaTypeNames.Application.Xml or MediaTypeNames.Text.Xml:
                        {
                            var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));

                            embedResponse = (EmbedResponse)new XmlSerializer(typeof(EmbedResponse)).Deserialize(stream);
                            break;
                        }
                    default:
                        return (false, null, false, new InvalidDataException("Unknown MediaType for oEmbed response"));
                }


                if (!string.IsNullOrEmpty(embedResponse?.Html))
                {
                    return (true, embedResponse.Html, embedResponse.Type == "video", null);
                }

                switch (embedResponse?.Type)
                {
                    case "photo" when string.IsNullOrEmpty(embedResponse.Url)
                                      || string.IsNullOrEmpty(embedResponse.Width)
                                      || string.IsNullOrEmpty(embedResponse.Height):
                        throw new InvalidDataException("Did not receive required oEmbed values for image type");

                    case "photo":
                        return (true,
                            $"<img src=\"{embedResponse.Url}\" width=\"{embedResponse.Width}\" height=\"{embedResponse.Height}\" />",
                            false, null);
                    case "link":
                        return (false, null, false, null);
                    default:
                        return (false, null, false,
                            new InvalidDataException("Unknown content type for oEmbed response"));
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                return (false, null, false, e);
            }
        }
    }
}