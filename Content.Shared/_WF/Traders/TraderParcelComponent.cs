using Robust.Shared.Audio;

namespace Content.Shared._WF.Traders;

/// <summary>
/// A sealed carton a trader packs an order into. Not storage: only the trader fills it, and
/// opening it tips everything out at once.
/// </summary>
[RegisterComponent]
public sealed partial class TraderParcelComponent : Component
{
    /// <summary>
    /// Container holding the packed goods.
    /// </summary>
    [DataField]
    public string ContainerId = "parcel-contents";

    /// <summary>
    /// How many goods fit before the trader starts a second parcel.
    /// </summary>
    [DataField]
    public int Capacity = 8;

    /// <summary>
    /// Played when the parcel is torn open.
    /// </summary>
    [DataField]
    public SoundSpecifier OpenSound =
        new SoundPathSpecifier("/Audio/_Goobstation/Items/handling/cardboardbox_drop.ogg");
}
