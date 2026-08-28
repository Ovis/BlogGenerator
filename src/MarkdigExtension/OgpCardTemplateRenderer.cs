using RazorLight;

namespace BlogGenerator.MarkdigExtension;

/// <summary>
/// テーマまたは組み込みテンプレートでOGPカードを描画するサービス
/// </summary>
public sealed class OgpCardTemplateRenderer : IOgpCardTemplateRenderer
{
    private const string ThemeTemplateKey = ".embeds/ogp.cshtml";
    private const string EmbeddedTemplateKey = "Templates.Embeds.Ogp.cshtml";

    private readonly RazorLightEngine? _themeEngine;
    private readonly string? _themeTemplatePath;
    private readonly RazorLightEngine _embeddedTemplateEngine;

    /// <summary>
    /// 組み込みテンプレートだけを使用するレンダラーを生成する
    /// </summary>
    public OgpCardTemplateRenderer()
    {
        _embeddedTemplateEngine = CreateEmbeddedTemplateEngine();
    }

    /// <summary>
    /// テーマ側の上書きテンプレートを利用できるレンダラーを生成する
    /// </summary>
    /// <param name="themeEngine">テーマディレクトリをルートとするRazorLightEngine</param>
    /// <param name="themePath">テーマディレクトリ</param>
    public OgpCardTemplateRenderer(RazorLightEngine themeEngine, string themePath)
        : this()
    {
        _themeEngine = themeEngine;
        _themeTemplatePath = Path.Combine(themePath, ".embeds", "ogp.cshtml");
    }

    /// <inheritdoc />
    public Task<string> RenderAsync(OgpCardModel model)
    {
        // テーマが明示的にOGPカードを定義した場合だけテーマ版を使用する
        if (_themeEngine is not null &&
            !string.IsNullOrEmpty(_themeTemplatePath) &&
            File.Exists(_themeTemplatePath))
        {
            return _themeEngine.CompileRenderAsync(ThemeTemplateKey, model);
        }

        return _embeddedTemplateEngine.CompileRenderAsync(EmbeddedTemplateKey, model);
    }

    /// <summary>
    /// テーマにOGPテンプレートがない場合のフォールバック用RazorLightEngineを生成する
    /// </summary>
    private static RazorLightEngine CreateEmbeddedTemplateEngine() =>
        new RazorLightEngineBuilder()
            .UseEmbeddedResourcesProject(typeof(OgpCardTemplateRenderer).Assembly, "BlogGenerator")
            .UseMemoryCachingProvider()
            .Build();
}
