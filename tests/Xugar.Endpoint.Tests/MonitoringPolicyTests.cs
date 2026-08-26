using Xugar.Endpoint.Core.Interfaces;
using Xugar.Endpoint.Core.Models;
using Xugar.Endpoint.Core.Services;

namespace Xugar.Endpoint.Tests;

public sealed class MonitoringPolicyTests
{
    [Fact]
    public void EvaluatesInsideOutsideAndOvernightWindowsInExplicitTimezone()
    {
        var daytime = Policy([new MonitoringScheduleWindow(1, "09:00", "17:00")]);
        Assert.True(MonitoringScheduleEvaluator.IsWithinSchedule(
            daytime,
            new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero)));
        Assert.False(MonitoringScheduleEvaluator.IsWithinSchedule(
            daytime,
            new DateTimeOffset(2026, 8, 24, 18, 0, 0, TimeSpan.Zero)));

        var overnight = Policy([new MonitoringScheduleWindow(1, "22:00", "06:00")]);
        Assert.True(MonitoringScheduleEvaluator.IsWithinSchedule(
            overnight,
            new DateTimeOffset(2026, 8, 24, 23, 0, 0, TimeSpan.Zero)));
        Assert.True(MonitoringScheduleEvaluator.IsWithinSchedule(
            overnight,
            new DateTimeOffset(2026, 8, 25, 5, 59, 0, TimeSpan.Zero)));
        Assert.False(MonitoringScheduleEvaluator.IsWithinSchedule(
            overnight,
            new DateTimeOffset(2026, 8, 25, 6, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void ConvertsUtcIntoConfiguredTimezoneDeterministically()
    {
        var policy = Policy(
            [new MonitoringScheduleWindow(1, "09:00", "10:00")],
            "Australia/Sydney");

        Assert.True(MonitoringScheduleEvaluator.IsWithinSchedule(
            policy,
            new DateTimeOffset(2026, 8, 23, 23, 30, 0, TimeSpan.Zero)));
        Assert.False(MonitoringScheduleEvaluator.IsWithinSchedule(
            policy,
            new DateTimeOffset(2026, 8, 24, 0, 30, 0, TimeSpan.Zero)));
    }

    [Fact]
    public async Task MissingAndExpiredPolicyDenyScreenshotsButKeepSafeLocalProcessCapture()
    {
        var now = new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero);
        var time = new ManualTimeProvider(now);
        var cache = new MemoryPolicyCache();
        var service = CreateService(time, cache, new StubServerClient());
        await service.LoadCachedAsync(CancellationToken.None);

        var missingScreenshot = service.Evaluate(PolicyActivity.ScreenshotCapture, now);
        var missingProcess = service.Evaluate(PolicyActivity.ProcessMonitoring, now);
        Assert.False(missingScreenshot.AllowLocalCapture);
        Assert.True(missingProcess.AllowLocalCapture);
        Assert.False(missingProcess.AllowSynchronization);

        cache.Value = new CachedMonitoringPolicy(now.AddMinutes(-20), Policy([
            new MonitoringScheduleWindow(1, "00:00", "23:59")
        ]));
        await service.LoadCachedAsync(CancellationToken.None);
        Assert.Equal(
            PolicyDecisionReason.Expired,
            service.Evaluate(PolicyActivity.ScreenshotCapture, now).Reason);
    }

    [Fact]
    public async Task ValidPolicyIsFetchedCachedAndAllowsOnlyEnabledScheduledActivity()
    {
        var now = new DateTimeOffset(2026, 8, 24, 10, 0, 0, TimeSpan.Zero);
        var time = new ManualTimeProvider(now);
        var cache = new MemoryPolicyCache();
        var server = new StubServerClient
        {
            Policy = Policy([new MonitoringScheduleWindow(1, "09:00", "17:00")])
        };
        var service = CreateService(time, cache, server);

        await service.RefreshAsync(
            new DeviceCredential(Guid.NewGuid(), "secret"),
            CancellationToken.None);

        Assert.NotNull(cache.Value);
        Assert.True(service.Evaluate(PolicyActivity.ScreenshotCapture, now).AllowSynchronization);
        Assert.True(service.Evaluate(PolicyActivity.ProcessMonitoring, now).AllowSynchronization);
    }

    [Fact]
    public async Task MalformedFetchedPolicyIsRejectedAndNeverCached()
    {
        var time = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var cache = new MemoryPolicyCache();
        var server = new StubServerClient
        {
            Policy = Policy([new MonitoringScheduleWindow(1, "09:00", "09:00")])
        };

        await Assert.ThrowsAsync<XugarServerException>(() =>
            CreateService(time, cache, server).RefreshAsync(
                new DeviceCredential(Guid.NewGuid(), "secret"),
                CancellationToken.None));
        Assert.Null(cache.Value);
    }

    [Fact]
    public async Task FilePolicyCacheSurvivesReloadAndQuarantinesCorruptJson()
    {
        using var directory = new TemporaryDirectory();
        var expected = new CachedMonitoringPolicy(DateTimeOffset.UtcNow, Policy([
            new MonitoringScheduleWindow(1, "09:00", "17:00")
        ]));
        using (var cache = new FileMonitoringPolicyCache(directory.Path))
        {
            await cache.WriteAsync(expected, CancellationToken.None);
        }
        using (var reloaded = new FileMonitoringPolicyCache(directory.Path))
        {
            var actual = await reloaded.ReadAsync(CancellationToken.None);
            Assert.NotNull(actual);
            Assert.Equal(expected.RetrievedAtUtc, actual.RetrievedAtUtc);
            Assert.Equal(expected.Policy.Version, actual.Policy.Version);
            Assert.Equal(expected.Policy.ScheduleWindows, actual.Policy.ScheduleWindows);
        }

        var path = StoragePaths.GetPolicyCachePath(directory.Path);
        await File.WriteAllTextAsync(path, "not-json");
        using var corrupt = new FileMonitoringPolicyCache(directory.Path);
        Assert.Null(await corrupt.ReadAsync(CancellationToken.None));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(path)!, "policy-cache.corrupt.*.json"));
    }

    private static CentralPolicyService CreateService(
        ManualTimeProvider time,
        IMonitoringPolicyCache cache,
        StubServerClient server) =>
        new(
            new ServerSyncSettings { Enabled = true, PolicyMaxAgeSeconds = 900 },
            new MonitoringSettings(),
            cache,
            server,
            time);

    private static MonitoringPolicy Policy(
        IReadOnlyList<MonitoringScheduleWindow> windows,
        string timezone = "UTC") =>
        new(1, true, true, 300, true, 60, timezone, windows);

    private sealed class MemoryPolicyCache : IMonitoringPolicyCache
    {
        public CachedMonitoringPolicy? Value { get; set; }

        public Task<CachedMonitoringPolicy?> ReadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Value);

        public Task WriteAsync(CachedMonitoringPolicy policy, CancellationToken cancellationToken)
        {
            Value = policy;
            return Task.CompletedTask;
        }
    }
}
