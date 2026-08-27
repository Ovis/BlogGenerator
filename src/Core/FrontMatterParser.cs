using System.Globalization;
using System.Text.RegularExpressions;
using BlogGenerator.Models;
using YamlDotNet.Serialization;

namespace BlogGenerator.Core;

internal sealed class FrontMatterParser(TimeZoneInfo timeZone)
{
    private readonly IDeserializer _deserializer = new DeserializerBuilder().IgnoreUnmatchedProperties().Build();

    public Frontmatter Parse(string yaml, string path)
    {
        var normalizedYaml = NormalizeYaml(yaml);
        ValidateYaml(normalizedYaml, path);
        var frontMatter = _deserializer.Deserialize<Frontmatter>(normalizedYaml) ?? new Frontmatter();
        frontMatter.Published = ParsePublishedCore(normalizedYaml, path);
        return frontMatter;
    }

    public DateTimeOffset? ParsePublished(string yaml, string path)
    {
        var normalizedYaml = NormalizeYaml(yaml);
        ValidateYaml(normalizedYaml, path);
        return ParsePublishedCore(normalizedYaml, path);
    }

    private void ValidateYaml(string yaml, string path)
    {
        try
        {
            _deserializer.Deserialize<object?>(yaml);
        }
        catch (Exception ex)
        {
            throw new InvalidDataException($"Invalid Front Matter YAML in '{path}'.", ex);
        }
    }

    private DateTimeOffset? ParsePublishedCore(string yaml, string path)
    {
        var matches = Regex.Matches(yaml, @"(?m)^Published\s*:(.*)$");
        if (matches.Count == 0) return null;
        if (matches.Count > 1) throw PublishedError(path, null, "Published is defined more than once.");

        var value = matches[0].Groups[1].Value.Trim();
        if (string.IsNullOrEmpty(value)) throw PublishedError(path, value, "Published must contain a valid date and time.");

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var withOffset) && HasExplicitOffset(value))
            return withOffset;

        if (!DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var local))
            throw PublishedError(path, value, "Published could not be parsed as a date and time.");

        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (timeZone.IsInvalidTime(local)) throw PublishedError(path, value, $"The local time does not exist in time zone '{timeZone.Id}'.");
        if (timeZone.IsAmbiguousTime(local)) throw PublishedError(path, value, $"The local time is ambiguous in time zone '{timeZone.Id}'. Specify an explicit UTC offset.");
        return new DateTimeOffset(local, timeZone.GetUtcOffset(local));
    }

    private InvalidDataException PublishedError(string path, string? value, string reason) =>
        new($"Invalid Published value in '{path}': '{value ?? "<null>"}'. Time zone: '{timeZone.Id}'. Reason: {reason}");

    private static bool HasExplicitOffset(string value) =>
        value.EndsWith("Z", StringComparison.OrdinalIgnoreCase) ||
        Regex.IsMatch(value, @"[+-]\d{2}:?\d{2}$");

    private static string NormalizeYaml(string yaml)
    {
        var lines = yaml.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList();
        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[0])) lines.RemoveAt(0);
        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1])) lines.RemoveAt(lines.Count - 1);
        if (lines.Count > 0 && lines[0].Trim() == "---") lines.RemoveAt(0);
        if (lines.Count > 0 && lines[^1].Trim() == "---") lines.RemoveAt(lines.Count - 1);
        return string.Join('\n', lines);
    }
}
