namespace ProfileHop.Core.Tray;

public enum WorkerStatusKind
{
    Starting,
    Reachable,
    Unreachable,
}

/// <summary>What the tray shows: last known mouse state, or why the mouse is not reachable.</summary>
public sealed record WorkerStatus(WorkerStatusKind Kind, ProfileState? State, string? Error)
{
    public static WorkerStatus Starting { get; } = new(WorkerStatusKind.Starting, null, null);

    public static WorkerStatus Reachable(ProfileState state) => new(WorkerStatusKind.Reachable, state, null);

    public static WorkerStatus Unreachable(string error) => new(WorkerStatusKind.Unreachable, null, error);

    /// <summary>One-off message for a balloon (e.g. the mouse rejected a switch); the status itself stays reachable.</summary>
    public string? Notice { get; init; }

    public bool IsReachable => Kind == WorkerStatusKind.Reachable;
}

/// <summary>
/// One answer to <see cref="ProfileWorker.RequestDevices"/>. Requests run in order and coalesce, so this answers every
/// request with an id up to <see cref="RequestId"/>.
/// </summary>
/// <param name="Devices">The device list (on failure: the previous list, or empty).</param>
/// <param name="RequestId">Id of the latest request this batch answered.</param>
/// <param name="Error">Why listing failed, or null.</param>
public sealed record DeviceListResult(IReadOnlyList<KnownDevice> Devices, long RequestId, string? Error)
{
    /// <summary>True when this answers request <paramref name="requestId"/> (0 = no request pending: false).</summary>
    public bool Answers(long requestId) => requestId != 0 && RequestId >= requestId;
}
