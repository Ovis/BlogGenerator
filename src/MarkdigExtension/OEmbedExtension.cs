using System.Collections.Concurrent;
using System.Net;
using System.Net.Mime;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Serialization;
using AngleSharp.Html.Parser;
using BlogGenerator.Converters;
using BlogGenerator.MarkdigExtension.Models;
using Hnx8.ReadJEnc;
using Markdig;
using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Renderers;
using Markdig.Syntax.Inlines;
using Microsoft.AspNetCore.WebUtilities;

namespace BlogGenerator.MarkdigExtension;

public class OEmbedCardExtension : IMarkdownExtension
{
    private static bool _isFirstCall = true;
    private static readonly object LockObject = new object();
    private static readonly HttpClient HttpClient = new HttpClient();

    private static List<OEmbedProviderJson> _oEmbedProvidersJson = new();
    private static readonly Dictionary<string, List<string>> OembedProviderDic = new();

    public static OEmbedCardParser oEmbedCardParser { get; set; }

    public void Setup(MarkdownPipelineBuilder pipeline)
    {
        lock (LockObject)
        {
            if (_isFirstCall)
            {
                // httpClientの初期化
                HttpClient.DefaultRequestHeaders.Add("User-Agent", "BlogGenerator");

                // 処理の都合15秒でタイムアウト
                HttpClient.Timeout = TimeSpan.FromSeconds(15);

                // 一番最初に呼ばれた場合だけ実行されるロジック
                GetOEmbedProvidersJson().GetAwaiter().GetResult();
                _isFirstCall = false;

                oEmbedCardParser = new OEmbedCardParser(_oEmbedProvidersJson, OembedProviderDic, HttpClient);
            }
        }

        if (!pipeline.InlineParsers.Contains<OEmbedCardParser>())
        {
            pipeline.InlineParsers.Insert(0, oEmbedCardParser);
        }
    }

    public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
    {
    }

    private async ValueTask GetOEmbedProvidersJson()
    {
        try
        {
            (bool isSuccess, string content, _, _) = await GetWebsiteContentAsync("https://oembed.com/providers.json");

            if (isSuccess)
            {
                var jsonData = JsonSerializer.Deserialize<List<OEmbedProviderJson>>(content);

                if (jsonData != null)
                {
                    _oEmbedProvidersJson = jsonData;

                    foreach (var oEmbedProviderJson in jsonData)
                    {
                        var providerUrl = oEmbedProviderJson.ProviderUrl;

                        var list = oEmbedProviderJson.EndPoints.SelectMany(r => r.Schemes)
                            .Select(url => url.Replace("*", @".*")).ToList();

                        list.Add($"{oEmbedProviderJson.ProviderUrl}.*");

                        OembedProviderDic.Add($"{providerUrl}", list);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"oEmbed provider json could not be obtained. Error:{ex.Message}");
        }
    }

    private async ValueTask<(bool isSuccess, string content, string mediaType, Exception? error)>
        GetWebsiteContentAsync(string url)
    {
        try
        {
            var response = await HttpClient.GetAsync(url);

            if (response.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.MovedPermanently)
            {
                response = await HttpClient.GetAsync(response.Headers.Location?.OriginalString);
            }

            response.EnsureSuccessStatusCode();

            if (response.IsSuccessStatusCode)
            {
                var mediaType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;

                var byteArray = await response.Content.ReadAsByteArrayAsync();

                ReadJEnc.JP.GetEncoding(byteArray, byteArray.Length, out var content);

                return (true, content, mediaType, null);
            }
        }
        catch (TaskCanceledException e)
        {
            return (false, string.Empty, string.Empty, e);
        }
        catch (Exception e)
        {
            return (false, string.Empty, string.Empty, e);
        }

        return (false, string.Empty, string.Empty, null);
    }

    // キャッシュをJSONファイルに保存するメソッド
    public static async Task SaveOEmbedCacheAsync(string filePath)
    {
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(OEmbedCardParser.OEmbedCache, options);
            await File.WriteAllTextAsync(filePath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving OEmbed cache: {ex.Message}");
        }
    }

    // JSONファイルからキャッシュを読み込むメソッド
    public static async Task LoadOEmbedCacheAsync(string filePath)
    {
        if (!File.Exists(filePath))
            return;

        try
        {
            var json = await File.ReadAllTextAsync(filePath);
            var loadedCache = JsonSerializer.Deserialize<ConcurrentDictionary<string, string>>(json);

            if (loadedCache != null)
            {
                foreach (var item in loadedCache)
                {
                    OEmbedCardParser.OEmbedCache.TryAdd(item.Key, item.Value);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading OEmbed cache: {ex.Message}");
        }
    }
}

public class OEmbedCardParser : InlineParser
{
    private static List<OEmbedProviderJson> _oEmbedProvidersJson = [];
    private static Dictionary<string, List<string>> _oembedProviderDic = new();
    private static HttpClient _httpClient = new();
    private static ConcurrentDictionary<string, string> _oEmbedCache = new();

    // OEmbedCacheをパブリックプロパティとして公開
    public static ConcurrentDictionary<string, string> OEmbedCache => _oEmbedCache;

    public OEmbedCardParser(List<OEmbedProviderJson> oEmbedProvidersJson, Dictionary<string, List<string>> oEmbedProviderDic, HttpClient httpClient)
    {
        _oEmbedProvidersJson = oEmbedProvidersJson;
        _oembedProviderDic = oEmbedProviderDic;
        _httpClient = httpClient;
        OpeningCharacters = ['['];
    }

    private static readonly Regex OEmbedTagRegex = new(@"\[oembed:""(?<url>https?:\/\/[^""]+)""\]");

    public override bool Match(InlineProcessor processor, ref StringSlice slice)
    {
        var precedingCharacter = slice.PeekCharExtra(-1);
        if (!precedingCharacter.IsWhiteSpaceOrZero())
        {
            return false;
        }

        var match = OEmbedTagRegex.Match(slice.ToString());

        if (!match.Success)
        {
            return false;
        }

        var url = match.Groups["url"].Value;

        var literal = GetOEmbedHtml(url).GetAwaiter().GetResult();

        processor.Inline = new HtmlInline(literal)
        {
            Span =
            {
                Start = processor.GetSourcePosition(slice.Start, out var line, out var column)
            },
            Line = line,
            Column = column,
            IsClosed = true
        };
        processor.Inline.Span.End = processor.Inline.Span.Start + match.Length - 1;
        slice.Start += match.Length;
        return true;
    }

    private async ValueTask<string> GetOEmbedHtml(string url)
    {
        if (_oEmbedCache.TryGetValue(url, out var cachedResult))
        {
            return cachedResult;
        }

        // GistはoEmbedが提供されていないので直接生成
        if (url.Contains("gist.github.com"))
        {
            var result = SetParagraph(GetGistEmbedContent(url));
            _oEmbedCache[url] = result;
            return result;
        }

        //oEmbed ProviderリストによるoEmbed処理
        {
            var (isGetLinkSuccess, richLinkHtml, isVideo) = await GetRichLinkByOEmbedProviderAsync(url);

            if (isGetLinkSuccess)
            {
                var result = SetParagraph(richLinkHtml ?? string.Empty, isVideo);
                _oEmbedCache[url] = result;
                return result;
            }
        }

        //WebページからMETAタグを取得
        SiteMetaData metaData;
        {
            var (isGetDataSuccess, data) = await GetSiteMetaDataAsync(url);

            if (!isGetDataSuccess)
            {
                var result = SetParagraph(GetStandardLink(url));
                _oEmbedCache[url] = result;
                return result;
            }

            metaData = data ?? new SiteMetaData();
        }

        //oEmbed Discovery
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

            if (!string.IsNullOrEmpty(oEmbedEndPoint))
            {
                var (isSuccess, richLinkString, _, _) = await GetEmbedResultAsync(oEmbedEndPoint, string.Empty);

                if (isSuccess)
                {
                    var result = SetParagraph(richLinkString ?? string.Empty);
                    _oEmbedCache[url] = result;
                    return result;
                }
            }
        }

        //OGP情報が存在する場合はOGP情報からリッチリンクを作成する
        if (!string.IsNullOrEmpty(metaData.OgTitle) && !string.IsNullOrEmpty(metaData.OgUrl))
        {
            var result = SetParagraph(GetOgpRichLink(url, metaData));
            _oEmbedCache[url] = result;
            return result;
        }

        // それ以外はURLをそのままリンクとして表示
        {
            var result = SetParagraph(GetStandardLink(url));
            _oEmbedCache[url] = result;
            return result;
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


    private async Task<(bool IsGetDataSuccess, SiteMetaData? Data)> GetSiteMetaDataAsync(string url)
    {
        {
            string content;
            {
                var (isSuccess, contentHtml, _, _) = await GetWebsiteContentAsync(url);

                if (!isSuccess)
                {
                    //取得できなかったのでURLをそのままリンクとして表示
                    return (false, null);
                }

                content = contentHtml ?? string.Empty;
            }

            try
            {
                var parseDoc = new HtmlParser().ParseDocument(content);

                var ogpData = new SiteMetaData
                {
                    Url = url,
                    Title = parseDoc.QuerySelector("title")?.TextContent ?? string.Empty,
                    OgTitle = parseDoc.QuerySelector("meta[property='og:title']")?.GetAttribute("content") ?? string.Empty,
                    OgImage = parseDoc.QuerySelector("meta[property='og:image']")?.GetAttribute("content") ?? string.Empty,
                    OgDescription = parseDoc.QuerySelector("meta[property='og:description']")?.GetAttribute("content") ?? string.Empty,
                    OgType = parseDoc.QuerySelector("meta[property='og:type']")?.GetAttribute("content") ?? string.Empty,
                    OgUrl = parseDoc.QuerySelector("meta[property='og:url']")?.GetAttribute("content") ?? string.Empty,
                    OgSiteName = parseDoc.QuerySelector("meta[property='og:site_name']")?.GetAttribute("content") ?? string.Empty,
                    OembedJson = parseDoc.QuerySelector("link[type='application/json+oembed']")?.GetAttribute("href") ?? string.Empty,
                    OembedXml = string.IsNullOrEmpty(parseDoc.QuerySelector("link[type='application/xml+oembed']")?.GetAttribute("href"))
                        ? parseDoc.QuerySelector("link[type='application/xml+oembed']")?.GetAttribute("href") ?? string.Empty :
                        parseDoc.QuerySelector("link[type='text/xml+oembed']")?.GetAttribute("href") ?? string.Empty
                };

                return (true, ogpData);
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error:{e.Message} Url:{url} Content:{content}");

                //HTMLパースに失敗したらURLをそのままリンクとして表示
                return (false, null);
            }
        }
    }

    /// <summary>
    /// 指定されたURLのコンテンツを取得して文字列型で返す
    /// </summary>
    /// <param name="url"></param>
    /// <returns></returns>
    private async Task<(bool IsSuccess, string? Content, string? MediaType, Exception? Error)> GetWebsiteContentAsync(
         string url)
    {
        try
        {
            var response = await _httpClient.GetAsync(url);
            if (response.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.MovedPermanently)
            {
                url = response.Headers.Location?.OriginalString ?? string.Empty;
                response = await _httpClient.GetAsync(url);
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
            Console.WriteLine($"Timeout Url:{url}");
            return (false, null, null, e);
        }
        catch (HttpRequestException ex)
        {
            if (ex.HttpRequestError == HttpRequestError.Unknown)
            {
                Console.WriteLine($"Error: {ex.StatusCode} Url:{url}");
            }
            else
            {
                Console.WriteLine($"Error: {ex.HttpRequestError} Url:{url}");
            }

            return (false, null, null, ex);
        }
        catch (Exception e)
        {
            Console.WriteLine($"{e.Message} {url}");
            return (false, null, null, e);
        }

        return (false, null, null, null);
    }


    private async Task<(bool IsGetLinkSuccess, string? RichLinkHtml, bool IsVideo)> GetRichLinkByOEmbedProviderAsync(string url)
    {
        var existProviderUrl = "";

        //oEmbed Providerリストチェック
        {

            foreach (var dic in _oembedProviderDic.Where(dic =>
                         dic.Value.Select(pattern => Regex.IsMatch(url, pattern)).Any(isMatch => isMatch)))
            {
                existProviderUrl = dic.Key;
            }

            //リストになければ抜ける
            if (string.IsNullOrEmpty(existProviderUrl))
            {
                return (false, null, false);
            }

            var oembedEndPointUrl = string.Empty;

            var providerData = _oEmbedProvidersJson.Where(r => r.ProviderUrl == existProviderUrl);

            foreach (var data in providerData)
            {
                foreach (var endPoint in data.EndPoints.Where(endPoint =>
                             endPoint.Schemes.Select(regexUrl => regexUrl.Replace("*", @".*"))
                                 .Any(r => Regex.IsMatch(url, r))))
                {
                    oembedEndPointUrl = endPoint.Url;
                }
            }

            if (string.IsNullOrEmpty(oembedEndPointUrl))
            {
                return (false, null, false);
            }

            // WordPress.comの場合調整必要
            if (existProviderUrl.Contains("wordpress.com"))
            {
                oembedEndPointUrl = QueryHelpers.AddQueryString(oembedEndPointUrl, new Dictionary<string, string?>
                {
                    { "for", "BlogGenerator" }
                });
            }

            var (isSuccess, richLinkString, isVideo, error) = await GetEmbedResultAsync(oembedEndPointUrl, url);

            if (!isSuccess)
            {
                Console.WriteLine($"Error:{error} Url:{url} EndPoint:{oembedEndPointUrl}");
                return (false, null, false);
            }

            return (true, richLinkString, isVideo);
        }
    }

    private async Task<(bool IsSuccess, string? RichLinkString, bool IsVideo, Exception? Error)> GetEmbedResultAsync(string endpoint, string url)
    {
        string requestUrl;
        if (!string.IsNullOrEmpty(url))
        {
            requestUrl = QueryHelpers.AddQueryString(endpoint, new Dictionary<string, string?>
            {
                { "url", url }
            });
        }
        else
        {
            requestUrl = endpoint;
        }

        try
        {
            var (isSuccess, content, mediaType, error) = await GetWebsiteContentAsync(requestUrl);

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

                        embedResponse = JsonSerializer.Deserialize<EmbedResponse>(content ?? string.Empty, deserializeOptions)!;
                        break;
                    }
                case MediaTypeNames.Application.Xml or MediaTypeNames.Text.Xml:
                    {
                        var stream = new MemoryStream(Encoding.UTF8.GetBytes(content ?? string.Empty));

                        embedResponse = (EmbedResponse)new XmlSerializer(typeof(EmbedResponse)).Deserialize(stream)!;
                        break;
                    }
                default:
                    return (false, null, false, new InvalidDataException("Unknown MediaType for oEmbed response"));
            }

            if (!string.IsNullOrEmpty(embedResponse.Html))
            {
                return (true, embedResponse.Html, embedResponse.Type == "video", null);
            }

            switch (embedResponse.Type)
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

    private static string SetParagraph(string linkHtml, bool isVideo = false) =>
        new StringBuilder()
            .Append($"<p")
            .Append($"{(isVideo ? " class='oembed-video'" : "")}")
            .Append($">")
            .Append(linkHtml)
            .Append($"</p>").ToString();

    private static string GetStandardLink(string url) =>
        new StringBuilder()
            .Append($"<p>")
            .Append($"<a href=\"")
            .Append($"{url}")
            .Append($"\" target=\"_blank\">")
            .Append($"{url}")
            .Append($"</a>")
            .Append($"</p>").ToString();

    private static string GetGistEmbedContent(string url) =>
        new StringBuilder()
            .Append($"<p>")
            .Append($"<script src=\"")
            .Append($"{url}.js")
            .Append($"\">")
            .Append($"</script>")
            .Append($"</p>").ToString();
}
