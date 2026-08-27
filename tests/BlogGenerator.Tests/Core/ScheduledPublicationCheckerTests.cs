using BlogGenerator.Core;
using BlogGenerator.Models;
using NUnit.Framework;

namespace BlogGenerator.Tests.Core;

public class ScheduledPublicationCheckerTests
{
    private string _dir = null!;
    private readonly TimeZoneInfo _timeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo");

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "bloggen-scheduled-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [Test]
    public void Check_UsesOpenClosedIntervalAndSortsResults()
    {
        Write("b.md", "2026-08-28T01:45:00+09:00");
        Write("a.md", "2026-08-28T01:45:00+09:00");
        Write("after.md", "2026-08-28T01:40:00+09:00");
        Write("until.md", "2026-08-28T01:50:00+09:00");
        Write("future.md", "2026-08-28T01:50:01+09:00");

        var result = new ScheduledPublicationChecker(_timeZone).Check(_dir, DateTimeOffset.Parse("2026-08-27T16:40:00Z"), DateTimeOffset.Parse("2026-08-27T16:50:00Z"));

        Assert.Multiple(() =>
        {
            Assert.That(result.HasScheduled, Is.True);
            Assert.That(result.Count, Is.EqualTo(3));
            Assert.That(result.Items.Select(x => x.Path), Is.EqualTo(new[] { "a.md", "b.md", "until.md" }));
        });
    }

    [Test]
    public void Check_IgnoresDraftAndFrontMatterlessMarkdown()
    {
        File.WriteAllText(Path.Combine(_dir, "draft.md"), "---\nTitle: Draft\n---\nbody");
        File.WriteAllText(Path.Combine(_dir, "plain.MD"), "body");
        var result = new ScheduledPublicationChecker(_timeZone).Check(_dir, DateTimeOffset.Parse("2026-08-27T16:40:00Z"), DateTimeOffset.Parse("2026-08-27T16:50:00Z"));
        Assert.Multiple(() =>
        {
            Assert.That(result.HasScheduled, Is.False);
            Assert.That(result.Items, Is.Empty);
        });
    }

    [Test]
    public void Check_CollectsErrorsInPathOrder()
    {
        File.WriteAllText(Path.Combine(_dir, "b.md"), "---\nPublished: invalid\n---\nbody");
        File.WriteAllText(Path.Combine(_dir, "a.md"), "---\nPublished:\n---\nbody");
        var ex = Assert.Throws<ScheduledPublicationCheckException>(() => new ScheduledPublicationChecker(_timeZone).Check(_dir, DateTimeOffset.Parse("2026-08-27T16:40:00Z"), DateTimeOffset.Parse("2026-08-27T16:50:00Z")));
        Assert.That(ex!.Errors.Select(x => x.Path), Is.EqualTo(new[] { "a.md", "b.md" }));
    }

    [Test]
    public void Check_RejectsEmptyInterval()
    {
        Assert.Throws<ArgumentException>(() => new ScheduledPublicationChecker(_timeZone).Check(_dir, DateTimeOffset.Parse("2026-08-27T16:50:00Z"), DateTimeOffset.Parse("2026-08-27T16:50:00Z")));
    }

    private void Write(string path, string published) => File.WriteAllText(Path.Combine(_dir, path), $"---\nTitle: Test\nPublished: {published}\nIsFixedPage: true\n---\nbody");
}
