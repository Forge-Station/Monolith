using Content.Shared._WF.Genitals.Components;
using Content.Shared._WF.Genitals.Prototypes;

namespace Content.Shared._WF.Genitals.Systems;

/// <summary>Arousal value, state mapping and the consent-gated adjustment API. Writes and resets live in SharedArousalSystem.Write.cs.</summary>
public sealed partial class SharedArousalSystem : EntitySystem
{
    [Dependency] private SharedGenitalsSystem _genitals = default!;

    public override void Initialize()
    {
        base.Initialize();
        InitializeWrites();
    }

    /// <summary>None below PartialArousal, Partial below FullArousal, otherwise Full.</summary>
    public static ArousalState ToState(byte value, GenitalSettingsPrototype settings)
    {
        if (value >= settings.FullArousal)
            return ArousalState.Full;

        return value >= settings.PartialArousal ? ArousalState.Partial : ArousalState.None;
    }

    /// <summary>Current arousal state; None without anatomy.</summary>
    public ArousalState GetState(Entity<GenitalsComponent?> ent)
    {
        if (!Resolve(ent, ref ent.Comp, false))
            return ArousalState.None;

        return ToState(ent.Comp.Arousal, _genitals.Settings);
    }

    /// <summary>True when a penis or vagina is present. Breasts and testicles have no arousal effect.</summary>
    public bool CanBeAroused(Entity<GenitalsComponent?> ent)
    {
        return Resolve(ent, ref ent.Comp, false) && (ent.Comp.Penis != null || ent.Comp.Vagina != null);
    }
}
