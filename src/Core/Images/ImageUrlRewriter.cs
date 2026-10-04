namespace BlogGenerator.Core.Images;

/// <summary>公開URLを成果物内の画像へ解決し、成功した変換だけを参照へ反映する。</summary>
internal sealed class ImageUrlRewriter(Uri siteUrl, IReadOnlyDictionary<string, string> replacements)
{
    private readonly Uri _siteRoot = new UriBuilder(siteUrl)
        { Path = siteUrl.AbsolutePath.TrimEnd('/') + "/", Query = "", Fragment = "" }.Uri;

    public Uri DocumentUrl(string relativePath) => new(
        _siteRoot,
        string.Join('/', relativePath.Replace('\\', '/').Split('/').Select(Uri.EscapeDataString)));

    public string Rewrite(string url, Uri documentUrl)
    {
        if (string.IsNullOrWhiteSpace(url) || url.StartsWith('#') ||
            !Uri.TryCreate(documentUrl, url, out var resolved) ||
            resolved.Scheme is not ("http" or "https") ||
            resolved.Authority != siteUrl.Authority || resolved.Scheme != siteUrl.Scheme)
            return url;

        var sitePath = siteUrl.AbsolutePath.TrimEnd('/') + "/";
        if (!resolved.AbsolutePath.StartsWith(sitePath, StringComparison.Ordinal)) return url;
        var relativePath = Uri.UnescapeDataString(resolved.AbsolutePath[sitePath.Length..]);
        if (!replacements.TryGetValue(relativePath, out var target)) return url;

        // パスの表記、percent encoding、クエリ、フラグメントをそのまま保ち、拡張子だけを置換する。
        var suffix = url.IndexOfAny(['?', '#']);
        var path = suffix < 0 ? url : url[..suffix];
        var extension = path.LastIndexOf('.');
        return extension < 0 ? url : path[..extension] + Path.GetExtension(target) + (suffix < 0 ? "" : url[suffix..]);
    }
}
