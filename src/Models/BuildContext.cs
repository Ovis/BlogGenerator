namespace BlogGenerator.Models;

/// <summary>
/// 1回のビルド処理で固定して使用するコンテキスト情報
/// </summary>
/// <param name="BuildTime">公開状態の判定基準としてビルド全体で共有する時刻</param>
internal sealed record BuildContext(DateTimeOffset BuildTime);
