using BlogGenerator.Core.Interfaces;
using BlogGenerator.Models;
using RazorLight;

namespace BlogGenerator.Core;

/// <summary>
/// 各ページ種別のジェネレーターをまとめ、従来のIPageGeneratorインターフェースを提供するFacade
/// </summary>
/// <remarks>
/// 呼び出し側から見た公開APIは維持しつつ、実際の生成責務は記事、トップ、タグ、アーカイブ、サイドバーの各クラスへ委譲する
/// </remarks>
public class PageGenerator : IPageGenerator
{
    private readonly SideBarGenerator _sideBarGenerator;
    private readonly ArticlePageGenerator _articlePageGenerator;
    private readonly IndexPageGenerator _indexPageGenerator;
    private readonly TagPageGenerator _tagPageGenerator;
    private readonly ArchivePageGenerator _archivePageGenerator;

    /// <summary>
    /// テーマパスを明示せずにページジェネレーターを生成する
    /// </summary>
    /// <remarks>
    /// 主にテーマファイルの存在検証を必要としない呼び出しとの互換性を維持するためのコンストラクター
    /// </remarks>
    public PageGenerator(RazorLightEngine razorLightEngine, SiteOption siteOption, IFileSystemHelper fileSystemHelper)
        : this(razorLightEngine, siteOption, fileSystemHelper, new ThemeSettings(string.Empty))
    {
    }

    /// <summary>
    /// サイト設定とテーマ設定を使用してページジェネレーターを生成する
    /// </summary>
    public PageGenerator(
        RazorLightEngine razorLightEngine,
        SiteOption siteOption,
        IFileSystemHelper fileSystemHelper,
        ThemeSettings themeSettings)
    {
        var renderingService = new PageRenderingService(razorLightEngine, siteOption, fileSystemHelper);

        // PageGenerator自身は処理を持たず、ページ種別ごとのジェネレーターを組み立てて委譲する
        _sideBarGenerator = new SideBarGenerator(razorLightEngine, siteOption);
        _articlePageGenerator = new ArticlePageGenerator(siteOption, fileSystemHelper, themeSettings, renderingService);
        _indexPageGenerator = new IndexPageGenerator(siteOption, fileSystemHelper, renderingService);
        _tagPageGenerator = new TagPageGenerator(siteOption, fileSystemHelper, renderingService);
        _archivePageGenerator = new ArchivePageGenerator(siteOption, fileSystemHelper, renderingService);
    }

    /// <inheritdoc />
    public Task<TrustedHtml> GenerateSideBarHtmlAsync(List<Article> articles) =>
        _sideBarGenerator.GenerateAsync(articles);

    /// <inheritdoc />
    public Task ValidateArticlePageAsync(Article article, List<Article> publishedArticles, TrustedHtml sideBarHtml) =>
        _articlePageGenerator.ValidateAsync(article, publishedArticles, sideBarHtml);

    /// <inheritdoc />
    public Task GenerateArticlePagesAsync(List<Article> articles, string outputDir, TrustedHtml sideBarHtml) =>
        _articlePageGenerator.GenerateAsync(articles, outputDir, sideBarHtml);

    /// <inheritdoc />
    public Task GenerateIndexPagesAsync(List<Article> articles, string outputDir, TrustedHtml sideBarHtml) =>
        _indexPageGenerator.GenerateAsync(articles, outputDir, sideBarHtml);

    /// <inheritdoc />
    public Task GenerateTagPagesAsync(List<Article> articles, string outputDir, TrustedHtml sideBarHtml) =>
        _tagPageGenerator.GenerateAsync(articles, outputDir, sideBarHtml);

    /// <inheritdoc />
    public Task GenerateArchivePagesAsync(List<Article> articles, string outputDir, TrustedHtml sideBarHtml) =>
        _archivePageGenerator.GenerateAsync(articles, outputDir, sideBarHtml);
}
