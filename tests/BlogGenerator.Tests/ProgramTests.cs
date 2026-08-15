using System.Reflection;
using NUnit.Framework;

namespace BlogGenerator.Tests;

[TestFixture]
public class ProgramTests
{
    [Test]
    public void outputがinput配下なら例外を送出する()
    {
        var method = typeof(Program)
            .GetMethod("ThrowIfOutputDirectoryIsInputSubdirectory", BindingFlags.Static | BindingFlags.NonPublic)!;

        var inputDir = Path.Combine(Path.GetTempPath(), "BlogGenerator.Tests", Guid.NewGuid().ToString("N"), "input");
        var outputDir = Path.Combine(inputDir, "output");

        var ex = Assert.Throws<TargetInvocationException>(() => method.Invoke(null, [inputDir, outputDir]));

        Assert.That(ex?.InnerException, Is.TypeOf<ArgumentException>());
    }

    [Test]
    public void outputがinput配下でなければ例外を送出しない()
    {
        var method = typeof(Program)
            .GetMethod("ThrowIfOutputDirectoryIsInputSubdirectory", BindingFlags.Static | BindingFlags.NonPublic)!;

        var rootDir = Path.Combine(Path.GetTempPath(), "BlogGenerator.Tests", Guid.NewGuid().ToString("N"));
        var inputDir = Path.Combine(rootDir, "input");
        var outputDir = Path.Combine(rootDir, "output");

        Assert.DoesNotThrow(() => method.Invoke(null, [inputDir, outputDir]));
    }
}
