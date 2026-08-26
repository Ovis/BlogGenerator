using System.Text;
using BlogGenerator.Models;
using NUnit.Framework;

namespace BlogGenerator.Tests.Models;

[TestFixture]
public class TagCatalogTests
{
    [Test]
    public void 大文字小文字と空白表記揺れを同一タグとして統合し最多表記を採用する()
    {
        var articles = new[]
        {
            CreateArticle("a.html", [" github "]),
            CreateArticle("b.html", ["GitHub"]),
            CreateArticle("c.html", ["GitHub"]),
            CreateArticle("d.html", ["GITHUB"])
        };

        var catalog = TagCatalog.Build(articles);
        var entry = catalog.Entries.Single();

        Assert.Multiple(() =>
        {
            Assert.That(entry.DisplayName, Is.EqualTo("GitHub"));
            Assert.That(entry.Slug, Is.EqualTo("github"));
            Assert.That(entry.Count, Is.EqualTo(4));
            Assert.That(catalog.TryGet(" github ", out var resolved), Is.True);
            Assert.That(resolved, Is.EqualTo(entry));
        });
    }

    [Test]
    public void NFCと連続空白を正規化する()
    {
        var decomposed = "カ\u3099  イド";
        var catalog = TagCatalog.Build([CreateArticle("a.html", [decomposed])]);
        var entry = catalog.Entries.Single();

        Assert.Multiple(() =>
        {
            Assert.That(entry.DisplayName, Is.EqualTo("ガ イド"));
            Assert.That(entry.Slug, Is.EqualTo("ガ-イド"));
        });
    }

    [TestCase("Windows Mobile", "windows-mobile")]
    [TestCase("Windows-Mobile", "windows~2Dmobile")]
    [TestCase("foo~bar", "foo~7Ebar")]
    [TestCase("C#/ASP.NET", "c~23~2Fasp.net")]
    [TestCase(".NET", "~2Enet")]
    [TestCase("foo.", "foo~2E")]
    [TestCase("CON", "_~con")]
    public void slug規則に従って安全なパスセグメントを生成する(string tag, string expectedSlug)
    {
        var normalized = TagCatalog.NormalizeDisplayValue(tag);
        var identity = TagCatalog.CreateIdentityKey(normalized);

        Assert.That(TagCatalog.CreateSlug(identity), Is.EqualTo(expectedSlug));
    }

    [Test]
    public void 記号と絵文字はUTF8バイト単位でエスケープする()
    {
        var identity = TagCatalog.CreateIdentityKey(TagCatalog.NormalizeDisplayValue("旅行✈️📘"));
        var slug = TagCatalog.CreateSlug(identity);

        Assert.Multiple(() =>
        {
            Assert.That(slug, Does.StartWith("旅行~E2~9C~88"));
            Assert.That(slug, Does.Contain("~F0~9F~93~98"));
        });
    }

    [Test]
    public void 長いslugは120バイト以内に短縮して決定的なhashを付ける()
    {
        var tag = string.Concat(Enumerable.Repeat("日本語タグ", 30));
        var identity = TagCatalog.CreateIdentityKey(TagCatalog.NormalizeDisplayValue(tag));

        var first = TagCatalog.CreateSlug(identity);
        var second = TagCatalog.CreateSlug(identity);

        Assert.Multiple(() =>
        {
            Assert.That(Encoding.UTF8.GetByteCount(first), Is.LessThanOrEqualTo(120));
            Assert.That(first, Is.EqualTo(second));
            Assert.That(first, Does.Match("~[0-9a-f]{16}$"));
        });
    }

    [Test]
    public void 空タグは警告して無視する()
    {
        var warnings = new List<string>();
        var catalog = TagCatalog.Build([CreateArticle("empty.html", ["   "])], warnings.Add);

        Assert.Multiple(() =>
        {
            Assert.That(catalog.Entries, Is.Empty);
            Assert.That(warnings, Has.Count.EqualTo(1));
            Assert.That(warnings[0], Does.Contain("empty.html"));
        });
    }

    private static Article CreateArticle(string fileName, string[] tags) =>
        new(
            FileName: fileName,
            Title: fileName,
            Body: "<p>body</p>",
            Tags: tags,
            Published: DateTimeOffset.Parse("2026-08-26T00:00:00+09:00"),
            RelativeDirectoryPath: "posts",
            RootRelativeDirectoryPath: "/blog/posts",
            IsFixedPage: false);
}
