using BlogGenerator.Models;

namespace BlogGenerator.Core;

internal sealed class ScheduledPublicationChecker(TimeZoneInfo timeZone)
{
    private readonly FrontMatterParser _frontMatterParser = new(timeZone);

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
                if (!TryExtractFrontMatter(markdown, out var yaml)) continue;

                var published = _frontMatterParser.ParsePublished(yaml, relativePath);
                if (published is { } value && after < value && value <= until)
                    items.Add(new ScheduledPublicationItem(relativePath, value));
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

    private static bool TryExtractFrontMatter(string markdown, out string yaml)
    {
        yaml = string.Empty;
        if (string.IsNullOrEmpty(markdown)) return false;

        var normalized = markdown.Replace("\r\n", "\n").Replace('\r', '\n');
        if (!normalized.StartsWith("---\n", StringComparison.Ordinal)) return false;

        var end = normalized.IndexOf("\n---", 4, StringComparison.Ordinal);
        if (end < 0) return false;

        var afterDelimiter = end + 4;
        if (afterDelimiter < normalized.Length && normalized[afterDelimiter] != '\n') return false;

        yaml = normalized[4..end];
        return true;
    }
}
