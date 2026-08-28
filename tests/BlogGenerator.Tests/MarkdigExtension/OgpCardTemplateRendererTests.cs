using BlogGenerator.MarkdigExtension;
using RazorLight;
using NUnit.Framework;

namespace BlogGenerator.Tests.MarkdigExtension;

[TestFixture]
public class OgpCardTemplateRendererTests
{
    private string _testRootPath = null!;

    [SetUp]
    public void SetUp()
    {
        _testRootPath = Path.Combine(Path.GetTempPath(), "BlogGeneratorTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testRootPath);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_testRootPath))
        {
            Directory.Delete(_testRootPath, true);
        }
    }

    [Test]
    public async Task テーマ未配置時は埋め込みテンプレートを描画できる()
    {
        var renderer = CreateRenderer();

        var html = await renderer.RenderAsync(CreateModel("タイトル & 詳細"));

        Assert.Multiple(() =>
        {
            // 組み込み版は既存テーマとの互換性のため旧wrapper classを外側に残す
            Assert.That(html, Does.StartWith("<div class=\"bcard-wrapper\">"));
            Assert.That(html, Does.Contain("class=\"ogp-card\""));
            Assert.That(html, Does.Contain("&amp;"));
            Assert.That(html, Does.Not.Contain("タイトル & 詳細"));
            Assert.That(html, Does.Contain("class=\"ogp-card-image\""));
        });
    }

    [Test]
    public async Task テーマ側テンプレートを埋め込みテンプレートより優先する()
    {
        var embedDirectory = Path.Combine(_testRootPath, ".embeds");
        Directory.CreateDirectory(embedDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(embedDirectory, "ogp.cshtml"),
            "@model BlogGenerator.MarkdigExtension.OgpCardModel\n<div class=\"custom-ogp-card\">@Model.Title</div>");
        var renderer = CreateRenderer();

        var html = await renderer.RenderAsync(CreateModel("テーマ版"));

        Assert.Multiple(() =>
        {
            Assert.That(html, Does.Contain("class=\"custom-ogp-card\""));
            Assert.That(html, Does.Not.Contain("class=\"bcard-wrapper\""));
        });
    }

    [Test]
    public async Task 画像URLがない場合は画像領域自体を出力しない()
    {
        var renderer = CreateRenderer();
        var model = CreateModel("画像なし") with { ImageUrl = null };

        var html = await renderer.RenderAsync(model);

        Assert.That(html, Does.Not.Contain("ogp-card-image-wrapper"));
    }

    private OgpCardTemplateRenderer CreateRenderer()
    {
        var themeEngine = new RazorLightEngineBuilder()
            .UseFileSystemProject(_testRootPath)
            .UseMemoryCachingProvider()
            .Build();

        return new OgpCardTemplateRenderer(themeEngine, _testRootPath);
    }

    private static OgpCardModel CreateModel(string title) => new(
        "https://example.com/post",
        "example.com/post",
        title,
        "説明",
        "Example",
        "https://example.com/image.png",
        "https://www.google.com/s2/favicons?domain_url=https%3A%2F%2Fexample.com%2Fpost&sz=32");
}
