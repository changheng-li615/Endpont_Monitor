namespace Xugar.Endpoint.Agent.Services;

public sealed record SynchronizationProgress(
    bool Enabled,
    string EnrollmentStatus,
    string ServerStatus,
    DateTimeOffset? LastHeartbeatUtc,
    DateTimeOffset? LastSuccessfulUploadUtc,
    DateTimeOffset? LastPolicyRefreshUtc,
    int PendingQueueItems,
    long PendingQueueBytes,
    string PolicyStatus);
