using Robust.Shared.Serialization;

namespace Content.Shared._WF.RoundSetup;

/// <summary>The current body's server-time crash deadline; null clears the preparation warning.</summary>
[Serializable, NetSerializable]
public sealed class WFCrashCountdownEvent(TimeSpan? descendAt, NetEntity? body) : EntityEventArgs
{
    /// <summary>The full preparation window before atmospheric entry.</summary>
    public const int DurationSeconds = 180;

    public readonly TimeSpan? DescendAt = descendAt;
    public readonly NetEntity? Body = body;
}
