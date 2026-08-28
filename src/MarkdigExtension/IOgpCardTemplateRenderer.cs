namespace BlogGenerator.MarkdigExtension;

/// <summary>
/// OGPカードをテーマテンプレートで描画する機能を提供する
/// </summary>
public interface IOgpCardTemplateRenderer
{
    /// <summary>
    /// OGPカード表示モデルをHTMLへ変換する
    /// </summary>
    /// <param name="model">安全なURLへ正規化済みの表示モデル</param>
    Task<string> RenderAsync(OgpCardModel model);
}
