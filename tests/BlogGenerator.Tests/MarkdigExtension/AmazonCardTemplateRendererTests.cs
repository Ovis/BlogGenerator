using BlogGenerator.MarkdigExtension;
using RazorLight;
using NUnit.Framework;

namespace BlogGenerator.Tests.MarkdigExtension;

[TestFixture]
public class AmazonCardTemplateRendererTests
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

        var html = await renderer.RenderAsync(new AmazonCardModel(
            "4844339648",
            "https://www.amazon.co.jp/dp/4844339648/?tag=test-tag",
            "商品 & 名",
            "https://m.media-amazon.com/images/I/91+IeF0u9eL._SL500_.jpg"));

        Assert.Multiple(() =>
        {
            Assert.That(html, Does.StartWith("<span class=\"amazon-card\""));
            Assert.That(html, Does.Contain("&amp;"));
            Assert.That(html, Does.Not.Contain("商品 & 名"));
            Assert.That(html, Does.Contain("class=\"amazon-card-image\""));
        });
    }

    [Test]
    public async Task テーマ側テンプレートを埋め込みテンプレートより優先する()
    {
        var embedDirectory = Path.Combine(_testRootPath, ".embeds");
        Directory.CreateDirectory(embedDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(embedDirectory, "amazon.cshtml"),
            "@model BlogGenerator.MarkdigExtension.AmazonCardModel\n<div class=\"custom-amazon-card\">@Model.Title</div>");
        var renderer = CreateRenderer();

        var html = await renderer.RenderAsync(new AmazonCardModel(
            "4844339648",
            "https://www.amazon.co.jp/dp/4844339648/?tag=test-tag",
            "商品名",
            null));

        Assert.Multiple(() =>
        {
            Assert.That(html, Does.Contain("class=\"custom-amazon-card\""));
            Assert.That(html, Does.Not.Contain("class=\"amazon-card\""));
        });
    }

    private AmazonCardTemplateRenderer CreateRenderer()
    {
        var themeEngine = new RazorLightEngineBuilder()
            .UseFileSystemProject(_testRootPath)
            .UseMemoryCachingProvider()
            .Build();

        return new AmazonCardTemplateRenderer(themeEngine, _testRootPath);
    }
}
