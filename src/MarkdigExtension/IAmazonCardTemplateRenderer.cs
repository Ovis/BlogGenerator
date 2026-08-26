namespace BlogGenerator.MarkdigExtension;

/// <summary>
/// Amazonカードテンプレートを描画するサービス
/// </summary>
public interface IAmazonCardTemplateRenderer
{
    Task<string> RenderAsync(AmazonCardModel model);
}
