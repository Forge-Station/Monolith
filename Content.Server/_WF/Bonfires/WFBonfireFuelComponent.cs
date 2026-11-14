using Content.Shared.Tag;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Bonfires;

/// <summary>A fire that burns its fuel down, takes wood and paper to burn on, goes out when it's spent and can be lit again.</summary>
[RegisterComponent]
public sealed partial class WFBonfireFuelComponent : Component
{
    /// <summary>Seconds of burning left.</summary>
    [DataField]
    public float Fuel = 1800f;

    /// <summary>Most seconds of fuel it holds.</summary>
    [DataField]
    public float MaxFuel = 3600f;

    /// <summary>Burns for ever, with no need of fuel or lighting.</summary>
    [DataField]
    public bool Infinite;

    [DataField]
    public bool Lit = true;

    /// <summary>Seconds of burning for each unit of wood in what is put on: a log (200) burns ten minutes.</summary>
    [DataField]
    public float WoodSeconds = 3f;

    /// <summary>Seconds of burning an item with a tag gives.</summary>
    [DataField]
    public Dictionary<ProtoId<TagPrototype>, float> TagSeconds = new()
    {
        ["Paper"] = 45f,
        ["Book"] = 90f,
    };

    [DataField]
    public SoundSpecifier? FuelSound = new SoundPathSpecifier("/Audio/Effects/burning.ogg");

    [DataField]
    public SoundSpecifier? LightSound = new SoundPathSpecifier("/Audio/Items/Lighters/lighter1.ogg");

    /// <summary>Share of <see cref="MaxFuel"/> under which it burns low.</summary>
    [DataField]
    public float LowShare = 0.2f;
}
