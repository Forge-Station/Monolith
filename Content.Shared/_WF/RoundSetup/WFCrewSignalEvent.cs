using System.Numerics;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.RoundSetup;

/// <summary>Authoritative leader status without exposing other crews or requiring the leader in PVS.</summary>
[Serializable, NetSerializable]
public enum WFCrewLeaderStatus : byte { Unavailable, Tracking, Elsewhere, Down, Self }

/// <summary>Personal navigation data; a null body clears the previous character's indicator.</summary>
[Serializable, NetSerializable]
public sealed class WFCrewSignalEvent(NetEntity? body, string crew, string leader, WFCrewLeaderStatus status,
    Vector2 origin = default, Vector2? offset = null) : EntityEventArgs
{
    public readonly NetEntity? Body = body;
    public readonly string Crew = crew;
    public readonly string Leader = leader;
    public readonly WFCrewLeaderStatus Status = status;
    public readonly Vector2 Origin = origin;
    public readonly Vector2? Offset = offset;
}
