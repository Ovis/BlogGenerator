namespace BlogGenerator.Models
{
    public class PageModelBase
    {
        public SiteOption SiteOption { get; set; } = new();

        public TagCatalog TagCatalog { get; set; } = TagCatalog.Build([]);

        public string GeneratePath(string path)
        {
            if (path.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                return path;
            }

            var normalizedBasePath = NormalizeUrlPath(SiteOption.BaseAbsolutePath);
            var normalizedPath = NormalizeUrlPath(path);

            if (normalizedPath == "/" || IsSameOrDescendantUrlPath(normalizedPath, normalizedBasePath))
            {
                return normalizedPath == "/" ? normalizedBasePath : normalizedPath;
            }

            return CombineUrlPath(normalizedBasePath, normalizedPath);
        }

        public string GenerateSiteUrl(string path)
        {
            if (path.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                return path;
            }

            return new Uri(new Uri(SiteOption.SiteUrl), GeneratePath(path)).ToString();
        }

        public string GenerateTagPath(string tag)
        {
            if (!TagCatalog.TryGet(tag, out var entry))
            {
                throw new InvalidOperationException($"Tag is not present in the catalog: '{tag}'");
            }

            return GeneratePath($"/tags/{entry.Slug}");
        }

        public string GenerateTagDisplayName(string tag)
        {
            return TagCatalog.TryGet(tag, out var entry)
                ? entry.DisplayName
                : TagCatalog.NormalizeDisplayValue(tag);
        }

        public IReadOnlyCollection<TagCatalogEntry> GetArticleTags(Article article)
        {
            var entries = new List<TagCatalogEntry>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var tag in article.Tags)
            {
                if (TagCatalog.TryGet(tag, out var entry) && seen.Add(entry.IdentityKey))
                {
                    entries.Add(entry);
                }
            }

            return entries;
        }

        public static string CombineUrlPath(params string[] segments)
        {
            var nonEmptySegments = segments
                .Where(segment => !string.IsNullOrWhiteSpace(segment))
                .ToArray();

            if (nonEmptySegments.Length == 0)
            {
                return "/";
            }

            var hasLeadingSlash = nonEmptySegments[0].StartsWith("/", StringComparison.Ordinal);
            var path = string.Join(
                "/",
                nonEmptySegments
                    .Select(segment => segment.Replace("\\", "/").Trim('/'))
                    .Where(segment => segment.Length > 0));

            return hasLeadingSlash ? "/" + path : path;
        }

        private static string NormalizeUrlPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return "/";
            }

            var normalizedPath = path.Replace("\\", "/").Trim();
            if (normalizedPath == "/")
            {
                return "/";
            }

            return normalizedPath.StartsWith("/", StringComparison.Ordinal)
                ? "/" + normalizedPath.Trim('/')
                : normalizedPath.Trim('/');
        }

        private static bool IsSameOrDescendantUrlPath(string path, string basePath)
        {
            if (path == basePath)
            {
                return true;
            }

            return basePath == "/"
                ? path.StartsWith("/", StringComparison.Ordinal)
                : path.StartsWith(basePath + "/", StringComparison.Ordinal);
        }
    }
}
