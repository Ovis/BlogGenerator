using System.Globalization;
using BlogGenerator.Models;
using YamlDotNet.RepresentationModel;
using YamlDotNet.Serialization;

namespace BlogGenerator.Core;

internal sealed class FrontMatterParser(TimeZoneInfo timeZone)
{
    private readonly IDeserializer _deserializer = new DeserializerBuilder().IgnoreUnmatchedProperties().Build();

    public Frontmatter Parse(string yaml, string path)
    {
        YamlStream stream = new();
        try
        {
            stream.Load(new StringReader(yaml));
        }
        catch (Exception ex)
        {
            throw new InvalidDataException($"Invalid Front Matter YAML in '{path}'.", ex);
        }

        var frontMatter = _deserializer.Deserialize<Frontmatter>(yaml) ?? new Frontmatter();
        frontMatter.Published = ParsePublished(stream, path);
        return frontMatter;
    }

    private DateTimeOffset? ParsePublished(YamlStream stream, string path)
    {
        if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode mapping) return null;

        var matches = mapping.Children
            .Where(x => x.Key is YamlScalarNode key && string.Equals(key.Value, "Published", StringComparison.Ordinal))
            .ToArray();
        if (matches.Length == 0) return null;
        if (matches.Length > 1) throw PublishedError(path, null, "Published is defined more than once.");
        if (matches[0].Value is not YamlScalarNode scalar) throw PublishedError(path, null, "Published must be a scalar value.");

        var value = scalar.Value?.Trim();
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
        System.Text.RegularExpressions.Regex.IsMatch(value, @"[+-]\d{2}:?\d{2}$");
}
