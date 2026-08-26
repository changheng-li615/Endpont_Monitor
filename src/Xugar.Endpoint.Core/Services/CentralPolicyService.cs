using Xugar.Endpoint.Core.Interfaces;
using Xugar.Endpoint.Core.Models;

namespace Xugar.Endpoint.Core.Services;

public sealed class CentralPolicyService(
    ServerSyncSettings settings,
    MonitoringSettings localSettings,
    IMonitoringPolicyCache cache,
    IXugarServerClient serverClient,
    TimeProvider timeProvider)
{
    private readonly object _gate = new();
    private CachedMonitoringPolicy? _current;

    public DateTimeOffset? LastRefreshUtc { get; private set; }

    public async Task LoadCachedAsync(CancellationToken cancellationToken)
    {
        var cached = await cache.ReadAsync(cancellationToken).ConfigureAwait(false);
        lock (_gate)
        {
            _current = cached;
        }
    }

    public async Task<MonitoringPolicy> RefreshAsync(
        DeviceCredential credential,
        CancellationToken cancellationToken)
    {
        var policy = await serverClient.GetPolicyAsync(credential, cancellationToken).ConfigureAwait(false);
        if (!MonitoringScheduleEvaluator.TryValidate(policy, out _))
        {
            throw new XugarServerException(
                ServerFailureKind.MalformedResponse,
                "Xugar server returned an invalid monitoring policy.");
        }

        var cached = new CachedMonitoringPolicy(timeProvider.GetUtcNow(), policy);
        await cache.WriteAsync(cached, cancellationToken).ConfigureAwait(false);
        lock (_gate)
        {
            _current = cached;
            LastRefreshUtc = cached.RetrievedAtUtc;
        }

        return policy;
    }

    public PolicyDecision Evaluate(PolicyActivity activity, DateTimeOffset utcNow)
    {
        if (!settings.Enabled)
        {
            return new PolicyDecision(
                true,
                false,
                PolicyDecisionReason.SynchronizationDisabled,
                activity == PolicyActivity.ScreenshotCapture
                    ? localSettings.ScreenshotIntervalSeconds
                    : localSettings.ProcessIntervalSeconds,
                null);
        }

        CachedMonitoringPolicy? current;
        lock (_gate)
        {
            current = _current;
        }

        if (current is null)
        {
            return Unavailable(PolicyDecisionReason.Unavailable, activity, null);
        }

        var age = utcNow - current.RetrievedAtUtc;
        if (age < TimeSpan.Zero || age > TimeSpan.FromSeconds(settings.PolicyMaxAgeSeconds))
        {
            return Unavailable(PolicyDecisionReason.Expired, activity, current.Policy.Version);
        }

        var policy = current.Policy;
        if (!MonitoringScheduleEvaluator.TryValidate(policy, out _))
        {
            return Unavailable(PolicyDecisionReason.Invalid, activity, policy.Version);
        }
        if (!policy.MonitoringEnabled)
        {
            return Denied(PolicyDecisionReason.MonitoringDisabled, activity, policy.Version);
        }

        var activityEnabled = activity == PolicyActivity.ScreenshotCapture
            ? policy.ScreenshotEnabled
            : policy.ProcessEnabled;
        if (!activityEnabled)
        {
            return Denied(PolicyDecisionReason.ActivityDisabled, activity, policy.Version);
        }
        if (!MonitoringScheduleEvaluator.IsWithinSchedule(policy, utcNow))
        {
            return Denied(PolicyDecisionReason.OutsideSchedule, activity, policy.Version);
        }

        return new PolicyDecision(
            true,
            true,
            PolicyDecisionReason.Allowed,
            activity == PolicyActivity.ScreenshotCapture
                ? policy.ScreenshotIntervalSeconds
                : policy.ProcessIntervalSeconds,
            policy.Version);
    }

    private PolicyDecision Denied(PolicyDecisionReason reason, PolicyActivity activity, int? version) =>
        new(
            false,
            false,
            reason,
            activity == PolicyActivity.ScreenshotCapture
                ? localSettings.ScreenshotIntervalSeconds
                : localSettings.ProcessIntervalSeconds,
            version);

    private PolicyDecision Unavailable(PolicyDecisionReason reason, PolicyActivity activity, int? version) =>
        new(
            activity == PolicyActivity.ProcessMonitoring,
            false,
            reason,
            activity == PolicyActivity.ScreenshotCapture
                ? localSettings.ScreenshotIntervalSeconds
                : localSettings.ProcessIntervalSeconds,
            version);
}
