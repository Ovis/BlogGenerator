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

        private bool _omitScript;

        private static List<OEmbedProviderJson> _jsonData;
        private static Dictionary<string, List<string>> _oembedProviderDic = new Dictionary<string, List<string>>();


        public override async Task<ShortcodeResult> ExecuteAsync(KeyValuePair<string, string>[] args, IDocument document, IExecutionContext context)
        {
            await Initialize(context);


            ShortcodeResult result = new ShortcodeResult("");

            IMetadataDictionary arguments = args.ToDictionary(
                TargetUrl,
                HideMedia,
                HideThread,
                Theme,
                OmitScript);
            arguments.RequireKeys(TargetUrl);

            List<string> query = new List<string>();
            if (_omitScript || arguments.GetBool(OmitScript))
            {
                query.Add("omit_script=true");
            }

            var url = arguments.GetString(TargetUrl);

            var existProviderName = "";

            foreach (var dic in _oembedProviderDic.Where(dic => dic.Value.Select(pattern => Regex.IsMatch(url, pattern)).Any(isMatch => isMatch)))
            {
                existProviderName = dic.Key;
            }


            if (!string.IsNullOrEmpty(existProviderName))
            {
                var oembedEndPointUrl = string.Empty;

                var providerData = _jsonData.Where(r => r.ProviderName == existProviderName);


                foreach (var data in providerData)
                {
                    foreach (var endPoint in data.EndPoints.Where(endPoint =>
                        endPoint.Schemes.Select(regexUrl => regexUrl.Replace("*", @"\w+"))
                            .Any(r => Regex.IsMatch(url, r))))
                    {
                        oembedEndPointUrl = endPoint.Url;
                    }
                }

                try
                {
                    return await GetEmbedResultAsync(arguments, oembedEndPointUrl, url, query, context);
                }
                catch
                {
                    var stringBuilder = new StringBuilder()
                        .Append($"<p>")
                        .Append($"<a href=\"")
                        .Append($"{url}")
                        .Append($"\" target=\"_blank\">")
                        .Append($"{url}")
                        .Append($"</a>")
                        .Append($"</p>");

                    return new ShortcodeResult(stringBuilder.ToString());
                }
            }

            return result;
        }

        private async Task Initialize(IExecutionContext context)
        {
            if (_initialized)
                return;

            _initialized = true;

            try
            {
                using var httpClient = context.CreateHttpClient();
                httpClient.DefaultRequestHeaders.Add("User-Agent", "Statiq");

                var response = await httpClient.GetAsync("https://oembed.com/providers.json");
                if (response.StatusCode == HttpStatusCode.Redirect ||
                    response.StatusCode == HttpStatusCode.MovedPermanently)
                {
                    context.LogWarning($"Attempting to follow redirect for oEmbed Provider Json");
                    var url = response.Headers.Location?.OriginalString;
                    response = await httpClient.GetAsync(url);
                }

                if (!response.IsSuccessStatusCode)
                {
                    return;
                }
                var jsonData = JsonSerializer.Deserialize<List<OEmbedProviderJson>>(await response.Content.ReadAsStringAsync());

                if (jsonData != null)
                {
                    _jsonData = jsonData;

                    foreach (var oEmbedProviderJson in jsonData)
                    {
                        var providerName = oEmbedProviderJson.ProviderName;

                        var list = oEmbedProviderJson.EndPoints.SelectMany(r => r.Schemes).Select(url => url.Replace("*", @"\w+")).ToList();

                        list.Add($"{oEmbedProviderJson.ProviderUrl}\\w+");

                        _oembedProviderDic.Add(providerName, list);
                    }
                }
            }
            catch (Exception ex)
            {
                context.LogWarning($"Error getting feed for{ex.Message}");
            }
        }
    }
}