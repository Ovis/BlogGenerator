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
                    var (isSuccess, content, _, _) =
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
                    context.LogError($"oEmbed provider json could not be obtained. Error:{ex.Message}");
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
            var isVideoContent = arguments.GetBool(IsVideo);

            //GistはoEmbedが提供されていないので直接生成
            if (url.Contains("gist.github.com"))
            {
                return SetParagraph(GetGistEmbedContent(url), isVideoContent);
            }

            //oEmbed ProviderリストによるoEmbed処理
            {
                var (isGetLinkSuccess, richLinkHtml, isVideo) = await GetRichLinkByOEmbedProviderAsync(url, context);

                if (isGetLinkSuccess)
                {
                    return SetParagraph(richLinkHtml, isVideo);
                }
            }

            //WebページからMETAタグを取得
            SiteMetaData metaData;
            {
                var (isGetDataSuccess, data) = await GetSiteMetaDataAsync(url, context);

                if (!isGetDataSuccess)
                {
                    return SetParagraph(GetStandardLink(url), isVideoContent);
                }

                metaData = data;
            }

            //oEmbed Discovery
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
                    return SetParagraph(richLinkString, isVideoContent);
                }
            }

            //OGP情報が存在する場合はOGP情報からリッチリンクを作成する
            if (!string.IsNullOrEmpty(metaData.OgTitle) && !string.IsNullOrEmpty(metaData.OgUrl))
            {
                return SetParagraph(GetOgpRichLink(url, metaData), isVideoContent);
            }

            return SetParagraph(GetStandardLink(url), isVideoContent);
        }


        /// <summary>
        /// 立地リンクの段落タグを付与
        /// </summary>
        /// <param name="linkHtml"></param>
        /// <param name="isVideo"></param>
        /// <returns></returns>
        private static ShortcodeResult SetParagraph(string linkHtml, bool isVideo = false) =>
            new(new StringBuilder()
                .Append($"<p")
                .Append($"{(isVideo ? " class='oembed-video'" : "")}")
                .Append($">")
                .Append(linkHtml)
                .Append($"</p>").ToString());


        /// <summary>
        /// 標準的なAタグによるリンクの生成
        /// </summary>
        /// <param name="url"></param>
        /// <returns></returns>
        private static string GetStandardLink(string url) =>
            new StringBuilder()
                .Append($"<p>")
                .Append($"<a href=\"")
                .Append($"{url}")
                .Append($"\" target=\"_blank\">")
                .Append($"{url}")
                .Append($"</a>")
                .Append($"</p>").ToString();


        /// <summary>
        /// GitHub Gist用埋め込みコードの生成
        /// </summary>
        /// <param name="url"></param>
        /// <returns></returns>
        private static string GetGistEmbedContent(string url) =>
            new StringBuilder()
                .Append($"<p>")
                .Append($"<script src=\"")
                .Append($"{url}.js")
                .Append($"\">")
                .Append($"</script>")
                .Append($"</p>").ToString();


        /// <summary>
        /// oEmbed プロバイダーリストからリッチリンクを生成
        /// </summary>
        /// <param name="url"></param>
        /// <param name="context"></param>
        /// <returns></returns>
        private async Task<(bool IsGetLinkSuccess, string RichLinkHtml, bool IsVideo)> GetRichLinkByOEmbedProviderAsync(string url, IExecutionContext context)
        {
            var existProviderName = "";

            //oEmbed Providerリストチェック
            {
                foreach (var dic in OembedProviderDic.Where(dic =>
                    dic.Value.Select(pattern => Regex.IsMatch(url, pattern)).Any(isMatch => isMatch)))
                {
                    existProviderName = dic.Key;
                }

                //リストになければ抜ける
                if (string.IsNullOrEmpty(existProviderName)) return (false, null, false);


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

                if (string.IsNullOrEmpty(oembedEndPointUrl)) return (false, null, false);


                var (isSuccess, richLinkString, isVideo, error) = await GetEmbedResultAsync(oembedEndPointUrl, url, context);

                if (!isSuccess)
                {
                    context.LogWarning($"Error:{error} Url:{url} EndPoint:{oembedEndPointUrl}");
                    return (false, null, false);
                }

                return (true, richLinkString, isVideo);
            }
        }


        /// <summary>
        /// ウェブサイトのメタデータ情報を取得
        /// </summary>
        /// <param name="url"></param>
        /// <param name="context"></param>
        /// <returns></returns>
        private async Task<(bool IsGetDataSuccess, SiteMetaData Data)> GetSiteMetaDataAsync(string url,
            IExecutionContext context)
        {
            {
                string content;
                {
                    var (isSuccess, contentHtml, _, _) = await GetWebsiteContentAsync(context, url);

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
                        OgDescription = parseDoc.QuerySelector("meta[property='og:description']")?.GetAttribute("content"),
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
                    context.LogWarning($"Error:{e.Message} Url:{url} Content:{content}");

                    //HTMLパースに失敗したらURLをそのままリンクとして表示
                    return (false, null);
                }
            }
        }


        /// <summary>
        /// OGPデータによるリッチリンク生成
        /// </summary>
        /// <param name="url"></param>
        /// <param name="metaData"></param>
        /// <returns></returns>
        private static string GetOgpRichLink(string url, SiteMetaData metaData)
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
        private static async Task<(bool IsSuccess, string Content, string MediaType, Exception Error)> GetWebsiteContentAsync(
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

                response.EnsureSuccessStatusCode();

                if (response.IsSuccessStatusCode)
                {
                    var mediaType = response.Content.Headers.ContentType?.MediaType;

                    var byteArray = await response.Content.ReadAsByteArrayAsync();

                    ReadJEnc.JP.GetEncoding(byteArray, byteArray.Length, out var content);

                    return (true, content, mediaType, null);
                }
            }
            catch (TaskCanceledException e)
            {
                //タイムアウト
                context.LogWarning($"{e.Message}");
                return (false, null, null, e);
            }
            catch (Exception e)
            {
                return (false, null, null, e);
            }

            return (false, null, null, null);
        }


        /// <summary>
        /// oEmbedプロバイダから結果を取得
        /// </summary>
        /// <param name="endpoint"></param>
        /// <param name="url"></param>
        /// <param name="context"></param>
        /// <returns></returns>
        private async Task<(bool IsSuccess, string RichLinkString, bool IsVideo, Exception Error)> GetEmbedResultAsync(string endpoint, string url, IExecutionContext context)
        {
            var request = string.IsNullOrEmpty(url) ? endpoint : $"{endpoint}?url={WebUtility.UrlEncode(url)}";

            try
            {
                var (isSuccess, content, mediaType, error) = await GetWebsiteContentAsync(context, request);

                if (!isSuccess)
                {
                    return (false, null, false, error);
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
                return (false, null, false, e);
            }
        }
    }
}