using Content.Server.Fluids.Components;
using Content.Server.Fluids.EntitySystems;
using Content.Server.Nutrition.EntitySystems;
using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Wolfmed.Medical;
using Content.Shared.Chemistry;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reaction;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Popups;
using Content.Shared.Timing;
using Robust.Shared.Audio.Systems;

namespace Content.Server._WF.Wolfmed.Medical;

/// <summary>
/// Playtest 5, "targeting yourself with antiseptic spray makes you drink it": the spray bottle base is also a drink,
/// and a click on a person reached the drink before the spray. A spray marked <see cref="WolfmedAntisepticSprayComponent"/>
/// sprays the person clicked, or the user on a use in hand, straight onto their skin by the touch reaction the vapor
/// would have used, and never pours down anyone's throat. A click on the world still sprays a cloud.
/// </summary>
public sealed class WolfmedAntisepticSpraySystem : EntitySystem
{
    [Dependency] private ReactiveSystem _reactive = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private UseDelaySystem _useDelay = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WolfmedAntisepticSprayComponent, AfterInteractEvent>(OnAfterInteract,
            before: [typeof(DrinkSystem), typeof(SpraySystem)]);
        SubscribeLocalEvent<WolfmedAntisepticSprayComponent, UseInHandEvent>(OnUseInHand, before: [typeof(DrinkSystem)]);
    }

    private void OnAfterInteract(Entity<WolfmedAntisepticSprayComponent> spray, ref AfterInteractEvent args)
    {
        if (args.Handled || args.Target is not { } target || !args.CanReach || !HasComp<WoundHostComponent>(target))
            return;

        args.Handled = TrySprayOn(spray, args.User, target);
    }

    private void OnUseInHand(Entity<WolfmedAntisepticSprayComponent> spray, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = TrySprayOn(spray, args.User, args.User);
    }

    /// <summary>One press of the spray onto a body: the same dose the vapor carries, by the same touch reaction.</summary>
    public bool TrySprayOn(EntityUid spray, EntityUid user, EntityUid target)
    {
        if (!TryComp(spray, out SprayComponent? nozzle) ||
            !_solutions.TryGetSolution(spray, SprayComponent.SolutionName, out var soln, out var solution))
            return false;

        if (TryComp(spray, out UseDelayComponent? delay) && !_useDelay.TryResetDelay((spray, delay), checkDelayed: true))
            return true;

        if (solution.Volume <= 0)
        {
            _popup.PopupEntity(Loc.GetString("spray-component-is-empty-message"), spray, user);
            return true;
        }

        var dose = _solutions.SplitSolution(soln.Value, nozzle.TransferAmount);
        _reactive.DoEntityReaction(target, dose, ReactionMethod.Touch);
        _audio.PlayPvs(nozzle.SpraySound, spray);
        _popup.PopupEntity(user == target
                ? Loc.GetString("wolfmed-antiseptic-spray-self")
                : Loc.GetString("wolfmed-antiseptic-spray-other", ("target", target)),
            target, user);
        return true;
    }
}
