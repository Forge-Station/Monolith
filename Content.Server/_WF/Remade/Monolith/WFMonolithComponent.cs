using Content.Shared._WF.Remade;
using Content.Shared.Materials;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Remade.Monolith;

/// <summary>A Monolith: gives without limit, and climbs towards the Crescendo the more it gives.</summary>
[RegisterComponent]
public sealed partial class WFMonolithComponent : Component
{
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float Resonance;

    /// <summary>How hard the Power gift is being drawn on, from 0 to 1.</summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float Draw;

    /// <summary>The dials of the other Gifts, from 0 to 1.</summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float MatterDraw;

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float FleshDraw;

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float ReachDraw;

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float WardDraw;

    /// <summary>Materials Matter makes.</summary>
    [DataField]
    public List<ProtoId<MaterialPrototype>> MatterMaterials = new() { "Steel", "Glass", "Plastic" };

    [ViewVariables]
    public TimeSpan NextMatter;

    /// <summary>Shards chipped off this Scion that still exist.</summary>
    [ViewVariables]
    public HashSet<EntityUid> Shards = new();

    [ViewVariables]
    public WFMonolithStage Stage = WFMonolithStage.Quiet;

    /// <summary>Resonance a fed corpse takes off.</summary>
    [DataField]
    public float FeedRelief = 5f;

    /// <summary>Radius in which corpses speed the climb.</summary>
    [DataField]
    public float CorpseRadius = 10f;

    [DataField]
    public SoundSpecifier CrescendoSound = new SoundPathSpecifier("/Audio/_WF/Remade/monolith/alarm.ogg");

    /// <summary>What everyone in the round hears as the stone climbs into Hymn, Growth, Harvest and Rapture.</summary>
    [DataField]
    public List<SoundSpecifier> StageSounds = new()
    {
        new SoundPathSpecifier("/Audio/_WF/Remade/monolith/stage_hymn.ogg"),
        new SoundPathSpecifier("/Audio/_WF/Remade/monolith/stage_growth.ogg"),
        new SoundPathSpecifier("/Audio/_WF/Remade/monolith/stage_harvest.ogg"),
        new SoundPathSpecifier("/Audio/_WF/Remade/monolith/stage_rapture.ogg"),
    };

    /// <summary>Played to everyone in the round under the alarm when the stone roots.</summary>
    [DataField]
    public SoundSpecifier CrescendoSong = new SoundPathSpecifier("/Audio/_WF/Remade/monolith/crescendo_song.ogg");

    [ViewVariables]
    public int Corpses;

    /// <summary>A splinter: its dread, score and stings stay on its own grid, and it is no sector threat.</summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public bool Local;

    /// <summary>The highest stage it can climb to.</summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public WFMonolithStage MaxStage = WFMonolithStage.Crescendo;

    /// <summary>Resonance a second it gains with nothing drawn on it.</summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float SelfClimb;

    [ViewVariables]
    public TimeSpan NextTick;

    [ViewVariables]
    public TimeSpan NextCorpseCount;

    /// <summary>Corruption grown from this Monolith.</summary>
    [ViewVariables]
    public HashSet<EntityUid> Corruption = new();
}
