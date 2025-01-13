namespace BlogGenerator.Models;

public class SiteOption
{
    /// <summary>
    /// サイト名
    /// </summary>
    public string SiteName { get; set; } = string.Empty;

    /// <summary>
    /// サイトの説明
    /// </summary>
    public string SiteDescription { get; set; } = string.Empty;

    /// <summary>
    /// サイトのURL
    /// </summary>
    public string SiteUrl { get; set; } = string.Empty;

    /// <summary>
    /// サイトの絶対パス
    /// </summary>
    public string BaseAbsolutePath
    {
        get
        {
            return new Uri(SiteUrl).AbsolutePath;
        }
    }

    /// <summary>
    /// サイト管理者
    /// </summary>
    public string SiteAuthor { get; set; } = string.Empty;

    /// <summary>
    /// サイト管理者の説明
    /// </summary>
    public string SiteAuthorDescription { get; set; } = string.Empty;


}
