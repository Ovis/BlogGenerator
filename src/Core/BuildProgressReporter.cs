using System.Globalization;

namespace BlogGenerator.Core;

/// <summary>
/// 通常ビルドの進捗、処理件数、経過時間を一貫した形式で出力する
/// </summary>
/// <remarks>
/// scheduledコマンドは標準出力をJSON専用としているため、このクラスは通常ビルドでのみ使用する
/// </remarks>
internal sealed class BuildProgressReporter(TextWriter output)
{
    /// <summary>
    /// ビルド開始時刻とタイムゾーンを出力する
    /// </summary>
    /// <param name="buildTime">公開状態判定に使用するビルド開始時刻</param>
    /// <param name="timeZoneId">ビルドで使用するタイムゾーンID</param>
    public void WriteBuildStarted(DateTimeOffset buildTime, string timeZoneId)
    {
        output.WriteLine($"[Build] Started: {buildTime:yyyy-MM-dd HH:mm:ss zzz}");
        output.WriteLine($"[Build] Time zone: {timeZoneId}");
    }

    /// <summary>
    /// ビルドフェーズの完了と経過時間を出力する
    /// </summary>
    /// <param name="phaseName">フェーズ名</param>
    /// <param name="elapsed">フェーズ単体の経過時間</param>
    /// <param name="details">件数などの補足情報。不要な場合は省略する</param>
    public void WritePhaseCompleted(string phaseName, TimeSpan elapsed, string? details = null)
    {
        var suffix = string.IsNullOrWhiteSpace(details) ? string.Empty : $" ({details})";
        output.WriteLine($"[{phaseName}] Completed in {FormatDuration(elapsed)}{suffix}");
    }

    /// <summary>
    /// ビルド全体の完了と総経過時間を出力する
    /// </summary>
    /// <param name="elapsed">ビルド全体の経過時間</param>
    public void WriteBuildCompleted(TimeSpan elapsed) =>
        output.WriteLine($"[Build] Completed in {FormatDuration(elapsed)}");

    /// <summary>
    /// ログ上で比較しやすいよう、秒単位かつ小数3桁で経過時間を整形する
    /// </summary>
    private static string FormatDuration(TimeSpan elapsed) =>
        elapsed.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture) + "s";
}
