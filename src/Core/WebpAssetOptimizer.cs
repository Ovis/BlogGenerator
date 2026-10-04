using System.Globalization;
using System.Buffers.Binary;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using BlogGenerator.Core.Images;
using ImageMagick;
using ImageMagick.Formats;

namespace BlogGenerator.Core;

/// <summary>生成済みのサイトを縮小する。変換・参照検査が完了するまでは元画像を削除しない。</summary>
internal sealed class WebpAssetOptimizer
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png" };
    private static readonly HashSet<string> BinaryExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".webp", ".gif", ".avif", ".ico", ".woff", ".woff2", ".ttf", ".otf", ".mp4", ".mp3", ".pdf", ".zip" };

    public async Task<WebpOptimizationResult> OptimizeAsync(
        string inputDir, string outputDir, Uri siteUrl, IReadOnlyCollection<string> feedFiles, TextWriter log)
    {
        var outputFiles = Directory.EnumerateFiles(outputDir, "*", SearchOption.AllDirectories).ToArray();
        var siteOriginalBytes = outputFiles.Sum(x => new FileInfo(x).Length);
        var existingPaths = outputFiles.Select(x => Relative(outputDir, x)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sources = Directory.EnumerateFiles(inputDir, "*", SearchOption.AllDirectories)
            .Where(x => ImageExtensions.Contains(Path.GetExtension(x)))
            .Select(x => Relative(inputDir, x))
            .Where(x => File.Exists(Path.Combine(outputDir, x)))
            .Order(StringComparer.Ordinal)
            .ToArray();
        var targetCounts = sources.GroupBy(x => Path.ChangeExtension(x, ".webp"), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Count(), StringComparer.OrdinalIgnoreCase);
        var originalBytes = sources.Sum(x => new FileInfo(Path.Combine(outputDir, x)).Length);
        var staging = Path.Combine(Path.GetTempPath(), "BlogGenerator.Webp", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            var candidates = new Dictionary<string, ConvertedImage>(StringComparer.Ordinal);
            foreach (var source in sources)
            {
                var target = Path.ChangeExtension(source, ".webp");
                if (targetCounts[target] != 1 || existingPaths.Contains(target))
                {
                    await log.WriteLineAsync($"[Images] Keeping {source}: WebP output name collision");
                    continue;
                }
                var sourceFile = Path.Combine(outputDir, source);
                var stagedFile = Path.Combine(staging, Guid.NewGuid().ToString("N") + ".webp");
                try
                {
                    if (MustKeepPng(sourceFile)) continue;
                    using var images = new MagickImageCollection(sourceFile);
                    // 拡張子と中身が異なるファイルやアニメーションを単一フレームに変えない。
                    if (images.Count != 1 || images[0].Format is not (MagickFormat.Jpeg or MagickFormat.Png)) continue;
                    var image = images[0];
                    image.AutoOrient();
                    if (image.GetColorProfile() is not null) image.TransformColorSpace(ColorProfiles.SRGB);
                    else image.ColorSpace = ColorSpace.sRGB;
                    var lossless = image.Format == MagickFormat.Png;
                    image.Strip();
                    image.Quality = 80;
                    image.Settings.SetDefines(new WebPWriteDefines { Lossless = lossless, Exact = lossless });
                    image.Write(stagedFile, MagickFormat.WebP);
                    // 出力を実際に読み直して、壊れた成果物へのリンクを作らない。
                    using var verification = new MagickImage(stagedFile);
                    if (verification.Format != MagickFormat.WebP || verification.Width != image.Width || verification.Height != image.Height)
                        throw new InvalidDataException("WebP verification failed");
                    var bytes = new FileInfo(stagedFile).Length;
                    if (bytes >= new FileInfo(sourceFile).Length) continue;
                    candidates.Add(source, new(target, stagedFile, bytes));
                }
                catch (Exception ex) when (ex is MagickException or IOException or UnauthorizedAccessException)
                {
                    await log.WriteLineAsync($"[Images] Keeping {source}: conversion failed ({ex.Message})");
                }
            }

            if (candidates.Count == 0) return new(sources.Length, 0, originalBytes, originalBytes, siteOriginalBytes, siteOriginalBytes);

            // 最終成果物を検査するため、記事本文だけでなくテーマやフィード由来の参照も含める。
            var texts = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var file in outputFiles)
                if (!BinaryExtensions.Contains(Path.GetExtension(file)))
                    texts.Add(Relative(outputDir, file), await File.ReadAllTextAsync(file));

            var replacements = candidates.ToDictionary(x => x.Key, x => x.Value.Target, StringComparer.Ordinal);
            var rewritten = RewriteTexts(texts, siteUrl, replacements, feedFiles, log);
            var namePattern = new Regex(string.Join('|', candidates.Keys.Select(Path.GetFileName).Distinct()
                .OrderByDescending(x => x!.Length).Select(x => Regex.Escape(x!))), RegexOptions.IgnoreCase);
            var remainingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var text in rewritten.Values)
            {
                // 未対応の属性、JS、JSON、SVG、コード例などに元の名前が残る場合は保守的に保持する。
                var decoded = DecodeReferenceText(text);
                foreach (Match match in namePattern.Matches(decoded)) remainingNames.Add(match.Value);
                // 動的に組み立てる拡張子は対象ファイルを特定できないため、同形式の元画像を保持する。
                foreach (Match match in Regex.Matches(decoded, "(?:[\"']\\.(?<ext>jpe?g|png)[\"']|\\$\\{[^}]*\\}[^\\s\"']*\\.(?<ext>jpe?g|png))", RegexOptions.IgnoreCase))
                    foreach (var source in candidates.Keys.Where(x => Path.GetExtension(x).Equals("." + match.Groups["ext"].Value, StringComparison.OrdinalIgnoreCase)))
                        remainingNames.Add(Path.GetFileName(source));
            }
            var retained = candidates.Keys.Where(x => remainingNames.Contains(Path.GetFileName(x))).ToArray();
            foreach (var source in retained)
            {
                candidates.Remove(source);
                replacements.Remove(source);
            }
            if (retained.Length > 0)
            {
                await log.WriteLineAsync($"[Images] Keeping {retained.Length} image(s): original names remain in unsupported or textual references");
                rewritten = RewriteTexts(texts, siteUrl, replacements, feedFiles, log);
            }

            // WebPを配置してから参照を確定し、最後に元画像を除去する。
            foreach (var image in candidates.Values) File.Copy(image.StagedFile, Path.Combine(outputDir, image.Target));
            foreach (var (path, text) in rewritten)
                if (text != texts[path]) await File.WriteAllTextAsync(Path.Combine(outputDir, path), text, new UTF8Encoding(false));
            var outputBytes = originalBytes;
            foreach (var (source, image) in candidates)
            {
                var file = Path.Combine(outputDir, source);
                outputBytes -= new FileInfo(file).Length - image.Bytes;
                File.Delete(file);
            }
            var siteOutputBytes = Directory.EnumerateFiles(outputDir, "*", SearchOption.AllDirectories).Sum(x => new FileInfo(x).Length);
            return new(sources.Length - candidates.Count, candidates.Count, originalBytes, outputBytes, siteOriginalBytes, siteOutputBytes);
        }
        finally
        {
            Directory.Delete(staging, recursive: true);
        }
    }

    private static Dictionary<string, string> RewriteTexts(
        Dictionary<string, string> texts, Uri siteUrl, Dictionary<string, string> replacements,
        IReadOnlyCollection<string> feedFiles, TextWriter log)
    {
        var urls = new ImageUrlRewriter(siteUrl, replacements);
        var rewriter = new ImageReferenceRewriter(urls);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (path, text) in texts)
        {
            var documentUrl = urls.DocumentUrl(path);
            var extension = Path.GetExtension(path).ToLowerInvariant();
            try
            {
                result[path] = extension switch
                {
                    ".html" or ".htm" => rewriter.RewriteHtml(text, documentUrl),
                    ".css" => rewriter.RewriteCss(text, documentUrl),
                    _ when feedFiles.Contains(path) => rewriter.RewriteFeed(text, documentUrl),
                    _ => text
                };
            }
            catch (System.Xml.XmlException ex)
            {
                log.WriteLine($"[Images] Keeping references in {path}: {ex.Message}");
                result[path] = text;
            }
        }
        return result;
    }

    private static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');

    private static string DecodeReferenceText(string text)
    {
        var decoded = WebUtility.HtmlDecode(text);
        decoded = Regex.Replace(decoded, @"\\u([0-9a-fA-F]{4})", x => ((char)Convert.ToInt32(x.Groups[1].Value, 16)).ToString());
        decoded = Regex.Replace(decoded, @"\\([0-9a-fA-F]{1,6})\s?", x =>
        {
            var code = Convert.ToInt32(x.Groups[1].Value, 16);
            return code is > 0 and <= 0x10ffff && code is not (>= 0xd800 and <= 0xdfff) ? char.ConvertFromUtf32(code) : x.Value;
        });
        decoded = Regex.Replace(decoded, @"\\([^\r\n])", "$1");
        // 静的な文字列連結も検出だけに使用し、スクリプト自体は書き換えない。
        decoded = Regex.Replace(decoded, "[\"']\\s*\\+\\s*[\"']", "");
        return Uri.UnescapeDataString(decoded);
    }

    private static bool MustKeepPng(string file)
    {
        using var stream = File.OpenRead(file);
        Span<byte> header = stackalloc byte[8];
        Span<byte> dimensionsAndDepth = stackalloc byte[9];
        if (stream.Read(header) != 8 || !header.SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return false;
        while (stream.Read(header) == 8)
        {
            var type = Encoding.ASCII.GetString(header[4..]);
            if (type == "acTL") return true;
            if (type == "IDAT") return false;
            var length = (long)BinaryPrimitives.ReadUInt32BigEndian(header) + 4;
            if (length > stream.Length - stream.Position) return false;
            if (type == "IHDR" && length >= 17)
            {
                if (stream.Read(dimensionsAndDepth) != 9) return false;
                // WebPは8bitまでのため、高ビット深度のPNGを量子化しない。
                if (dimensionsAndDepth[8] > 8) return true;
                length -= 9;
            }
            stream.Seek(length, SeekOrigin.Current);
        }
        return false;
    }
    private sealed record ConvertedImage(string Target, string StagedFile, long Bytes);
}

internal sealed record WebpOptimizationResult(int Kept, int Converted, long OriginalBytes, long OutputBytes, long SiteOriginalBytes, long SiteOutputBytes)
{
    public override string ToString() => string.Create(CultureInfo.InvariantCulture,
        $"converted: {Converted}, kept: {Kept}, image bytes: {OriginalBytes:N0} → {OutputBytes:N0}, saved: {(OriginalBytes == 0 ? 0 : 100d * (OriginalBytes - OutputBytes) / OriginalBytes):F1}%, site bytes: {SiteOriginalBytes:N0} → {SiteOutputBytes:N0}");
}
