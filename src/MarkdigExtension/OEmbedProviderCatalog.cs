using System.Text.RegularExpressions;
using BlogGenerator.MarkdigExtension.Models;

namespace BlogGenerator.MarkdigExtension;

public class OEmbedProviderCatalog
{
    private readonly IReadOnlyList<OEmbedProviderJson> _providers;
    private readonly Dictionary<string, List<string>> _providerPatterns;

    public OEmbedProviderCatalog(IEnumerable<OEmbedProviderJson> providers)
    {
        _providers = providers.ToList();
        _providerPatterns = _providers.ToDictionary(
            provider => provider.ProviderUrl,
            provider => provider.EndPoints
                .SelectMany(endpoint => endpoint.Schemes)
                .Select(ConvertSchemeToRegexPattern)
                .Append(ConvertSchemeToRegexPattern($"{provider.ProviderUrl}*"))
                .ToList());
    }

    public string FindMatchingProviderUrl(string url)
    {
        foreach (var provider in _providerPatterns)
        {
            if (provider.Value.Any(pattern => Regex.IsMatch(url, pattern)))
            {
                return provider.Key;
            }
        }

        return string.Empty;
    }

    public string GetProviderEndpointUrl(string providerUrl, string targetUrl)
    {
        var providers = _providers.Where(r => r.ProviderUrl == providerUrl);

        foreach (var provider in providers)
        {
            foreach (var endpoint in provider.EndPoints)
            {
                var regexPatterns = endpoint.Schemes.Select(ConvertSchemeToRegexPattern);
                if (regexPatterns.Any(pattern => Regex.IsMatch(targetUrl, pattern)))
                {
                    return endpoint.Url;
                }
            }
        }

        return string.Empty;
    }

    private static string ConvertSchemeToRegexPattern(string scheme)
    {
        var escapedPattern = Regex.Escape(scheme).Replace(@"\*", ".*");
        return $"^{escapedPattern}$";
    }
}
