using System.CommandLine;
using BlogGenerator.Core;
using NUnit.Framework;

namespace BlogGenerator.Tests.Core;

public class CommandLineSetupTests
{
    [Test]
    public void WebP並列数は省略すると自動設定になる()
    {
        var setup = new CommandLineSetup();
        var parsed = setup.CreateRootCommand().Parse(["--input", "input", "--output", "output", "--theme", "theme", "--webp"]);
        Assert.Multiple(() =>
        {
            Assert.That(parsed.Errors, Is.Empty);
            Assert.That(parsed.GetValue(setup.WebpParallelismOption), Is.Null);
        });
    }

    [TestCase(1)]
    [TestCase(2)]
    [TestCase(8)]
    public void WebP並列数を明示指定できる(int parallelism)
    {
        var setup = new CommandLineSetup();
        var parsed = setup.CreateRootCommand().Parse(["--input", "input", "--output", "output", "--theme", "theme", "--webp", "--webp-parallelism", parallelism.ToString()]);
        Assert.Multiple(() =>
        {
            Assert.That(parsed.Errors, Is.Empty);
            Assert.That(parsed.GetValue(setup.WebpParallelismOption), Is.EqualTo(parallelism));
        });
    }

    [TestCase("0")]
    [TestCase("-1")]
    [TestCase("abc")]
    [TestCase(null)]
    public void WebP並列数の不正値と値なしを拒否する(string? value)
    {
        var setup = new CommandLineSetup();
        var args = new List<string> { "--input", "input", "--output", "output", "--theme", "theme", "--webp", "--webp-parallelism" };
        if (value is not null) args.Add(value);
        Assert.That(setup.CreateRootCommand().Parse(args.ToArray()).Errors, Is.Not.Empty);
    }

    [Test]
    public void WebP無効時に並列数だけを指定するとエラーになる()
    {
        var setup = new CommandLineSetup();
        var parsed = setup.CreateRootCommand().Parse(["--input", "input", "--output", "output", "--theme", "theme", "--webp-parallelism", "2"]);
        Assert.That(parsed.Errors.Select(x => x.Message), Has.Some.Contains("--webpも指定"));
    }

    [Test]
    public void 旧来エイリアスを含むオプションを解析できる()
    {
        var inputPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "input");
        var outputPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "output");
        var themePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "theme");
        var oEmbedPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "oembed.json");
        var amazonCachePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "amazon.json");
        var configPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "config.json");

        var setup = new CommandLineSetup();
        var rootCommand = setup.CreateRootCommand();

        var parseResult = rootCommand.Parse(
            ["/input", inputPath, "--output", outputPath, "/theme", themePath, "/oembed", oEmbedPath, "--amazon-cache", amazonCachePath, "-c", configPath],
            new ParserConfiguration());

        Assert.Multiple(() =>
        {
            Assert.That(parseResult.Errors, Is.Empty);
            Assert.That(parseResult.GetRequiredValue(setup.InputOption).FullName, Is.EqualTo(Path.GetFullPath(inputPath)));
            Assert.That(parseResult.GetRequiredValue(setup.OutputOption).FullName, Is.EqualTo(Path.GetFullPath(outputPath)));
            Assert.That(parseResult.GetRequiredValue(setup.ThemeOption).FullName, Is.EqualTo(Path.GetFullPath(themePath)));
            Assert.That(parseResult.GetValue(setup.OEmbedOption), Is.EqualTo(oEmbedPath));
            Assert.That(parseResult.GetValue(setup.AmazonCacheOption), Is.EqualTo(amazonCachePath));
            Assert.That(parseResult.GetValue(setup.ConfigOption)?.FullName, Is.EqualTo(Path.GetFullPath(configPath)));
        });
    }
}
