using BlogGenerator.Core;
using NUnit.Framework;

namespace BlogGenerator.Tests.Core;

[TestFixture]
public class BuildProgressReporterTests
{
    [Test]
    public void ビルド開始情報を一貫した形式で出力する()
    {
        using var writer = new StringWriter();
        var reporter = new BuildProgressReporter(writer);
        var buildTime = new DateTimeOffset(2026, 8, 28, 23, 30, 45, TimeSpan.FromHours(9));

        reporter.WriteBuildStarted(buildTime, "Asia/Tokyo");

        Assert.That(
            writer.ToString(),
            Is.EqualTo(
                "[Build] Started: 2026-08-28 23:30:45 +09:00" + Environment.NewLine +
                "[Build] Time zone: Asia/Tokyo" + Environment.NewLine));
    }

    [Test]
    public void フェーズの経過時間と補足情報を出力する()
    {
        using var writer = new StringWriter();
        var reporter = new BuildProgressReporter(writer);

        reporter.WritePhaseCompleted(
            "Parse",
            TimeSpan.FromMilliseconds(1234),
            "published: 10, scheduled: 2, drafts: 1");

        Assert.That(
            writer.ToString(),
            Is.EqualTo("[Parse] Completed in 1.234s (published: 10, scheduled: 2, drafts: 1)" + Environment.NewLine));
    }

    [Test]
    public void 補足情報がないフェーズとビルド全体を簡潔に出力する()
    {
        using var writer = new StringWriter();
        var reporter = new BuildProgressReporter(writer);

        reporter.WritePhaseCompleted("Assets", TimeSpan.FromMilliseconds(25));
        reporter.WriteBuildCompleted(TimeSpan.FromMilliseconds(2500));

        Assert.That(
            writer.ToString(),
            Is.EqualTo(
                "[Assets] Completed in 0.025s" + Environment.NewLine +
                "[Build] Completed in 2.500s" + Environment.NewLine));
    }
}
