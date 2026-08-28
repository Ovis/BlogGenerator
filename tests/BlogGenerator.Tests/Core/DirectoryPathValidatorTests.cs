using BlogGenerator.Core;
using NUnit.Framework;

namespace BlogGenerator.Tests.Core;

[TestFixture]
public class DirectoryPathValidatorTests
{
    [Test]
    public void 入力と出力が同一の場合は拒否する()
    {
        var path = Path.Combine(Path.GetTempPath(), "BlogGenerator.Tests", "same");

        Assert.That(
            () => DirectoryPathValidator.ThrowIfInputAndOutputDirectoriesOverlap(path, path),
            Throws.ArgumentException);
    }

    [Test]
    public void 出力が入力の配下の場合は拒否する()
    {
        var inputDir = Path.Combine(Path.GetTempPath(), "BlogGenerator.Tests", "input");
        var outputDir = Path.Combine(inputDir, "output");

        Assert.That(
            () => DirectoryPathValidator.ThrowIfInputAndOutputDirectoriesOverlap(inputDir, outputDir),
            Throws.ArgumentException);
    }

    [Test]
    public void 入力が出力の配下の場合は拒否する()
    {
        var outputDir = Path.Combine(Path.GetTempPath(), "BlogGenerator.Tests", "output");
        var inputDir = Path.Combine(outputDir, "input");

        Assert.That(
            () => DirectoryPathValidator.ThrowIfInputAndOutputDirectoriesOverlap(inputDir, outputDir),
            Throws.ArgumentException);
    }

    [Test]
    public void 独立した入力と出力は許可する()
    {
        var root = Path.Combine(Path.GetTempPath(), "BlogGenerator.Tests");
        var inputDir = Path.Combine(root, "input");
        var outputDir = Path.Combine(root, "output");

        Assert.That(
            () => DirectoryPathValidator.ThrowIfInputAndOutputDirectoriesOverlap(inputDir, outputDir),
            Throws.Nothing);
    }
}
