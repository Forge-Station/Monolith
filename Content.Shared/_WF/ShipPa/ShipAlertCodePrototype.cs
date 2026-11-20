using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.ShipPa;

/// <summary>
/// One situation code a ship can be set to, e.g. "Code Red".
/// </summary>
[Prototype]
public sealed partial class ShipAlertCodePrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;

    /// <summary>Short name, e.g. "Code Red".</summary>
    [DataField(required: true)] public LocId Name;

    /// <summary>Tooltip.</summary>
    [DataField] public LocId? Description;

    /// <summary>PA text read out when the code is set. Receives ($ship).</summary>
    [DataField(required: true)] public LocId Announcement;

    [DataField] public Color Color = Color.White;

    /// <summary>Button order in the console, ascending.</summary>
    [DataField] public int Order;

    [DataField] public bool Selectable = true;

    /// <summary>Played through the speakers when the code is set (chime now, voice line later).</summary>
    [DataField] public SoundSpecifier? Sound;
}
