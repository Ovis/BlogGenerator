using System.Diagnostics;

namespace BlogGenerator.MarkdigExtension;

/// <summary>
/// 外部HTTP取得の件数と累積時間をスレッドセーフに集計する
/// </summary>
internal sealed class ExternalRequestMetrics
{
    private long _requestCount;
    private long _elapsedTicks;

    /// <summary>
    /// HTTP取得処理を実行し、件数と所要時間を記録する
    /// </summary>
    /// <typeparam name="T">取得処理の戻り値型</typeparam>
    /// <param name="operation">計測対象の非同期処理</param>
    /// <returns>取得処理の戻り値</returns>
    public async Task<T> MeasureAsync<T>(Func<Task<T>> operation)
    {
        Interlocked.Increment(ref _requestCount);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            return await operation();
        }
        finally
        {
            Interlocked.Add(ref _elapsedTicks, stopwatch.Elapsed.Ticks);
        }
    }

    /// <summary>
    /// 現在までに集計した値を取得する
    /// </summary>
    public ExternalRequestMetricsSnapshot GetSnapshot() => new(
        Interlocked.Read(ref _requestCount),
        TimeSpan.FromTicks(Interlocked.Read(ref _elapsedTicks)));
}

/// <summary>
/// 外部HTTP取得の計測結果
/// </summary>
internal readonly record struct ExternalRequestMetricsSnapshot(long RequestCount, TimeSpan Elapsed);
