using BlogGenerator.Core;
using BlogGenerator.Models;
using NUnit.Framework;
using RazorLight;

namespace BlogGenerator.Tests.Core;

[TestFixture]
[NonParallelizable]
public class FixedPageTemplateTests
{
    private string _root = null!;
    private string _theme = null!;
    private string _output = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "BlogGenerator.FixedPageTests", Guid.NewGuid().ToString("N"));
        _theme = Path.Combine(_root, "theme");
        _output = Path.Combine(_root, "output");
        Directory.CreateDirectory(_theme);
        Directory.CreateDirectory(_output);

        File.WriteAllText(Path.Combine(_theme, "Layout.cshtml"), "@using RazorLight\n@using BlogGenerator.Models\n@inherits TemplatePage<PageModel>\n<html><body>@{ await IncludeAsync(Model.ContentTemplate, Model); }</body></html>");
        File.WriteAllText(Path.Combine(_theme, "Content.cshtml"), "@using RazorLight\n@using BlogGenerator.Models\n@inherits TemplatePage<PageModel>\n<article>ARTICLE:@Model.Articles.First().Title</article>");
        File.WriteAllText(Path.Combine(_theme, "Page.cshtml"), "@using RazorLight\n@using BlogGenerator.Models\n@inherits TemplatePage<PageModel>\n<article>PAGE:@Raw(Model.Articles.First().BodyHtml.Value)</article>");
        File.WriteAllText(Path.Combine(_theme, "Search.cshtml"), "@using RazorLight\n@using BlogGenerator.Models\n@inherits TemplatePage<PageModel>\n<article>SEARCH:@Raw(Model.Articles.First().BodyHtml.Value)</article>");
        File.WriteAllText(Path.Combine(_theme, "SideBar.cshtml"), "@using RazorLight\n@using BlogGenerator.Models\n@inherits TemplatePage<SideBarModel>\n<aside>@string.Join(\",\", Model.Articles.Select(x => x.Title))</aside>");
        File.WriteAllText(Path.Combine(_theme, "PageList.cshtml"), "@using RazorLight\n@using BlogGenerator.Models\n@inherits TemplatePage<PageModel>\n<div>@string.Join(\",\", Model.Articles.Select(x => x.Title))</div>");
        File.WriteAllText(Path.Combine(_theme, "Tag.cshtml"), "@using RazorLight\n@using BlogGenerator.Models\n@inherits TemplatePage<PageModel>\n<div>@string.Join(\",\", Model.Articles.Select(x => x.Title))</div>");
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    [Test]
    public async Task 固定ページはTemplateを大文字小文字を区別せず解決できる()
    {
        var generator = CreateGenerator();
        var page = CreateArticle("search.html", "Search page", true, template: "search");

        await generator.GenerateArticlePagesAsync([page], _output, TrustedHtml.Empty);

        var html = await File.ReadAllTextAsync(Path.Combine(_output, "search.html"));
        Assert.That(html, Does.Contain("SEARCH:<p>body</p>"));
    }

    [Test]
    public async Task Template未指定の固定ページはPageテンプレートを使用する()
    {
        var generator = CreateGenerator();
        var page = CreateArticle("about.html", "About", true);

        await generator.GenerateArticlePagesAsync([page], _output, TrustedHtml.Empty);

        var html = await File.ReadAllTextAsync(Path.Combine(_output, "about.html"));
        Assert.That(html, Does.Contain("PAGE:<p>body</p>"));
    }

    [Test]
    public void 不正なTemplate名はエラーにする()
    {
        var generator = CreateGenerator();
        var page = CreateArticle("bad.html", "Bad", true, template: "../Search");

        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await generator.GenerateArticlePagesAsync([page], _output, TrustedHtml.Empty));

        Assert.That(exception!.Message, Does.Contain("Invalid fixed page template name"));
    }

    [Test]
    public async Task 固定ページはサイドバーと記事一覧とタグから除外する()
    {
        var generator = CreateGenerator();
        var article = CreateArticle("article.html", "Normal article", false, tags: ["normal"]);
        var fixedPage = CreateArticle("about.html", "Fixed page", true, tags: ["fixed"]);
        var pages = new List<Article> { article, fixedPage };

        var sidebar = await generator.GenerateSideBarHtmlAsync(pages);
        await generator.GenerateIndexPagesAsync(pages, _output, sidebar);
        await generator.GenerateTagPagesAsync(pages, _output, sidebar);

        var index = await File.ReadAllTextAsync(Path.Combine(_output, "index.html"));
        var tagIndex = await File.ReadAllTextAsync(Path.Combine(_output, "tags", "index.html"));

        Assert.Multiple(() =>
        {
            Assert.That(sidebar.Value, Does.Contain("Normal article"));
            Assert.That(sidebar.Value, Does.Not.Contain("Fixed page"));
            Assert.That(index, Does.Contain("Normal article"));
            Assert.That(index, Does.Not.Contain("Fixed page"));
            Assert.That(tagIndex, Does.Contain("Normal article"));
            Assert.That(tagIndex, Does.Not.Contain("Fixed page"));
            Assert.That(Directory.Exists(Path.Combine(_output, "tags", "fixed")), Is.False);
        });
    }

    private PageGenerator CreateGenerator()
    {
        var siteOption = new SiteOption
        {
            SiteName = "Test",
            SiteDescription = "Test",
            SiteUrl = "https://example.com/"
        };
        var engine = new RazorLightEngineBuilder()
            .UseFileSystemProject(_theme)
            .UseMemoryCachingProvider()
            .Build();

        return new PageGenerator(engine, siteOption, new FileSystemHelper(), new ThemeSettings(_theme));
    }

    private static Article CreateArticle(
        string fileName,
        string title,
        bool isFixedPage,
        string template = "",
        List<string>? tags = null)
    {
        return new Article(
            FileName: fileName,
            Title: title,
            Body: "<p>body</p>",
            Tags: tags ?? [],
            Published: DateTimeOffset.Parse("2026-08-27T12:00:00+09:00"),
            RelativeDirectoryPath: string.Empty,
            RootRelativeDirectoryPath: "/",
            IsFixedPage: isFixedPage,
            Template: template);
    }
}
