using Content.Shared.Eui;
using Robust.Shared.Serialization;

namespace Content.Shared._Forge.BoardingTeleport;

[Serializable, NetSerializable]
public sealed class BoardingTeleportEmergencyReturnState(string title, string message) : EuiStateBase
{
    public readonly string Title = title;
    public readonly string Message = message;
}

[Serializable, NetSerializable]
public sealed class BoardingTeleportEmergencyReturnResponseMessage(bool accepted) : EuiMessageBase
{
    public readonly bool Accepted = accepted;
}
