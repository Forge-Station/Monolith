// WOLFGATE(Wolfmed): ported from Onyx for Wolfmed.
using Content.Shared.Body.Part;
using Content.Shared.CCVar;
using Robust.Shared.Configuration;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Shared._Onyx.Wounds;

public sealed partial class WoundScarSystem : EntitySystem
{
    private static readonly ProtoId<WoundPrototype> ScarWound = "MedicalScarWound";

    [Dependency] private INetManager _net = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private WoundSystem _wounds = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IRobustRandom _random = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WoundComponent, WoundStateChangedEvent>(OnWoundStateChanged);
        SubscribeLocalEvent<WoundScarComponent, WoundTreatmentAttemptEvent>(OnTreatmentAttempt);
    }

    private void OnWoundStateChanged(Entity<WoundComponent> wound, ref WoundStateChangedEvent args)
    {
        if (args.State is not WoundState.Closed and not WoundState.Healed)
        {
            wound.Comp.ScarCreatedForCurrentClosure = false;
            return;
        }

        if (!_net.IsServer)
            return;

        if (args.OldState is WoundState.Closed or WoundState.Healed ||
            !_prototypes.TryIndex(wound.Comp.Prototype, out var source) ||
            !source.TryGetBehavior(wound.Comp.PeakSeverity, out WoundScarBehavior scar) ||
            wound.Comp.PeakSeverity < scar.Threshold)
            return;

        var baseChance = Math.Clamp(scar.Chance, 0f, 1f);
        if (baseChance <= 0f)
            return;

        var globalChance = Math.Clamp(_cfg.GetCVar(CCVars.SurgeryScarChance), 0f, 1f);
        var finalChance = baseChance * globalChance;
        if (finalChance <= 0f)
            return;

        if (finalChance < 1f && !_random.Prob(finalChance))
            return;

        CreateScar((wound.Owner, wound.Comp));
    }

    private void OnTreatmentAttempt(Entity<WoundScarComponent> scar, ref WoundTreatmentAttemptEvent args)
    {
        args.Cancelled = true;
    }

    public EntityUid? CreateScar(Entity<WoundComponent?> source)
    {
        if (!_net.IsServer || !Resolve(source, ref source.Comp, false) ||
            source.Comp.State == WoundState.Scarred || source.Comp.ScarCreatedForCurrentClosure ||
            !TryComp(source.Comp.HoldingPart, out BodyPartComponent? part) ||
            TryComp(source.Comp.HoldingPart, out WoundableComponent? woundable) &&
            _prototypes.TryIndex(woundable.Profile, out var profile) && !profile.Scarrable)
            return null;

        var scar = _wounds.CreateOrMergeWound(source.Comp.HoldingPart, ScarWound, 1);
        if (scar is null)
            return null;

        AddComp<WoundScarComponent>(scar.Value);
        _wounds.SetWoundState(scar.Value, WoundState.Scarred);
        source.Comp.ScarCreatedForCurrentClosure = true;

        var created = new ScarCreatedEvent(part.Body, source.Comp.HoldingPart, scar.Value);
        RaiseLocalEvent(source.Comp.HoldingPart, ref created);
        RaiseLocalEvent(scar.Value, ref created);
        return scar;
    }
}
