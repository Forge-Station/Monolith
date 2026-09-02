using Robust.Shared.Serialization;

namespace Content.Shared._Forge.Botany;

/// <summary>
/// Tray grow-lamp setting compared against <c>SeedData.IdealLight</c>.
/// Ambient matches typical station lighting; Day/Shade are forced high/low lux.
/// </summary>
[Serializable, NetSerializable]
public enum HydroponicsLightMode : byte
{
    Ambient = 0,
    Day = 1,
    Shade = 2,
}
