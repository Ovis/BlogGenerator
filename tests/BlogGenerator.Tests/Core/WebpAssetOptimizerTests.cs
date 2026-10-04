using BlogGenerator.Core;
using ImageMagick;
using NUnit.Framework;

namespace BlogGenerator.Tests.Core;

[TestFixture]
public class WebpAssetOptimizerTests
{
    private string _root = null!;
    private string Input => Path.Combine(_root, "input");
    private string Output => Path.Combine(_root, "output");
    private static readonly Uri Site = new("https://example.test/");

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "BlogGenerator.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Input);
        Directory.CreateDirectory(Output);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_root, recursive: true);

    [TestCase(1)]
    [TestCase(2)]
    [TestCase(4)]
    [TestCase(null)]
    public async Task 変換した画像だけを出力しHTMLとCSSとフィードを更新してソースを保持する(int? parallelism)
    {
        CreateImage("posts/photo.jpg");
        CreateImage("posts/transparent.png", transparent: true);
        await WriteOutput("posts/page.html", "<img src=\"photo.jpg?x=1#part\"><a href=\"transparent.png\">open</a>");
        await WriteOutput("css/site.css", "body { background:url('../posts/photo.jpg'); }");
        await WriteOutput("feed.rss", "<rss><channel><item><description>&lt;img src=\"/posts/photo.jpg\"&gt;</description></item></channel></rss>");
        var source = await File.ReadAllBytesAsync(Path.Combine(Input, "posts/photo.jpg"));
        using var log = new StringWriter();
        var result = await Optimize(log, parallelism);

        using var transparent = new MagickImage(Path.Combine(Output, "posts/transparent.webp"));
        Assert.Multiple(() =>
        {
            Assert.That(result.Converted, Is.EqualTo(2));
            Assert.That(result.OutputBytes, Is.LessThan(result.OriginalBytes));
            Assert.That(File.Exists(Path.Combine(Output, "posts/photo.jpg")), Is.False);
            Assert.That(File.Exists(Path.Combine(Output, "posts/transparent.png")), Is.False);
            Assert.That(File.Exists(Path.Combine(Output, "posts/photo.webp")), Is.True);
            Assert.That(File.ReadAllText(Path.Combine(Output, "posts/page.html")), Does.Contain("photo.webp?x=1#part"));
            Assert.That(File.ReadAllText(Path.Combine(Output, "css/site.css")), Does.Contain("../posts/photo.webp"));
            Assert.That(File.ReadAllText(Path.Combine(Output, "feed.rss")), Does.Contain("/posts/photo.webp"));
            Assert.That(File.ReadAllBytes(Path.Combine(Input, "posts/photo.jpg")), Is.EqualTo(source));
            Assert.That(transparent.Width, Is.EqualTo(64));
            Assert.That(transparent.Height, Is.EqualTo(32));
            Assert.That(transparent.HasAlpha, Is.True);
            Assert.That(log.ToString(), Does.Contain($"Parallelism: {WebpAssetOptimizer.ResolveParallelism(parallelism, Environment.ProcessorCount, 2)}"));
            Assert.That(log.ToString(), Does.Contain(parallelism is null ? "automatic" : "specified"));
        });
        using var original = new MagickImage(Path.Combine(Input, "posts/transparent.png"));
        Assert.That(transparent.Compare(original, ErrorMetric.Absolute), Is.Zero, "PNG pixels are preserved losslessly");
        using var originalPixels = original.GetPixels();
        using var convertedPixels = transparent.GetPixels();
        var expectedPixel = originalPixels.GetPixel(0, 0).ToColor()!;
        var actualPixel = convertedPixels.GetPixel(0, 0).ToColor()!;
        Assert.That(new[] { actualPixel.R, actualPixel.G, actualPixel.B, actualPixel.A },
            Is.EqualTo(new[] { expectedPixel.R, expectedPixel.G, expectedPixel.B, expectedPixel.A }),
            "Fully transparent pixels also preserve their RGB values");
    }

    [Test]
    public async Task 既存WebPと出力名衝突と破損画像は保持する()
    {
        CreateImage("same.jpg");
        CreateImage("same.png");
        CreateImage("existing.jpg");
        await WriteOutput("existing.webp", "existing content");
        await File.WriteAllTextAsync(Path.Combine(Input, "broken.jpg"), "broken");
        await WriteOutput("broken.jpg", "broken");
        await WriteOutput("index.html", "<img src=\"same.jpg\"><img src=\"existing.jpg\"><img src=\"broken.jpg\">");
        using var log = new StringWriter();
        var result = await Optimize(log);

        Assert.Multiple(() =>
        {
            Assert.That(result.Converted, Is.Zero);
            Assert.That(result.Kept, Is.EqualTo(4));
            Assert.That(File.Exists(Path.Combine(Output, "same.jpg")), Is.True);
            Assert.That(File.Exists(Path.Combine(Output, "same.png")), Is.True);
            Assert.That(File.ReadAllText(Path.Combine(Output, "existing.webp")), Is.EqualTo("existing content"));
            Assert.That(log.ToString(), Does.Contain("collision").And.Contain("conversion failed"));
        });
    }

    [TestCase("dynamic.js", "const original = 'photo.jpg';")]
    [TestCase("data.json", "{\"image\":\"photo%2Ejpg\"}")]
    [TestCase("icon.svg", "<svg><image href=\"photo.jpg\"/></svg>")]
    [TestCase("index.html", "<source type=\"image/jpeg\" srcset=\"photo.jpg 1x\">")]
    [TestCase("unicode.js", "const original = 'photo\\u002ejpg';")]
    [TestCase("concat.js", "const original = 'photo' + '.jpg';")]
    [TestCase("dynamic.js", "const original = `${name}.jpg`;")]
    [TestCase("image-set.css", "body { background:image-set(url('photo.jpg') type('image/jpeg')); }")]
    [TestCase("escaped.css", "body { background:url(photo\\.jpg); }")]
    public async Task 未対応の参照が残る画像はWebPも作らず元画像を保持する(string file, string reference)
    {
        CreateImage("photo.jpg");
        await WriteOutput(file, reference);
        await WriteOutput("page.html", "<img src=\"photo.jpg\">");
        var result = await Optimize(TextWriter.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result.Converted, Is.Zero);
            Assert.That(File.Exists(Path.Combine(Output, "photo.jpg")), Is.True);
            Assert.That(File.Exists(Path.Combine(Output, "photo.webp")), Is.False);
            Assert.That(File.ReadAllText(Path.Combine(Output, "page.html")), Is.EqualTo("<img src=\"photo.jpg\">"));
        });
    }

    [Test]
    public async Task GIFとテーマ画像は変換しない()
    {
        using var image = new MagickImage(MagickColors.Red, 64, 32);
        image.Write(Path.Combine(Input, "animation.gif"));
        File.Copy(Path.Combine(Input, "animation.gif"), Path.Combine(Output, "animation.gif"));
        image.Write(Path.Combine(Output, "theme.png"));
        // 拡張子だけPNGに見えるGIFも保持する。
        File.Copy(Path.Combine(Input, "animation.gif"), Path.Combine(Input, "disguised.png"));
        File.Copy(Path.Combine(Input, "disguised.png"), Path.Combine(Output, "disguised.png"));
        var result = await Optimize(TextWriter.Null);
        Assert.That(result.Converted, Is.Zero);
        Assert.That(Directory.GetFiles(Output).Length, Is.EqualTo(3));
    }

    [Test]
    public async Task 成果物を再処理しても変換済み画像を再圧縮しない()
    {
        CreateImage("photo.jpg");
        await WriteOutput("page.html", "<img src=\"photo.jpg\">");
        await Optimize(TextWriter.Null);
        var bytes = await File.ReadAllBytesAsync(Path.Combine(Output, "photo.webp"));
        var result = await Optimize(TextWriter.Null);
        Assert.That(result.Converted, Is.Zero);
        Assert.That(await File.ReadAllBytesAsync(Path.Combine(Output, "photo.webp")), Is.EqualTo(bytes));
    }

    [Test]
    public async Task EXIFの回転を画素へ適用する()
    {
        using (var image = new MagickImage(MagickColors.Red, 64, 32))
        {
            var profile = new ExifProfile();
            profile.SetValue(ExifTag.Orientation, (ushort)6);
            image.SetProfile(profile);
            image.Orientation = OrientationType.RightTop;
            image.Comment = new string('x', 4000);
            image.Write(Path.Combine(Input, "rotated.jpg"));
        }
        File.Copy(Path.Combine(Input, "rotated.jpg"), Path.Combine(Output, "rotated.jpg"));
        using (var original = new MagickImage(Path.Combine(Input, "rotated.jpg")))
            Assert.That(original.Orientation, Is.EqualTo(OrientationType.RightTop));
        await WriteOutput("page.html", "<img src=\"rotated.jpg\">");
        var result = await Optimize(TextWriter.Null);
        using var converted = new MagickImage(Path.Combine(Output, "rotated.webp"));
        Assert.Multiple(() =>
        {
            Assert.That(result.Converted, Is.EqualTo(1));
            Assert.That(converted.Width, Is.EqualTo(32));
            Assert.That(converted.Height, Is.EqualTo(64));
            Assert.That(converted.GetExifProfile(), Is.Null);
        });
    }

    [Test]
    public async Task WebPで容量が増える画像は元形式だけを出力する()
    {
        using (var image = new MagickImage(MagickColors.Black, 128, 128))
        {
            using (var pixels = image.GetPixels())
                for (var y = 0; y < 128; y++)
                    for (var x = 0; x < 128; x++)
                        pixels.SetPixel(x, y, [(byte)(x * y), (byte)(x + y), (byte)(x - y)]);
            image.Strip();
            image.Write(Path.Combine(Input, "pattern.png"));
        }
        File.Copy(Path.Combine(Input, "pattern.png"), Path.Combine(Output, "pattern.png"));
        await WriteOutput("page.html", "<img src=\"pattern.png\">");
        var result = await Optimize(TextWriter.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result.Converted, Is.Zero);
            Assert.That(result.OutputBytes, Is.EqualTo(result.OriginalBytes));
            Assert.That(File.Exists(Path.Combine(Output, "pattern.png")), Is.True);
            Assert.That(File.Exists(Path.Combine(Output, "pattern.webp")), Is.False);
            Assert.That(File.ReadAllText(Path.Combine(Output, "page.html")), Is.EqualTo("<img src=\"pattern.png\">"));
        });
    }

    [Test]
    public async Task アニメーションPNGの制御チャンクがあれば単一フレームへ変換しない()
    {
        CreateImage("animated.png");
        var png = await File.ReadAllBytesAsync(Path.Combine(Input, "animated.png"));
        // PNGシグネチャの直後にacTLを挿入。検査がデコーダより先に働くことも確認する。
        byte[] control = [0, 0, 0, 8, 97, 99, 84, 76, 0, 0, 0, 2, 0, 0, 0, 0, 0, 0, 0, 0];
        await File.WriteAllBytesAsync(Path.Combine(Output, "animated.png"), png[..8].Concat(control).Concat(png[8..]).ToArray());
        await WriteOutput("page.html", "<img src=\"animated.png\">");
        var result = await Optimize(TextWriter.Null);
        Assert.That(result.Converted, Is.Zero);
        Assert.That(File.Exists(Path.Combine(Output, "animated.png")), Is.True);
    }

    [Test]
    public async Task 高ビット深度のPNGは量子化せず元形式で保持する()
    {
        using (var image = new MagickImage(MagickColors.Red, 64, 32))
        {
            image.Settings.SetDefine(MagickFormat.Png, "bit-depth", "16");
            image.Write(Path.Combine(Input, "high-depth.png"));
        }
        var original = await File.ReadAllBytesAsync(Path.Combine(Input, "high-depth.png"));
        Assert.That(original[24], Is.EqualTo(16));
        File.Copy(Path.Combine(Input, "high-depth.png"), Path.Combine(Output, "high-depth.png"));
        var result = await Optimize(TextWriter.Null);
        Assert.That(result.Converted, Is.Zero);
        Assert.That(await File.ReadAllBytesAsync(Path.Combine(Output, "high-depth.png")), Is.EqualTo(original));
    }

    [TestCase(null, 1, 100, 1)]
    [TestCase(null, 2, 100, 2)]
    [TestCase(null, 8, 100, 4)]
    [TestCase(null, 8, 2, 2)]
    [TestCase(null, 4, 0, 1)]
    [TestCase(1, 8, 100, 1)]
    [TestCase(8, 2, 100, 8)]
    [TestCase(8, 2, 3, 3)]
    public void CPU数と画像数による自動設定を明示指定で上書きできる(int? requested, int processors, int images, int expected)
    {
        Assert.That(WebpAssetOptimizer.ResolveParallelism(requested, processors, images), Is.EqualTo(expected));
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void 不正な並列数では画像に触れず失敗する(int parallelism)
    {
        // CLIを経由しない呼び出しでも、ファイル操作より前に拒否する。
        using var log = new StringWriter();
        Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => new WebpAssetOptimizer().OptimizeAsync(
            Path.Combine(_root, "missing-input"), Path.Combine(_root, "missing-output"), Site, [], log, parallelism));
    }

    private Task<WebpOptimizationResult> Optimize(TextWriter log, int? parallelism = null) => new WebpAssetOptimizer().OptimizeAsync(
        Input, Output, Site, ["feed.rss", "feed.atom"], log, parallelism);

    private void CreateImage(string relative, bool transparent = false)
    {
        var source = Path.Combine(Input, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        using var image = new MagickImage(transparent ? new MagickColor("#aabbcc80") : MagickColors.CornflowerBlue, 64, 32);
        if (transparent)
        {
            using var pixels = image.GetPixels();
            pixels.SetPixel(0, 0, [19, 54, 211, 0]);
        }
        image.Comment = new string('x', 4000);
        image.Write(source);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(Output, relative))!);
        File.Copy(source, Path.Combine(Output, relative));
    }

    private async Task WriteOutput(string relative, string text)
    {
        var file = Path.Combine(Output, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await File.WriteAllTextAsync(file, text);
    }
}
