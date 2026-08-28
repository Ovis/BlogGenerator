namespace BlogGenerator.MarkdigExtension;

/// <summary>
/// 1つのURLに対するoEmbed解決結果
/// </summary>
internal sealed class OEmbedResolutionResult
{
    /// <summary>
    /// provider由来HTML、標準リンクなどそのまま保存できるHTML
    /// </summary>
    public string HtmlContent { get; init; } = string.Empty;

    /// <summary>
    /// テーマテンプレートで描画するOGP fallbackカード
    /// </summary>
    public OgpCardModel? OgpCard { get; init; }

    public required bool IsSuccess { get; init; }

    public string ErrorSummary { get; init; } = string.Empty;
}
