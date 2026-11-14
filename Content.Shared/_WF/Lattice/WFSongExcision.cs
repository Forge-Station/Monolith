using Content.Shared._Shitmed.Medical.Surgery.Conditions;
using Content.Shared._Shitmed.Medical.Surgery.Steps;
using Content.Shared._Shitmed.Medical.Surgery.Tools;
using Robust.Shared.GameStates;

namespace Content.Shared._WF.Lattice;

/// <summary>On a surgery: only for a Choir member, and not out in the band where the dead rise.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WFSongExcisionConditionComponent : Component
{
    /// <summary>Least Lattice band where the song is too loud to cut out.</summary>
    [DataField]
    public int NoSurgeryBand = 4;
}

/// <summary>On a surgery step: cuts the song out. Done once the patient is no longer Choir.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WFSongExcisionEffectComponent : Component
{
    /// <summary>Chance the patient dies on the table and turns Remade.</summary>
    [DataField]
    public float FailChance = 0.3f;

    /// <summary>Brain health lost either way.</summary>
    [DataField]
    public float BrainDamage = 30f;
}

/// <summary>What the song is cut out with: exotic matter, used up one shard at a time.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WFSongExcisionToolComponent : Component, ISurgeryToolComponent
{
    public string ToolName => Loc.GetString("wf-song-excision-tool-name");

    public bool? Used { get; set; } = true;

    [DataField]
    public float Speed { get; set; } = 1f;
}

/// <summary>The excision's checks, which the surgery window also runs on the client.</summary>
public sealed class WFSongExcisionConditionSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFSongExcisionConditionComponent, SurgeryValidEvent>(OnValid);
        SubscribeLocalEvent<WFSongExcisionEffectComponent, SurgeryStepCompleteCheckEvent>(OnCheck);
    }

    private void OnValid(Entity<WFSongExcisionConditionComponent> ent, ref SurgeryValidEvent args)
    {
        if (!HasComp<WFChoirMemberComponent>(args.Body) || Band(args.Body) >= ent.Comp.NoSurgeryBand)
            args.Cancelled = true;
    }

    private void OnCheck(Entity<WFSongExcisionEffectComponent> ent, ref SurgeryStepCompleteCheckEvent args)
    {
        if (HasComp<WFChoirMemberComponent>(args.Body))
            args.Cancelled = true;
    }

    /// <summary>The Lattice band the patient is in: their ship's, or their own off one.</summary>
    private int Band(EntityUid body)
    {
        if (Transform(body).GridUid is { } grid && TryComp<WFDriftComponent>(grid, out var ship))
            return ship.Band;

        return CompOrNull<WFDriftComponent>(body)?.Band ?? 0;
    }
}
