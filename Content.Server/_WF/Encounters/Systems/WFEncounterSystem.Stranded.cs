using Content.Server._WF.Encounters.Components;
using Content.Server.Ame.Components;
using Content.Server.Ame.EntitySystems;
using Content.Server.Destructible;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Server.Power.Generator;
using Content.Server.Shuttles.Components;
using Content.Shared.Shuttles.Components;
using Content.Shared._WF.Encounters;
using Content.Shared.Power.Components;
using Content.Shared.Power.Generator;

namespace Content.Server._WF.Encounters.Systems;

public sealed partial class WFEncounterSystem
{
    [Dependency] private GeneratorSystem _generators = default!;
    [Dependency] private AmeControllerSystem _ame = default!;
    [Dependency] private BatterySystem _battery = default!;
    [Dependency] private DestructibleSystem _destructible = default!;

    /// <summary>Where forward (local north) thrust sits in a shuttle's linear thrust table.</summary>
    private const int ForwardThrust = 2;

    /// <summary>Whether a ship can drive ahead, as against only nudging sideways on a side thruster.</summary>
    private bool HasForwardThrust(EntityUid grid)
    {
        return TryComp<ShuttleComponent>(grid, out var shuttle) && shuttle.LinearThrust[ForwardThrust] > 0f;
    }

    /// <summary>How long a stranded ship must keep its thrust without a break before it counts as rescued.</summary>
    private static readonly TimeSpan RescueDelay = TimeSpan.FromSeconds(20);

    /// <summary>Whether a ship arrived stranded and players have not got it under way yet.</summary>
    public static bool IsStranded(WFEncounterShipState ship)
    {
        return ship.Stranding != WFEncounterStranding.None && !ship.Rescued;
    }

    /// <summary>What a ship's adrift call asks for, as the $need of its line: fuel, thrusters or none.</summary>
    private static string Need(WFEncounterShipState ship)
    {
        if (!IsStranded(ship))
            return "none";

        return ship.Stranding == WFEncounterStranding.Fuel ? "fuel" : "thrusters";
    }

    /// <summary>
    /// Leaves a ship stranded. For want of fuel its generators are emptied and stopped, its antimatter engine stopped
    /// and its jar taken, and its batteries run flat, breakers left on: fuel and a start bring the power back. With
    /// wrecked thrusters every thruster that drives it is destroyed, to be rebuilt or replaced.
    /// </summary>
    private void LeaveStranded(EntityUid grid, WFEncounterStranding how)
    {
        if (how == WFEncounterStranding.Thrusters)
        {
            var thrusters = new List<EntityUid>();
            var drives = EntityQueryEnumerator<ThrusterComponent, TransformComponent>();
            while (drives.MoveNext(out var uid, out var thruster, out var xform))
            {
                if (xform.GridUid == grid && thruster.Type == ThrusterType.Linear)
                    thrusters.Add(uid);
            }

            foreach (var uid in thrusters)
            {
                _destructible.DestroyEntity(uid);
            }

            return;
        }

        var generators = new List<Entity<FuelGeneratorComponent>>();
        var fuelled = EntityQueryEnumerator<FuelGeneratorComponent, TransformComponent>();
        while (fuelled.MoveNext(out var uid, out var generator, out var xform))
        {
            if (xform.GridUid == grid)
                generators.Add((uid, generator));
        }

        // Out of fuel means out of it: the spare sheets in the lockers and the spare jars are gone too.
        var fuels = new HashSet<string>();
        foreach (var (uid, _) in generators)
        {
            if (TryComp<Content.Server.Power.Generator.SolidFuelGeneratorAdapterComponent>(uid, out var adapter))
                fuels.Add(adapter.FuelMaterial);
        }

        var spares = new List<EntityUid>();
        var stacks = EntityQueryEnumerator<Content.Shared.Materials.PhysicalCompositionComponent, TransformComponent>();
        while (stacks.MoveNext(out var uid, out var made, out var xform))
        {
            if (xform.GridUid == grid && !xform.Anchored && HasComp<Content.Shared.Stacks.StackComponent>(uid)
                && System.Linq.Enumerable.Any(made.MaterialComposition.Keys, fuels.Contains))
                spares.Add(uid);
        }

        var jars = EntityQueryEnumerator<Content.Shared.Ame.Components.AmeFuelContainerComponent, TransformComponent>();
        while (jars.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid == grid)
                spares.Add(uid);
        }

        foreach (var uid in spares)
        {
            QueueDel(uid);
        }

        foreach (var (uid, generator) in generators)
        {
            // Burning more than is left empties it, part-used sheet and all.
            var fuel = _generators.GetFuel(uid);
            if (fuel > 0f)
                RaiseLocalEvent(uid, new GeneratorUseFuel(fuel * 2f + 1f));
            _generators.SetFuelGeneratorOn(uid, false, generator);
        }

        var engines = new List<Entity<AmeControllerComponent>>();
        var controllers = EntityQueryEnumerator<AmeControllerComponent, TransformComponent>();
        while (controllers.MoveNext(out var uid, out var controller, out var xform))
        {
            if (xform.GridUid == grid)
                engines.Add((uid, controller));
        }

        foreach (var (uid, controller) in engines)
        {
            _ame.SetInjecting(uid, false, null, controller);
            var slot = controller.FuelSlot;
            if (slot.Item is { } jar)
                QueueDel(jar);
        }

        var batteries = EntityQueryEnumerator<PowerNetworkBatteryComponent, BatteryComponent, TransformComponent>();
        while (batteries.MoveNext(out var uid, out _, out var battery, out var xform))
        {
            if (xform.GridUid == grid)
                _battery.SetCharge(uid, 0f, battery);
        }
    }

    /// <summary>
    /// Players got a stranded ship under way: its side's reward goes to them, and it flies its orders, or gets them
    /// when the encounter begins.
    /// </summary>
    private void Rescue(Entity<WFEncounterComponent> encounter, string key, WFEncounterShipState ship)
    {
        ship.Rescued = true;
        ship.UnderwaySince = null;
        ship.NextPower = _timing.CurTime + PowerInterval;
        Log.Info($"Encounter ship {ToPrettyString(ship.Grid)} of {ToPrettyString(encounter)} is under way again.");
        EntityManager.System<WFEncounterRewardSystem>().PayRescue(encounter, ship);

        if (!encounter.Comp.Begun || !_prototypes.TryIndex(encounter.Comp.Prototype, out var prototype))
            return;

        foreach (var entry in prototype.Ships)
        {
            if (entry.Key == key && !IssueShipOrders(encounter.Comp, prototype, entry, ship))
                Log.Error($"Encounter {prototype.ID} has invalid orders for ship '{key}'.");
        }
    }
}
