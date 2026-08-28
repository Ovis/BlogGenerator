using BlogGenerator.Core;
using NUnit.Framework;

namespace BlogGenerator.Tests.Core;

[TestFixture]
[NonParallelizable]
public class OutputDirectoryPreparerTests
{
    private string _testRootPath = null!;

    [SetUp]
    public void SetUp()
    {
        _testRootPath = Path.Combine(Path.GetTempPath(), "BlogGenerator.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testRootPath);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_testRootPath))
        {
            Directory.Delete(_testRootPath, recursive: true);
        }
    }

    [Test]
    public async Task 既存の出力ディレクトリを空の状態で再作成する()
    {
        var outputDir = Path.Combine(_testRootPath, "output");
        var staleDirectory = Path.Combine(outputDir, "tags", "removed-tag");
        Directory.CreateDirectory(staleDirectory);
        await File.WriteAllTextAsync(Path.Combine(outputDir, "removed-article.html"), "stale");
        await File.WriteAllTextAsync(Path.Combine(staleDirectory, "index.html"), "stale tag");

        OutputDirectoryPreparer.Recreate(outputDir);

        Assert.Multiple(() =>
        {
            Assert.That(Directory.Exists(outputDir), Is.True);
            Assert.That(Directory.EnumerateFileSystemEntries(outputDir), Is.Empty);
        });
    }

    [Test]
    public void 出力ディレクトリが存在しなくても空の状態で作成する()
    {
        var outputDir = Path.Combine(_testRootPath, "output");

        OutputDirectoryPreparer.Recreate(outputDir);

        Assert.Multiple(() =>
        {
            Assert.That(Directory.Exists(outputDir), Is.True);
            Assert.That(Directory.EnumerateFileSystemEntries(outputDir), Is.Empty);
        });
    }
}
