using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Shared._WF.Wolfmed.Reagents;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.DoAfter;
using Content.Shared.FixedPoint;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Popups;
using Robust.Shared.Audio.Systems;

namespace Content.Server._WF.Wolfmed.Medical;

/// <summary>
/// Playtest 3 IPC 2: refilling a body from a fluid pack by hand. A use moves up to the pack's transfer amount of the
/// body's own fluid and repeats until the body is full or the pack is empty. A pack holding anything the body does not
/// run on is refused whole: oil in an IPC is a foreign reagent, not hydraulic fluid.
/// </summary>
public sealed class WolfmedFluidPackSystem : EntitySystem
{
    [Dependency] private BloodstreamSystem _bloodstream = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WolfmedFluidPackComponent, UseInHandEvent>(OnUseInHand);
        SubscribeLocalEvent<WolfmedFluidPackComponent, AfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<WolfmedFluidPackComponent, WolfmedFluidPackDoAfterEvent>(OnDoAfter);
    }

    private void OnUseInHand(Entity<WolfmedFluidPackComponent> pack, ref UseInHandEvent args)
    {
        if (args.Handled || !HasComp<BloodstreamComponent>(args.User))
            return;

        args.Handled = true;
        TryStart(pack, args.User, args.User);
    }

    private void OnAfterInteract(Entity<WolfmedFluidPackComponent> pack, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } target || !HasComp<BloodstreamComponent>(target))
            return;

        args.Handled = true;
        TryStart(pack, target, args.User);
    }

    /// <summary>Starts a use if the pack can give this body anything, and says why not if it cannot.</summary>
    public bool TryStart(Entity<WolfmedFluidPackComponent> pack, EntityUid body, EntityUid user)
    {
        if (GetRefusal(pack, body) is { } refusal)
        {
            _popup.PopupEntity(Loc.GetString(refusal, ("pack", pack.Owner), ("target", Identity.Entity(body, EntityManager))),
                body, user);
            return false;
        }

        return _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, user, pack.Comp.Delay,
            new WolfmedFluidPackDoAfterEvent(), pack, body, pack)
        {
            NeedHand = true,
            BreakOnMove = true,
        });
    }

    private void OnDoAfter(Entity<WolfmedFluidPackComponent> pack, ref WolfmedFluidPackDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled || args.Args.Target is not { } body)
            return;

        args.Handled = true;
        if (!TryTransfer(pack, body, args.Args.User, out var moved))
            return;

        args.Repeat = moved > FixedPoint2.Zero && GetRefusal(pack, body) == null;
        if (!args.Repeat)
            _popup.PopupEntity(Loc.GetString("wolfmed-fluid-pack-done", ("target", Identity.Entity(body, EntityManager))),
                body, args.Args.User);
    }

    /// <summary>
    /// One use: up to the pack's transfer amount of the body's own fluid, out of the pack and into the bloodstream.
    /// Public so a test can skip the do-after.
    /// </summary>
    public bool TryTransfer(Entity<WolfmedFluidPackComponent> pack, EntityUid body, EntityUid user, out FixedPoint2 moved)
    {
        moved = FixedPoint2.Zero;
        if (GetRefusal(pack, body) is { } refusal)
        {
            _popup.PopupEntity(Loc.GetString(refusal, ("pack", pack.Owner), ("target", Identity.Entity(body, EntityManager))),
                body, user);
            return false;
        }

        if (!TryGetPackSolution(pack, out var soln, out var solution) ||
            !TryComp(body, out BloodstreamComponent? bloodstream) ||
            !_solutions.ResolveSolution(body, bloodstream.BloodSolutionName, ref bloodstream.BloodSolution, out var blood))
            return false;

        var amount = FixedPoint2.Min(pack.Comp.TransferAmount,
            FixedPoint2.Min(solution.Volume, blood.MaxVolume - blood.Volume));
        if (amount <= FixedPoint2.Zero)
            return false;

        var taken = _solutions.SplitSolution(soln.Value, amount);
        _bloodstream.TryModifyBloodLevel(body, taken.Volume, bloodstream);
        moved = taken.Volume;
        _audio.PlayPvs(pack.Comp.UseSound, body);
        return true;
    }

    /// <summary>The locale key for why the pack can give this body nothing, or null when it can.</summary>
    public string? GetRefusal(Entity<WolfmedFluidPackComponent> pack, EntityUid body)
    {
        if (!TryComp(body, out BloodstreamComponent? bloodstream) ||
            !_solutions.ResolveSolution(body, bloodstream.BloodSolutionName, ref bloodstream.BloodSolution, out var blood))
            return "wolfmed-fluid-pack-no-fluid";

        if (!TryGetPackSolution(pack, out _, out var solution) || solution.Volume <= FixedPoint2.Zero)
            return "wolfmed-fluid-pack-empty";

        if (solution.Contents.Any(reagent => reagent.Reagent.Prototype != bloodstream.BloodReagent.Id))
            return "wolfmed-fluid-pack-wrong";

        return blood.Volume >= blood.MaxVolume ? "wolfmed-fluid-pack-full" : null;
    }

    private bool TryGetPackSolution(Entity<WolfmedFluidPackComponent> pack,
        [NotNullWhen(true)] out Entity<SolutionComponent>? soln,
        [NotNullWhen(true)] out Solution? solution)
    {
        return _solutions.TryGetSolution(pack.Owner, pack.Comp.Solution, out soln, out solution);
    }
}
