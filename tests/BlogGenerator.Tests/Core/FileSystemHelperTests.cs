using BlogGenerator.Core;
using NUnit.Framework;

namespace BlogGenerator.Tests.Core;

[TestFixture]
[NonParallelizable]
public class FileSystemHelperTests
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
    public async Task 入力ルート配下の非Markdown資産を一括コピーできる()
    {
        var inputDir = Path.Combine(_testRootPath, "input");
        var outputDir = Path.Combine(_testRootPath, "output");
        Directory.CreateDirectory(Path.Combine(inputDir, "posts"));
        Directory.CreateDirectory(Path.Combine(inputDir, "assets", "images"));
        Directory.CreateDirectory(Path.Combine(inputDir, "downloads"));
        Directory.CreateDirectory(outputDir);

        await File.WriteAllTextAsync(Path.Combine(inputDir, "posts", "hello.md"), "# article");
        await File.WriteAllTextAsync(Path.Combine(inputDir, "assets", "site.css"), "body {}");
        await File.WriteAllBytesAsync(Path.Combine(inputDir, "assets", "images", "logo.png"), [1, 2, 3]);
        await File.WriteAllTextAsync(Path.Combine(inputDir, "downloads", "manual.txt"), "manual");
        await File.WriteAllTextAsync(Path.Combine(inputDir, ".draft"), "hidden");

        var fileSystemHelper = new FileSystemHelper();

        fileSystemHelper.CopyContentFiles(inputDir, outputDir);

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(Path.Combine(outputDir, "assets", "site.css")), Is.True);
            Assert.That(File.Exists(Path.Combine(outputDir, "assets", "images", "logo.png")), Is.True);
            Assert.That(File.Exists(Path.Combine(outputDir, "downloads", "manual.txt")), Is.True);
            Assert.That(File.Exists(Path.Combine(outputDir, "posts", "hello.md")), Is.False);
            Assert.That(File.Exists(Path.Combine(outputDir, ".draft")), Is.False);
        });
    }
}
