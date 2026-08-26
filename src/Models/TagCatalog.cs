using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace BlogGenerator.Models;

public sealed class TagCatalog
{
    private const int MaxSlugBytes = 120;
    private const int HashHexLength = 16;
    private static readonly HashSet<string> WindowsReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    private readonly Dictionary<string, TagCatalogEntry> _entriesByIdentity;

    private TagCatalog(Dictionary<string, TagCatalogEntry> entriesByIdentity)
    {
        _entriesByIdentity = entriesByIdentity;
        Entries = entriesByIdentity.Values.ToArray();
    }

    public IReadOnlyCollection<TagCatalogEntry> Entries { get; }

    public static TagCatalog Build(IEnumerable<Article> articles, Action<string>? warningWriter = null)
    {
        var groups = new Dictionary<string, TagAccumulator>(StringComparer.OrdinalIgnoreCase);
        var order = 0;

        foreach (var article in articles)
        {
            foreach (var rawTag in article.Tags)
            {
                var normalized = NormalizeDisplayValue(rawTag);
                if (normalized.Length == 0)
                {
                    warningWriter?.Invoke($"Empty tag was ignored: {article.RootRelativePath} ({article.Title})");
                    continue;
                }

                var identity = CreateIdentityKey(normalized);
                if (!groups.TryGetValue(identity, out var accumulator))
                {
                    accumulator = new TagAccumulator(identity, order++);
                    groups.Add(identity, accumulator);
                }

                accumulator.Add(normalized, article);
            }
        }

        var entries = new Dictionary<string, TagCatalogEntry>(StringComparer.OrdinalIgnoreCase);
        var slugOwners = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var accumulator in groups.Values.OrderBy(x => x.FirstOrder))
        {
            var displayName = accumulator.DisplayCounts
                .OrderByDescending(x => x.Value.Count)
                .ThenBy(x => x.Value.FirstOrder)
                .First().Key;
            var slug = CreateSlug(accumulator.IdentityKey);

            if (slugOwners.TryGetValue(slug, out var existingIdentity) &&
                !string.Equals(existingIdentity, accumulator.IdentityKey, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Tag slug collision detected: '{existingIdentity}' and '{accumulator.IdentityKey}' -> '{slug}'");
            }

            slugOwners[slug] = accumulator.IdentityKey;
            entries[accumulator.IdentityKey] = new TagCatalogEntry(
                accumulator.IdentityKey,
                displayName,
                slug,
                accumulator.Articles.ToArray());
        }

        return new TagCatalog(entries);
    }

    public bool TryGet(string tag, out TagCatalogEntry entry)
    {
        var normalized = NormalizeDisplayValue(tag);
        if (normalized.Length == 0)
        {
            entry = null!;
            return false;
        }

        return _entriesByIdentity.TryGetValue(CreateIdentityKey(normalized), out entry!);
    }

    public static string NormalizeDisplayValue(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return string.Empty;
        }

        var normalized = tag.Normalize(NormalizationForm.FormC).Trim();
        return Regex.Replace(normalized, @"\s+", " ");
    }

    public static string CreateIdentityKey(string normalizedTag) =>
        normalizedTag.Normalize(NormalizationForm.FormC).ToUpperInvariant();

    public static string CreateSlug(string identityKey)
    {
        var source = identityKey.ToLowerInvariant();
        var runes = source.EnumerateRunes().ToArray();
        var units = new List<string>(runes.Length);

        for (var index = 0; index < runes.Length; index++)
        {
            var rune = runes[index];
            var isFirst = index == 0;
            var isLast = index == runes.Length - 1;
            units.Add(EncodeRune(rune, isFirst, isLast));
        }

        var slug = string.Concat(units);
        if (IsWindowsReservedName(slug))
        {
            slug = "_~" + slug;
        }

        if (Encoding.UTF8.GetByteCount(slug) <= MaxSlugBytes)
        {
            return slug;
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identityKey)))[..HashHexLength].ToLowerInvariant();
        var suffix = "~" + hash;
        var maxPrefixBytes = MaxSlugBytes - Encoding.UTF8.GetByteCount(suffix);
        var prefixBuilder = new StringBuilder();
        var usedBytes = 0;

        foreach (var unit in units)
        {
            var unitBytes = Encoding.UTF8.GetByteCount(unit);
            if (usedBytes + unitBytes > maxPrefixBytes)
            {
                break;
            }

            prefixBuilder.Append(unit);
            usedBytes += unitBytes;
        }

        var prefix = prefixBuilder.ToString();
        if (IsWindowsReservedName(prefix))
        {
            prefix = "_~" + prefix;
            while (Encoding.UTF8.GetByteCount(prefix + suffix) > MaxSlugBytes && prefix.Length > 2)
            {
                prefix = prefix[..^1];
            }
        }

        return prefix + suffix;
    }

    private static string EncodeRune(Rune rune, bool isFirst, bool isLast)
    {
        if (rune.Value == ' ')
        {
            return "-";
        }

        if (rune.Value == '-')
        {
            return "~2D";
        }

        if (rune.Value == '~')
        {
            return "~7E";
        }

        if (rune.Value == '_')
        {
            return "_";
        }

        if (rune.Value == '.')
        {
            return isFirst || isLast ? "~2E" : ".";
        }

        var category = Rune.GetUnicodeCategory(rune);
        if (category is UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or
            UnicodeCategory.TitlecaseLetter or UnicodeCategory.ModifierLetter or UnicodeCategory.OtherLetter or
            UnicodeCategory.DecimalDigitNumber or UnicodeCategory.LetterNumber or UnicodeCategory.OtherNumber)
        {
            return rune.ToString();
        }

        Span<byte> buffer = stackalloc byte[4];
        var byteCount = rune.EncodeToUtf8(buffer);
        var builder = new StringBuilder(byteCount * 3);
        for (var index = 0; index < byteCount; index++)
        {
            builder.Append('~').Append(buffer[index].ToString("X2", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    private static bool IsWindowsReservedName(string slug)
    {
        var candidate = slug.Split('.', 2)[0];
        return WindowsReservedNames.Contains(candidate);
    }

    private sealed class TagAccumulator(string identityKey, int firstOrder)
    {
        public string IdentityKey { get; } = identityKey;
        public int FirstOrder { get; } = firstOrder;
        public Dictionary<string, (int Count, int FirstOrder)> DisplayCounts { get; } = new(StringComparer.Ordinal);
        public List<Article> Articles { get; } = [];
        private int _displayOrder;

        public void Add(string displayValue, Article article)
        {
            if (DisplayCounts.TryGetValue(displayValue, out var value))
            {
                DisplayCounts[displayValue] = (value.Count + 1, value.FirstOrder);
            }
            else
            {
                DisplayCounts[displayValue] = (1, _displayOrder++);
            }

            if (!Articles.Contains(article))
            {
                Articles.Add(article);
            }
        }
    }
}

public sealed record TagCatalogEntry(
    string IdentityKey,
    string DisplayName,
    string Slug,
    IReadOnlyCollection<Article> Articles)
{
    public int Count => Articles.Count;
}
