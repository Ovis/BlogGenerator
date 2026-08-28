using BlogGenerator.Core;
using NUnit.Framework;

namespace BlogGenerator.Tests;

[TestFixture]
public class DirectoryPathValidatorTests
{
    [Test]
    public void outputがinput配下なら例外を送出する()
    {
        var inputDir = Path.Combine(Path.GetTempPath(), "BlogGenerator.Tests", Guid.NewGuid().ToString("N"), "input");
        var outputDir = Path.Combine(inputDir, "output");

        Assert.Throws<ArgumentException>(() =>
            DirectoryPathValidator.ThrowIfInputAndOutputDirectoriesOverlap(inputDir, outputDir));
    }

    [Test]
    public void outputがinput配下でなければ例外を送出しない()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), "BlogGenerator.Tests", Guid.NewGuid().ToString("N"));
        var inputDir = Path.Combine(rootDir, "input");
        var outputDir = Path.Combine(rootDir, "output");

        Assert.DoesNotThrow(() =>
            DirectoryPathValidator.ThrowIfInputAndOutputDirectoriesOverlap(inputDir, outputDir));
    }
}
