namespace BlogGenerator.Models
{
    public class PageModelBase
    {
        public SiteOption SiteOption { get; set; } = new();

        public string GeneratePath(string path)
        {
            return path.StartsWith("http") ? path : Path.Combine(SiteOption.BaseAbsolutePath, path.TrimStart('/'));
        }

        public string GenerateTagPath(string tag)
        {
            return GeneratePath($"/tags/{EncodeTagSegment(tag)}");
        }

        public static string EncodeTagSegment(string tag)
        {
            return Uri.EscapeDataString(tag);
        }
    }
}
