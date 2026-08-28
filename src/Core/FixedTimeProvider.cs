namespace BlogGenerator.Core;

/// <summary>
/// 1回のビルド中で同じ現在時刻を返すTimeProvider
/// </summary>
/// <remarks>
/// 公開判定やフィード更新時刻など、ビルド中に現在時刻へ依存する処理が同一時刻を参照できるようにする
/// </remarks>
internal sealed class FixedTimeProvider(DateTimeOffset buildTime, TimeZoneInfo localTimeZone) : TimeProvider
{
    /// <inheritdoc />
    public override TimeZoneInfo LocalTimeZone => localTimeZone;

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => buildTime.ToUniversalTime();
}
