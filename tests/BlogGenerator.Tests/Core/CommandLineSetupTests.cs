using System.CommandLine;
using BlogGenerator.Core;
using NUnit.Framework;

namespace BlogGenerator.Tests.Core;

public class CommandLineSetupTests
{
    [Test]
    public void 旧来エイリアスを含むオプションを解析できる()
    {
        var inputPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "input");
        var outputPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "output");
        var themePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "theme");
        var oEmbedPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "oembed.json");
        var configPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "config.json");

        var setup = new CommandLineSetup();
        var rootCommand = setup.CreateRootCommand();

        var parseResult = rootCommand.Parse(
            ["/input", inputPath, "--output", outputPath, "/theme", themePath, "/oembed", oEmbedPath, "-c", configPath],
            new ParserConfiguration());

        Assert.Multiple(() =>
        {
            Assert.That(parseResult.Errors, Is.Empty);
            Assert.That(parseResult.GetRequiredValue(setup.InputOption).FullName, Is.EqualTo(Path.GetFullPath(inputPath)));
            Assert.That(parseResult.GetRequiredValue(setup.OutputOption).FullName, Is.EqualTo(Path.GetFullPath(outputPath)));
            Assert.That(parseResult.GetRequiredValue(setup.ThemeOption).FullName, Is.EqualTo(Path.GetFullPath(themePath)));
            Assert.That(parseResult.GetValue(setup.OEmbedOption), Is.EqualTo(oEmbedPath));
            Assert.That(parseResult.GetValue(setup.ConfigOption)?.FullName, Is.EqualTo(Path.GetFullPath(configPath)));
        });
    }
}
