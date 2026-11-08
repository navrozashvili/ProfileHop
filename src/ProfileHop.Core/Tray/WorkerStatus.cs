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

    public bool IsReachable => Kind == WorkerStatusKind.Reachable;
}
