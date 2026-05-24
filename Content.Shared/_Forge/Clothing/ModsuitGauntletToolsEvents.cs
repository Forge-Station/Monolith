using Content.Shared.Actions;
using Robust.Shared.Serialization;

namespace Content.Shared._Forge.Clothing;

[Serializable, NetSerializable]
public enum ModsuitGauntletToolSlot : byte
{
    Urk,
    Omnitool,
    Welder,
    NaniteApplicator,
}

[Serializable, NetSerializable]
public sealed class ModsuitGauntletToggleToolMessage(NetEntity gauntlets, ModsuitGauntletToolSlot slot) : EntityEventArgs
{
    public readonly NetEntity Gauntlets = gauntlets;
    public readonly ModsuitGauntletToolSlot Slot = slot;
}

public sealed partial class OpenModsuitGauntletToolsMenuActionEvent : InstantActionEvent;
