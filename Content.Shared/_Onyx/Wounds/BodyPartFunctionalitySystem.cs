using Content.Shared.Body;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.CCVar;
using Content.Shared._Shitmed.Cybernetics; // WOLFGATE(Wolfmed): Wolfgate keeps CyberneticsComponent under _Shitmed.
using Robust.Shared.Configuration;
using Robust.Shared.Network;

namespace Content.Shared._Onyx.Wounds;

public sealed partial class BodyPartFunctionalitySystem : EntitySystem
{
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private WoundSystem _wounds = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private IConfigurationManager _configuration = default!;

    public BodyPartFunctionalityState GetState(Entity<WoundableComponent?> part)
    {
        if (TryComp(part, out CyberneticsComponent? cybernetics) && cybernetics.Disabled)
            return BodyPartFunctionalityState.Disabled;

        if (!_configuration.GetCVar(CCVars.WoundsBodyPartFunctionalityEnabled))
            return BodyPartFunctionalityState.Functional;

        if (!Resolve(part, ref part.Comp, false) ||
            !TryComp(part, out BodyPartComponent? bodyPart))
            return BodyPartFunctionalityState.Unavailable;

        if (bodyPart.Body == null)
            return BodyPartFunctionalityState.Unavailable;

        var state = BodyPartFunctionalityState.Functional;
        foreach (var wound in _wounds.GetWounds(part))
        {
            if (wound.Comp.State is WoundState.Healed or WoundState.Scarred ||
                !TryComp(wound, out WoundFunctionalityComponent? functionality))
                continue;

            var woundState = functionality.State;
            if (TryComp(wound, out WoundFractureComponent? fracture))
            {
                if (fracture.Treatment == FractureTreatment.Mended)
                    continue;
                if (fracture.Treatment == FractureTreatment.Reduced && woundState == BodyPartFunctionalityState.Disabled)
                    woundState = BodyPartFunctionalityState.Impaired;
            }

            if (woundState > state)
                state = woundState;
        }

        return state;
    }

    public void Refresh(EntityUid body)
    {
        if (!_net.IsServer || !HasComp<WoundHostComponent>(body))
            return;

        foreach (var (part, _) in _body.GetBodyChildren(body))
            RefreshPart(body, part);
    }

    public void RefreshPart(EntityUid body, EntityUid part)
    {
        var functionality = EnsureComp<BodyPartFunctionalityComponent>(part);
        var state = GetState((part, (WoundableComponent?) null));
        if (functionality.State == state)
            return;

        var old = functionality.State;
        functionality.State = state;
        Dirty(part, functionality);
        var changed = new BodyPartFunctionalityChangedEvent(body, part, old, state);
        RaiseLocalEvent(part, ref changed);
    }
}
