namespace BlogGenerator.Models
{
    public class PageModelBase
    {
        public SiteOption SiteOption { get; set; } = new();

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
            return GeneratePath($"/tags/{EncodeTagSegment(tag)}");
        }

        public static string EncodeTagSegment(string tag)
        {
            return Uri.EscapeDataString(tag);
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
