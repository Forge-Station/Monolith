using Robust.Shared.Serialization;

namespace Content.Shared._WF.RMCCore.Damage;

[Serializable, NetSerializable]
public enum RMCDamageOverlayVisuals : byte
{
    DamageOverlay,
    AdditionalDamageOverlay,
}
