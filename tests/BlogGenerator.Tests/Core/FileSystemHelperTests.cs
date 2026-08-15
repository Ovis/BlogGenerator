using System.Reflection;
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

    [Test]
    public async Task 大文字拡張子のMarkdownは静的ファイルとしてコピーしない()
    {
        var inputDir = Path.Combine(_testRootPath, "input");
        var outputDir = Path.Combine(_testRootPath, "output");
        Directory.CreateDirectory(inputDir);
        Directory.CreateDirectory(outputDir);

        await File.WriteAllTextAsync(Path.Combine(inputDir, "HELLO.MD"), "# article");
        await File.WriteAllTextAsync(Path.Combine(inputDir, "note.txt"), "memo");

        var fileSystemHelper = new FileSystemHelper();

        fileSystemHelper.CopyContentFiles(inputDir, outputDir);

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(Path.Combine(outputDir, "HELLO.MD")), Is.False);
            Assert.That(File.Exists(Path.Combine(outputDir, "note.txt")), Is.True);
        });
    }

    [Test]
    public void 共有違反とロック違反はリトライ対象として判定する()
    {
        var method = typeof(FileSystemHelper)
            .GetMethod("IsRetryableCopyIOException", BindingFlags.Static | BindingFlags.NonPublic)!;

        var sharingViolation = new TestIOException(unchecked((int)0x80070020));
        var lockViolation = new TestIOException(unchecked((int)0x80070021));

        Assert.Multiple(() =>
        {
            Assert.That((bool)method.Invoke(null, [sharingViolation])!, Is.True);
            Assert.That((bool)method.Invoke(null, [lockViolation])!, Is.True);
        });
    }

    [Test]
    public void それ以外のIOExceptionはリトライ対象にしない()
    {
        var method = typeof(FileSystemHelper)
            .GetMethod("IsRetryableCopyIOException", BindingFlags.Static | BindingFlags.NonPublic)!;

        var accessDenied = new TestIOException(unchecked((int)0x80070005));

        Assert.That((bool)method.Invoke(null, [accessDenied])!, Is.False);
    }

    private sealed class TestIOException : IOException
    {
        public TestIOException(int hResult)
        {
            HResult = hResult;
        }
    }
}
