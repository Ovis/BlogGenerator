using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BlogGenerator.ShortCodes.Models;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Statiq.Common;
using Statiq.Web.Shortcodes;
using System.IO;
using System.Xml.Linq;
using System.Xml;
using System.Xml.Serialization;
using System.Threading;
using Spectre.Console;

namespace BlogGenerator.ShortCodes
{

    public class OEmbedShortCodes : EmbedShortcode
    {
        private const string TargetUrl = nameof(TargetUrl);
        private const string HideMedia = nameof(HideMedia);
        private const string HideThread = nameof(HideThread);
        private const string Theme = nameof(Theme);
        private const string OmitScript = nameof(OmitScript);

        private static volatile bool _initialized;

        private static readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

        private bool _omitScript;

        private static List<OEmbedProviderJson> _jsonData;
        private static Dictionary<string, List<string>> _oembedProviderDic = new Dictionary<string, List<string>>();


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

            var defaultLink = new StringBuilder()
                .Append($"<p>")
                .Append($"<a href=\"")
                .Append($"{url}")
                .Append($"\" target=\"_blank\">")
                .Append($"{url}")
                .Append($"</a>")
                .Append($"</p>");

            var result = new ShortcodeResult(defaultLink.ToString());

            var query = new List<string>();
            if (_omitScript || arguments.GetBool(OmitScript))
            {
                query.Add("omit_script=true");
            }

            var existProviderName = "";

            foreach (var dic in _oembedProviderDic.Where(dic => dic.Value.Select(pattern => Regex.IsMatch(url, pattern)).Any(isMatch => isMatch)))
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
                        return await GetEmbedResultAsync(arguments, oembedEndPointUrl, url, query, context);
                    }
                    catch
                    {
                        return result;
                    }
                }
            }

            {
                var content = string.Empty;
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

                    var json = new
                    {
                        url,
                        title = parseDoc.QuerySelector("title")?.TextContent,
                        ogTitle = parseDoc.QuerySelector("meta[property='og:title']")?.GetAttribute("content"),
                        ogImage = parseDoc.QuerySelector("meta[property='og:image']")?.GetAttribute("content"),
                        ogDescription = parseDoc.QuerySelector("meta[property='og:description']")?.GetAttribute("content"),
                        ogType = parseDoc.QuerySelector("meta[property='og:type']")?.GetAttribute("content"),
                        ogUrl = parseDoc.QuerySelector("meta[property='og:url']")?.GetAttribute("content"),
                        ogSiteName = parseDoc.QuerySelector("meta[property='og:site_name']")?.GetAttribute("content"),
                        oembedJson = parseDoc.QuerySelector("link[type='application/json+oembed']")?.GetAttribute("href")
                    };

                    if (!string.IsNullOrEmpty(json.oembedJson))
                    {
                        var (isSuccess, jsonContent) = await GetWebsiteContentAsync(context, json.oembedJson);

                        if (isSuccess)
                        {
                            var jsonData = JsonSerializer.Deserialize<ProviderResponse>(jsonContent);

                            switch (jsonData.Type)
                            {
                                case "link":
                                    break;

                                case "photo":
                                    break;

                                case "video":
                                case "rich":
                                    return new ShortcodeResult(jsonData.Html);

                                default:
                                    break;
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);

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

            await _semaphore.WaitAsync();

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

                                _oembedProviderDic.Add(providerName, list);
                            }
                        }
                    }
                    _initialized = true;
                }
                catch (Exception ex)
                {
                    context.LogWarning($"Error getting feed for{ex.Message}");
                }
            }
            finally
            {
                _semaphore.Release();
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
            httpClient.DefaultRequestHeaders.Add("User-Agent", "Statiq");

            var response = await httpClient.GetAsync(url);
            if (response.StatusCode == HttpStatusCode.Redirect ||
                response.StatusCode == HttpStatusCode.MovedPermanently)
            {
                context.LogWarning($"Attempting to follow redirect for oEmbed Provider Json");
                url = response.Headers.Location?.OriginalString;
                response = await httpClient.GetAsync(url);
            }

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();

                return (true, content);
            }

            return (false, null);
        }
    }
}