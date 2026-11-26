using System.Diagnostics.CodeAnalysis;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Robust.Shared.Audio.Systems;

namespace Content.Shared._WF.Planets.Jetpack;

/// <summary>
/// Where an atmospheric jetpack lights, what it says when it will not, and how it refuels: from any welding fuel
/// tank, the way a welder does. The burn itself is server side, in <c>WFAtmosphericJetpackFuelSystem</c>.
/// </summary>
public sealed partial class WFAtmosphericJetpackSystem : EntitySystem
{
    [Dependency] private SharedSolutionContainerSystem _solution = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedAudioSystem _audio = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WFAtmosphericJetpackComponent, AfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<WFAtmosphericJetpackComponent, ExaminedEvent>(OnExamined);
    }

    /// <summary>The planet layer an entity is in the air of: a ground or air layer, never orbit or off-world.</summary>
    public bool TryGetAirLayer(EntityUid user, [NotNullWhen(true)] out WFPlanetLayerComponent? layer)
    {
        layer = null;

        return TryComp(user, out TransformComponent? xform)
            && TryComp(xform.MapUid, out layer)
            && !HasComp<WFOrbitLayerComponent>(xform.MapUid);
    }

    /// <summary>Why the pack will not fly for this wearer here, as a loc key, or null when it will.</summary>
    public string? GetRefusal(Entity<WFAtmosphericJetpackComponent> pack, EntityUid user)
    {
        if (!TryGetAirLayer(user, out var layer))
            return "wf-jetpack-atmospheric-no-air";

        if (layer.Gravity > pack.Comp.MaxGravity)
            return "wf-jetpack-atmospheric-too-heavy";

        if (GetFuel(pack) <= FixedPoint2.Zero)
            return "wf-jetpack-atmospheric-no-fuel";

        return null;
    }

    public bool CanFly(Entity<WFAtmosphericJetpackComponent> pack, EntityUid user)
    {
        return GetRefusal(pack, user) == null;
    }

    public bool TryGetFuelSolution(Entity<WFAtmosphericJetpackComponent> pack,
        [NotNullWhen(true)] out Entity<SolutionComponent>? soln,
        [NotNullWhen(true)] out Solution? solution)
    {
        return _solution.TryGetSolution(pack.Owner, pack.Comp.FuelSolutionName, out soln, out solution);
    }

    /// <summary>Units of fuel left in the tank.</summary>
    public FixedPoint2 GetFuel(Entity<WFAtmosphericJetpackComponent> pack)
    {
        return TryGetFuelSolution(pack, out _, out var solution)
            ? solution.GetTotalPrototypeQuantity(pack.Comp.FuelReagent)
            : FixedPoint2.Zero;
    }

    /// <summary>Clicking a welding fuel tank with the pack tops it up, the welder interaction on a jetpack.</summary>
    private void OnAfterInteract(Entity<WFAtmosphericJetpackComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || args.Target is not { Valid: true } target || !args.CanReach)
            return;

        if (!TryComp(target, out ReagentTankComponent? tank)
            || tank.TankType != ReagentTankType.Fuel
            || !_solution.TryGetDrainableSolution(target, out var tankSoln, out var tankSolution)
            || !TryGetFuelSolution(ent, out var packSoln, out var packSolution))
            return;

        args.Handled = true;

        foreach (var reagent in tankSolution.Contents)
        {
            if (reagent.Reagent.Prototype != ent.Comp.FuelReagent.Id)
            {
                _popup.PopupClient(Loc.GetString("wf-jetpack-atmospheric-bad-fuel", ("tank", target)), ent, args.User);
                return;
            }
        }

        var amount = FixedPoint2.Min(packSolution.AvailableVolume, tankSolution.Volume);

        if (amount > FixedPoint2.Zero)
        {
            var drained = _solution.Drain(target, tankSoln.Value, amount);
            _solution.TryAddSolution(packSoln.Value, drained);
            _audio.PlayPredicted(ent.Comp.RefillSound, ent, args.User);
            _popup.PopupClient(Loc.GetString("wf-jetpack-atmospheric-refueled"), ent, args.User);
        }
        else if (packSolution.AvailableVolume <= FixedPoint2.Zero)
        {
            _popup.PopupClient(Loc.GetString("wf-jetpack-atmospheric-full"), ent, args.User);
        }
        else
        {
            _popup.PopupClient(Loc.GetString("wf-jetpack-atmospheric-tank-empty", ("tank", target)), ent, args.User);
        }
    }

    private void OnExamined(Entity<WFAtmosphericJetpackComponent> ent, ref ExaminedEvent args)
    {
        if (!TryGetFuelSolution(ent, out _, out var solution))
            return;

        args.PushMarkup(Loc.GetString("wf-jetpack-atmospheric-examine",
            ("fuel", GetFuel(ent).Int()), ("max", solution.MaxVolume.Int())));
    }
}
