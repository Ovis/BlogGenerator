using BlogGenerator.Models;
using Markdig;
using Markdig.Extensions.Yaml;
using Markdig.Syntax;

namespace BlogGenerator.Core;

internal sealed class ScheduledPublicationChecker(TimeZoneInfo timeZone)
{
    private readonly FrontMatterParser _frontMatterParser = new(timeZone);
    private readonly MarkdownPipeline _frontMatterPipeline = new MarkdownPipelineBuilder().UseYamlFrontMatter().Build();

    public ScheduledPublicationCheckResult Check(string inputDirectory, DateTimeOffset after, DateTimeOffset until)
    {
        if (after >= until) throw new ArgumentException("--after must be earlier than --until.");

        var items = new List<ScheduledPublicationItem>();
        var errors = new List<ScheduledPublicationError>();
        var files = Directory.GetFiles(inputDirectory, "*", SearchOption.AllDirectories)
            .Where(path => string.Equals(Path.GetExtension(path), ".md", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => Path.GetRelativePath(inputDirectory, path).Replace('\\', '/'), StringComparer.Ordinal);

        foreach (var file in files)
        {
            var relativePath = Path.GetRelativePath(inputDirectory, file).Replace('\\', '/');
            try
            {
                var markdown = File.ReadAllText(file);
                var document = Markdown.Parse(markdown, _frontMatterPipeline);
                var yamlBlock = document.Descendants<YamlFrontMatterBlock>().FirstOrDefault();
                if (yamlBlock is null) continue;

                var frontMatter = _frontMatterParser.Parse(yamlBlock.Lines.ToString(), relativePath);
                if (frontMatter.Published is { } published && after < published && published <= until)
                    items.Add(new ScheduledPublicationItem(relativePath, published));
            }
            catch (Exception ex)
            {
                errors.Add(new ScheduledPublicationError(relativePath, ex));
            }
        }

        if (errors.Count > 0)
            throw new ScheduledPublicationCheckException(errors.OrderBy(x => x.Path, StringComparer.Ordinal).ToArray());

        var orderedItems = items
            .OrderBy(x => x.Published)
            .ThenBy(x => x.Path, StringComparer.Ordinal)
            .ToArray();
        return new ScheduledPublicationCheckResult(after, until, timeZone, orderedItems);
    }
}
