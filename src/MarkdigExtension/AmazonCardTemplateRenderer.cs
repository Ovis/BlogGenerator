using RazorLight;

namespace BlogGenerator.MarkdigExtension;

/// <summary>
/// テーマまたは組み込みテンプレートでAmazonカードを描画するサービス
/// </summary>
public sealed class AmazonCardTemplateRenderer : IAmazonCardTemplateRenderer
{
    private const string ThemeTemplateKey = ".embeds/amazon.cshtml";
    private const string EmbeddedTemplateKey = "Templates.Embeds.Amazon.cshtml";

    private readonly RazorLightEngine _themeEngine;
    private readonly string _themeTemplatePath;
    private readonly RazorLightEngine _embeddedTemplateEngine;

    public AmazonCardTemplateRenderer(RazorLightEngine themeEngine, string themePath)
    {
        _themeEngine = themeEngine;
        _themeTemplatePath = Path.Combine(themePath, ".embeds", "amazon.cshtml");
        _embeddedTemplateEngine = new RazorLightEngineBuilder()
            // テンプレートはBlogGenerator直下のmanifest resourceとして埋め込むため、型の深いnamespaceではなくrootを明示する
            .UseEmbeddedResourcesProject(typeof(AmazonCardTemplateRenderer).Assembly, "BlogGenerator")
            .UseMemoryCachingProvider()
            .Build();
    }

    public Task<string> RenderAsync(AmazonCardModel model)
    {
        // テーマが明示的に上書きした場合だけ外部テンプレートを使い、存在しないテーマでも生成を継続できるようにする
        return File.Exists(_themeTemplatePath)
            ? _themeEngine.CompileRenderAsync(ThemeTemplateKey, model)
            : _embeddedTemplateEngine.CompileRenderAsync(EmbeddedTemplateKey, model);
    }
}
