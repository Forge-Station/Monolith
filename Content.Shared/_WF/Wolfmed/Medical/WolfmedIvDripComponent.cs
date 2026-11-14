using Content.Shared.DoAfter;
using Content.Shared.Tag;
using Content.Shared.Whitelist;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Wolfmed.Medical;

/// <summary>
/// A movable IV stand, after tgstation's (Nova's) drip. It holds one container, and with its needle in a patient it
/// either feeds the container in or draws the patient's blood out, at its flow rate. A Bloodpack stack restores the
/// patient's own blood; a beaker or jug is injected as a hypospray would.
/// </summary>
[RegisterComponent]
public sealed partial class WolfmedIvDripComponent : Component
{
    public const string ContainerId = "wolfmed_iv_container";

    /// <summary>What hangs on the stand: blood packs, beakers, jugs and bottles.</summary>
    [DataField]
    public EntityWhitelist? Whitelist;

    /// <summary>The tag that makes a hung item a stack of blood packs rather than a solution container.</summary>
    [DataField]
    public ProtoId<TagPrototype> PackTag = "Bloodpack";

    /// <summary>How far the patient may be from the stand before the needle comes out. One tile, diagonals included.</summary>
    [DataField]
    public float Range = 1.5f;

    /// <summary>Seconds between transfers. tg's drip moves on the two-second machine tick; this is finer.</summary>
    [DataField]
    public float Interval = 1f;

    /// <summary>Played with the "pings" when a container fills in take mode.</summary>
    [DataField]
    public SoundSpecifier? PingSound = new SoundPathSpecifier("/Audio/Machines/microwave_done_beep.ogg");

    /// <summary>Played with the "beeps loudly" while a patient giving blood is low.</summary>
    [DataField]
    public SoundSpecifier? BeepSound = new SoundPathSpecifier("/Audio/Machines/twobeep.ogg");

    // Runtime state, written on the server only.

    /// <summary>Feeding the container in, or drawing blood out into it.</summary>
    [ViewVariables]
    public WolfmedIvMode Mode = WolfmedIvMode.Inject;

    /// <summary>Units a second. Negative until the server sets the default flow.</summary>
    [ViewVariables]
    public float Rate = -1f;

    /// <summary>Whoever has the needle in them.</summary>
    [ViewVariables]
    public EntityUid? Patient;

    /// <summary>
    /// Playtest 5: units left in the pack the drip has opened. A pack leaves the stack the moment it is opened, so
    /// taking the stack down and hanging it again gives nothing back, and the opened pack runs on with nothing hung.
    /// </summary>
    [ViewVariables]
    public float PackOpened;

    [ViewVariables]
    public TimeSpan NextUpdate;

    /// <summary>Times the drip pinged on a full container. Counts for the tests.</summary>
    [ViewVariables]
    public int Pings;

    /// <summary>Times the drip beeped over a low patient. Counts for the tests.</summary>
    [ViewVariables]
    public int Beeps;
}

/// <summary>Which way an IV drip moves fluid.</summary>
[Serializable, NetSerializable]
public enum WolfmedIvMode : byte
{
    /// <summary>The container into the patient.</summary>
    Inject,

    /// <summary>The patient's blood into the container.</summary>
    Take,
}

/// <summary>Appearance keys the drip's visualizer reads.</summary>
[Serializable, NetSerializable]
public enum WolfmedIvDripVisuals : byte
{
    /// <summary><see cref="WolfmedIvMode"/>.</summary>
    Mode,

    /// <summary>Bool: a needle is in somebody.</summary>
    Attached,

    /// <summary>Bool: attached with a flow over zero.</summary>
    Flowing,

    /// <summary>Bool: something hangs on the stand.</summary>
    Container,

    /// <summary>Int: tg's fill threshold (0, 10, 25, 50, 75, 80 or 90); the overlay is "reagent" plus it.</summary>
    Fill,

    /// <summary>Color: the fill overlay's tint.</summary>
    FillColor,
}

/// <summary>The drip's sprite layers.</summary>
[Serializable, NetSerializable]
public enum WolfmedIvDripLayers : byte
{
    Base,
    Container,
    Fill,
}

/// <summary>Putting the drip's needle into a patient.</summary>
[Serializable, NetSerializable]
public sealed partial class WolfmedIvAttachDoAfterEvent : SimpleDoAfterEvent;
