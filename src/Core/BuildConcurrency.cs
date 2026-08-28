namespace BlogGenerator.Core;

/// <summary>
/// ビルド中にCPU、ファイルI/O、外部待機を過剰に並列化しないための並列度を定義する
/// </summary>
internal static class BuildConcurrency
{
    /// <summary>
    /// Markdown変換で同時に処理するファイル数
    /// </summary>
    /// <remarks>
    /// Markdown処理にはHTTP待機も含まれるためCPU数より多めに確保するが、全ファイルを一度にTask化することは避ける
    /// </remarks>
    public static int MarkdownDegreeOfParallelism => Math.Clamp(Environment.ProcessorCount * 8, 8, 32);

    /// <summary>
    /// RazorレンダリングとHTML書き出しで同時に処理するページ数
    /// </summary>
    public static int RenderingDegreeOfParallelism => Math.Clamp(Environment.ProcessorCount, 1, 8);

    /// <summary>
    /// 静的ファイルコピーで同時に処理するファイル数
    /// </summary>
    public static int FileCopyDegreeOfParallelism => Math.Clamp(Environment.ProcessorCount * 2, 2, 8);
}
