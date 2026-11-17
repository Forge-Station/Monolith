using Robust.Shared.Serialization;

namespace Content.Shared._Forge.Bss;

[Serializable, NetSerializable]
public sealed class BssGateJumpMessage : BoundUserInterfaceMessage
{
    public string DestinationSector = string.Empty;
}
