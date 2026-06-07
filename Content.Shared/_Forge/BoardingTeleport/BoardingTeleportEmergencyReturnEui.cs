using Content.Shared.Eui;
using Robust.Shared.Serialization;

namespace Content.Shared._Forge.BoardingTeleport;

[Serializable, NetSerializable]
public sealed class BoardingTeleportEmergencyReturnState(
    string title,
    string message,
    string confirmButton,
    BoardingTeleportReturnConfirmKind kind) : EuiStateBase
{
    public readonly string Title = title;
    public readonly string Message = message;
    public readonly string ConfirmButton = confirmButton;
    public readonly BoardingTeleportReturnConfirmKind Kind = kind;
}

[Serializable, NetSerializable]
public sealed class BoardingTeleportEmergencyReturnResponseMessage(bool accepted, BoardingTeleportReturnConfirmKind kind) : EuiMessageBase
{
    public readonly bool Accepted = accepted;
    public readonly BoardingTeleportReturnConfirmKind Kind = kind;
}
