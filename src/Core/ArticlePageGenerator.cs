using System.Text;
using System.Text.RegularExpressions;
using BlogGenerator.Models;

namespace BlogGenerator.Core;

/// <summary>
/// 記事ページと固定ページの検証およびHTML生成を担当する
/// </summary>
internal sealed class ArticlePageGenerator(
    SiteOption siteOption,
    IFileSystemHelper fileSystemHelper,
    ThemeSettings themeSettings,
    PageRenderingService renderingService)
{
    private static readonly Regex TemplateNamePattern = new("^[A-Za-z0-9_-]+$", RegexOptions.Compiled);

    /// <summary>
    /// 指定された記事を実際には書き出さずにレンダリングし、公開時に生成可能か検証する
    /// </summary>
    public async Task ValidateAsync(Article article, List<Article> publishedArticles, TrustedHtml sideBarHtml)
    {
        var regularArticles = PageGenerationContent.GetRegularArticles(publishedArticles).ToList();
        if (!article.IsFixedPage)
        {
            // 予約記事自身のタグも公開時には集計対象になるため、検証用カタログへ一時的に加える
            regularArticles.Add(article);
        }

        var model = CreateArticlePageModel(article, sideBarHtml, PageGenerationContent.CreateTagCatalog(regularArticles));
        _ = await renderingService.RenderLayoutTemplateAsync(model);
    }

    /// <summary>
    /// 公開対象の記事と固定ページの個別HTMLファイルを生成する
    /// </summary>
    public async Task GenerateAsync(List<Article> articles, string outputDir, TrustedHtml sideBarHtml)
    {
        var regularArticles = PageGenerationContent.GetRegularArticles(articles).ToList();
        var tagCatalog = PageGenerationContent.CreateTagCatalog(regularArticles);

        foreach (var article in articles.Where(PageGenerationContent.IsRenderableContent))
        {
            var outputFilePath = Path.Combine(outputDir, article.RelativeDirectoryPath, article.FileName);
            fileSystemHelper.EnsureDirectoryExists(Path.GetDirectoryName(outputFilePath)!);
            var result = await renderingService.RenderLayoutTemplateAsync(CreateArticlePageModel(article, sideBarHtml, tagCatalog));
            await File.WriteAllTextAsync(outputFilePath, result, Encoding.UTF8);
        }
    }

    /// <summary>
    /// 記事ページ用のRazorモデルを生成する
    /// </summary>
    private PageModel CreateArticlePageModel(Article article, TrustedHtml sideBarHtml, TagCatalog tagCatalog) => new()
    {
        SiteOption = siteOption,
        PageType = Enums.PageType.Article,
        SideBarHtml = sideBarHtml,
        Articles = [article],
        TagCatalog = tagCatalog,
        ContentTemplate = article.IsFixedPage ? ResolveFixedPageTemplate(article.Template) : "Content.cshtml"
    };

    /// <summary>
    /// 固定ページで使用するテンプレート名を検証し、実際のテンプレートファイル名へ解決する
    /// </summary>
    private string ResolveFixedPageTemplate(string configuredTemplate)
    {
        var templateName = string.IsNullOrWhiteSpace(configuredTemplate) ? "Page" : configuredTemplate.Trim();
        if (!TemplateNamePattern.IsMatch(templateName))
            throw new InvalidOperationException($"Invalid fixed page template name '{configuredTemplate}'. Only letters, digits, '_' and '-' are allowed.");

        // テーマパスがない旧来の呼び出しでは、存在確認を行わず従来どおりファイル名だけを返す
        if (string.IsNullOrEmpty(themeSettings.ThemePath)) return $"{templateName}.cshtml";

        var matches = Directory.GetFiles(themeSettings.ThemePath, "*.cshtml", SearchOption.TopDirectoryOnly)
            .Where(path => string.Equals(Path.GetFileNameWithoutExtension(path), templateName, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (matches.Length == 0)
            throw new InvalidOperationException($"Fixed page template '{templateName}.cshtml' was not found in theme '{themeSettings.ThemePath}'.");
        if (matches.Length > 1)
            throw new InvalidOperationException($"Fixed page template '{templateName}' is ambiguous. Matching files: {string.Join(", ", matches.Select(Path.GetFileName))}.");

        return Path.GetFileName(matches[0]);
    }
}
