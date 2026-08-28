using BlogGenerator.Core;
using BlogGenerator.Core.Interfaces;
using BlogGenerator.Models;
using NUnit.Framework;

namespace BlogGenerator.Tests.Core;

public class ScheduledContentValidatorTests
{
    [Test]
    public async Task 予約コンテンツをすべて検証する()
    {
        var pageGenerator = new RecordingPageGenerator();
        var validator = new ScheduledContentValidator(pageGenerator);
        var published = new[] { CreateArticle("published.html", "Published") };
        var scheduled = new[]
        {
            CreateArticle("first.html", "First"),
            CreateArticle("second.html", "Second")
        };

        await validator.ValidateAsync(scheduled, published);

        Assert.Multiple(() =>
        {
            Assert.That(pageGenerator.SideBarGenerationCount, Is.EqualTo(1));
            Assert.That(pageGenerator.ValidatedArticles.Select(x => x.FileName), Is.EqualTo(new[] { "first.html", "second.html" }));
            Assert.That(pageGenerator.PublishedArticleCounts, Is.EqualTo(new[] { 1, 1 }));
        });
    }

    [Test]
    public void 複数の検証エラーをまとめて通知する()
    {
        var pageGenerator = new RecordingPageGenerator { ThrowOnValidation = true };
        var validator = new ScheduledContentValidator(pageGenerator);
        var scheduled = new[]
        {
            CreateArticle("first.html", "First"),
            CreateArticle("second.html", "Second")
        };

        var exception = Assert.ThrowsAsync<AggregateException>(() => validator.ValidateAsync(scheduled, []));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.InnerExceptions, Has.Count.EqualTo(2));
            Assert.That(exception.InnerExceptions[0].Message, Does.Contain("first.html"));
            Assert.That(exception.InnerExceptions[1].Message, Does.Contain("second.html"));
        });
    }

    private static Article CreateArticle(string fileName, string title) => new(
        FileName: fileName,
        Title: title,
        Body: "<p>body</p>",
        Tags: [],
        Published: DateTimeOffset.Parse("2026-08-29T10:00:00+09:00"),
        RelativeDirectoryPath: string.Empty,
        RootRelativeDirectoryPath: "/blog",
        IsFixedPage: false);

    private sealed class RecordingPageGenerator : IPageGenerator
    {
        public int SideBarGenerationCount { get; private set; }
        public List<Article> ValidatedArticles { get; } = [];
        public List<int> PublishedArticleCounts { get; } = [];
        public bool ThrowOnValidation { get; init; }

        public Task<TrustedHtml> GenerateSideBarHtmlAsync(List<Article> articles)
        {
            SideBarGenerationCount++;
            return Task.FromResult(new TrustedHtml("<aside>test</aside>"));
        }

        public Task ValidateArticlePageAsync(Article article, List<Article> publishedArticles, TrustedHtml sideBarHtml)
        {
            ValidatedArticles.Add(article);
            PublishedArticleCounts.Add(publishedArticles.Count);
            if (ThrowOnValidation) throw new InvalidOperationException("validation failed");
            return Task.CompletedTask;
        }

        public Task GenerateArticlePagesAsync(List<Article> articles, string outputDir, TrustedHtml sideBarHtml) => Task.CompletedTask;
        public Task GenerateIndexPagesAsync(List<Article> articles, string outputDir, TrustedHtml sideBarHtml) => Task.CompletedTask;
        public Task GenerateTagPagesAsync(List<Article> articles, string outputDir, TrustedHtml sideBarHtml) => Task.CompletedTask;
        public Task GenerateArchivePagesAsync(List<Article> articles, string outputDir, TrustedHtml sideBarHtml) => Task.CompletedTask;
    }
}
