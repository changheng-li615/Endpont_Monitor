namespace Xugar.Endpoint.Core.Services;

public static class RetryBackoffCalculator
{
    public static TimeSpan Calculate(
        int attemptCount,
        TimeSpan minimum,
        TimeSpan maximum,
        double jitterSample)
    {
        if (attemptCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(attemptCount));
        }
        if (minimum <= TimeSpan.Zero || maximum < minimum)
        {
            throw new ArgumentOutOfRangeException(nameof(minimum));
        }
        if (jitterSample is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(jitterSample));
        }

        var exponent = Math.Min(attemptCount - 1, 30);
        var uncappedMilliseconds = minimum.TotalMilliseconds * Math.Pow(2, exponent);
        var cappedMilliseconds = Math.Min(uncappedMilliseconds, maximum.TotalMilliseconds);
        var jitterFactor = 0.8 + (0.4 * jitterSample);
        return TimeSpan.FromMilliseconds(Math.Min(cappedMilliseconds * jitterFactor, maximum.TotalMilliseconds));
    }
}
