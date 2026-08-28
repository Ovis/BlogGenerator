namespace BlogGenerator.Core;

/// <summary>
/// サイト生成を開始する前に出力ディレクトリをクリーンな状態へ準備する
/// </summary>
internal static class OutputDirectoryPreparer
{
    /// <summary>
    /// 既存の出力ディレクトリを削除し、空のディレクトリとして再作成する
    /// </summary>
    /// <param name="outputDir">BlogGeneratorが生成成果物を配置する出力ディレクトリ</param>
    /// <remarks>
    /// 前回ビルドで生成された記事、タグ、ページネーションなどが今回の生成対象から外れた場合でも、
    /// 古い成果物が残らないよう成果物生成フェーズの直前に実行する
    /// </remarks>
    public static void Recreate(string outputDir)
    {
        if (Directory.Exists(outputDir))
        {
            Directory.Delete(outputDir, recursive: true);
        }

        Directory.CreateDirectory(outputDir);
    }
}
