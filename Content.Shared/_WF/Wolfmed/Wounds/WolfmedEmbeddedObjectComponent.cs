using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Wolfmed.Wounds;

/// <summary>Objects lodged in a wound: blocks its treatment and names the item each removal spawns.</summary>
// Bleeding and pain upkeep are the wound prototype's own behaviors (a clottingMultiplier: 0 bleed never stops on its
// own), so a new embedded wound only has to set them there.
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WolfmedEmbeddedObjectComponent : Component
{
    [DataField, AutoNetworkedField]
    public int Count = 1;

    /// <summary>Spawned at the patient when one object is pulled out.</summary>
    [DataField, AutoNetworkedField]
    public EntProtoId Item = "WFWolfmedShrapnelFragment";

    /// <summary>Cap for later hits merging into this wound.</summary>
    [DataField]
    public int MaxCount = 6;

    /// <summary>Whether sutures, gauze and surgery are refused while anything is embedded.</summary>
    [DataField]
    public bool BlocksTreatment = true;

    /// <summary>Do-after for a surgical tool.</summary>
    [DataField]
    public TimeSpan CleanDelay = TimeSpan.FromSeconds(4);

    /// <summary>Do-after for an improvised sharp item.</summary>
    [DataField]
    public TimeSpan SharpDelay = TimeSpan.FromSeconds(7);

    /// <summary>Digging it out of your own limb takes longer.</summary>
    [DataField]
    public float SelfMultiplier = 1.75f;

    /// <summary>Dealt to the part when an improvised sharp item is used: the extra cut it leaves.</summary>
    [DataField]
    public DamageSpecifier SharpDamage = new()
    {
        DamageDict = { ["Slash"] = FixedPoint2.New(4) },
    };

    /// <summary>Pain added on top of the cut, for the digging itself.</summary>
    [DataField]
    public FixedPoint2 SharpPain = FixedPoint2.New(10);

    /// <summary>Played at the patient when the tool goes in.</summary>
    [DataField]
    public SoundSpecifier? BeginSound = new SoundCollectionSpecifier("WFWolfmedToolProbe");

    /// <summary>Played when the object comes out.</summary>
    [DataField]
    public SoundSpecifier? EndSound = new SoundCollectionSpecifier("WFWolfmedToolExtract");
}
