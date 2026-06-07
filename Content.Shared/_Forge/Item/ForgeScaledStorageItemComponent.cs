using System.Numerics;
using Robust.Shared.GameStates;

namespace Content.Shared._Forge.Item;

/// <summary>
/// Optional per-item storage UI scaling and wide-slot offset. Used by empire energy spear only.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class ForgeScaledStorageItemComponent : Component
{
    [DataField]
    public Vector2 Scale = Vector2.One;

    /// <summary>
    /// Offset override when the item occupies a wider-than-tall grid area (e.g. 6x2 vs 2x6).
    /// </summary>
    [DataField]
    public Vector2i OffsetWide;
}
