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

    /// <summary>Unreachable because the device chosen in config is not there: its name (e.g. <c>PRO X2 SUPERSTRIKE</c>).</summary>
    public string? MissingDevice { get; init; }

    /// <summary>
    /// The user request behind this batch's switch, or null. Set only when the switch succeeded, so a failed switch
    /// (unreachable, rejected, host mode) never teaches a per-app profile.
    /// </summary>
    public UserSwitch? User { get; init; }

    /// <summary>
    /// True when this batch's switch came from <see cref="ProfileWorker.RequestAutoProfile"/>, including a failed one
    /// (unreachable, rejected, host mode), so the tray can stay quiet about automatic attempts.
    /// </summary>
    public bool Automatic { get; init; }

    public bool IsReachable => Kind == WorkerStatusKind.Reachable;
}

/// <summary>A profile switch the user asked for (hotkey or menu), as opposed to an automatic per-app one.</summary>
/// <param name="LearnFor">App key to remember the resulting profile for, or null to not learn.</param>
/// <param name="Announce">Show the switch notification.</param>
public sealed record UserSwitch(string? LearnFor, bool Announce);

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
