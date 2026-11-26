using Content.Server._FarHorizons.Power.Generation.FissionGenerator;
using Content.Server._WF.NpcCrew.Components;
using Content.Server.Ame.Components;
using Content.Server.Ame.EntitySystems;
using Content.Server.Materials;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.Nodes;
using Content.Server.Power.Generator;
using Content.Server.Stack;
using Content.Server.Storage.Components;
using Content.Server.Storage.EntitySystems;
using Content.Shared._FarHorizons.Power.Generation.FissionGenerator;
using Content.Shared._WF.NpcCrew;
using Content.Shared.Ame.Components;
using Content.Shared.Atmos;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Materials;
using Content.Shared.Mobs.Components;
using Content.Shared.Power.Generator;
using Content.Shared.Stacks;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using System.Numerics;

namespace Content.Server._WF.NpcCrew.Systems;

/// <summary>
/// The engineer's work: keeping the ship's plant running from its own stores. A generator low on fuel is fed from the
/// fuel stowed aboard, the antimatter engine gets a fresh jar and is set injecting, a fission reactor has its spent
/// rods swapped for fresh ones and its control rods trimmed to the casing temperature, its turbine's stator load is
/// kept on the turbine's best speed, and a plant with fuel in it is switched on. Nothing is conjured: when the stores
/// are gone, or the engineer is dead, the ship goes dark.
/// </summary>
public sealed partial class WFCrewWorkSystem
{
    [Dependency] private GeneratorSystem _generators = default!;
    [Dependency] private NuclearReactorSystem _reactors = default!;
    [Dependency] private ReactorPartSystem _parts = default!;
    [Dependency] private NodeContainerSystem _nodes = default!;
    [Dependency] private MaterialStorageSystem _materials = default!;
    [Dependency] private AmeControllerSystem _ame = default!;
    [Dependency] private ItemSlotsSystem _slots = default!;
    [Dependency] private StackSystem _stacks = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private EntityStorageSystem _storage = default!;

    private static readonly TimeSpan TendInterval = TimeSpan.FromSeconds(5);
    private TimeSpan _nextTend;

    /// <summary>A turbine runs away in seconds, so its load is trimmed far more often than the rods.</summary>
    private static readonly TimeSpan TrimInterval = TimeSpan.FromSeconds(1);
    private TimeSpan _nextTrim;

    /// <summary>Fuel left in a generator, in its own units, below which the engineer brings more.</summary>
    private const float LowFuel = 10f;

    /// <summary>Antimatter left in the reactor's jar below which it is swapped for a fresh one.</summary>
    private const int LowAntimatter = 200;

    /// <summary>Stacks of solid fuel stowed per generator, and jars per reactor, when a ship is stocked.</summary>
    private const int StoresPerMachine = 2;

    /// <summary>Sheets in each stowed stack: small enough to go into a generator in one, whatever is left in it.</summary>
    private const int StackSize = 10;

    /// <summary>Radioactivity left in a fuel rod, neutron and plain together, below which it is spent and swapped out.</summary>
    private const float SpentRod = 1f;

    /// <summary>Pressure a commissioned reactor's coolant loop is charged to, in kPa.</summary>
    private const float CoolantPressure = 3000f;

    /// <summary>Casing temperatures between which the engineer leaves the control rods be, in kelvin.</summary>
    private const float ReactorCold = 700f;
    private const float ReactorHot = 1000f;

    /// <summary>Moles in the inlet below which the loop counts as dry: the reactor's own gauge lights at this.</summary>
    private const float DryLoop = 20f;

    /// <summary>The band about a turbine's best speed, as a share of it, inside which its load is left alone.</summary>
    private const float TurbineLowBand = 0.95f;
    private const float TurbineHighBand = 1.05f;

    /// <summary>The least the load is raised by while the blades are overspeeding and taking damage.</summary>
    private const float OverspeedStep = 1.5f;

    /// <summary>The share of the load left when the turbine has stalled under it.</summary>
    private const float StallCut = 0.5f;

    /// <summary>The least stator load the engineer sets; below it the turbine makes nothing worth having.</summary>
    private const float MinStatorLoad = 1000f;

    /// <summary>A turbine turning slower than this is standing, and nothing is read from its speed.</summary>
    private const float IdleRpm = 10f;

    /// <summary>Litres a second the turbine passes for each gas channel in the core, and on top of them.</summary>
    private const float FlowPerChannel = 100f;
    private const float FlowBase = 200f;

    private static readonly EntProtoId StoresCrate = "WFCrewFuelStores";
    private static readonly EntProtoId Jar = "AmeJar";
    private static readonly EntProtoId WeldingFuelCan = "JerryCanWeldingFuel";
    private static readonly EntProtoId FuelRod = "CerenkiteReactorFuelRod";

    /// <summary>Commissioned reactors whose coolant loop is still to be charged: a reactor's pipes only appear on its first tick.</summary>
    private readonly HashSet<EntityUid> _uncharged = new();

    /// <summary>Gives every idle engineer with a plant in need the job of seeing to it.</summary>
    private void TendPower()
    {
        if (_timing.CurTime >= _nextTrim)
        {
            _nextTrim = _timing.CurTime + TrimInterval;
            TrimTurbines();
        }

        if (_timing.CurTime < _nextTend)
            return;

        _nextTend = _timing.CurTime + TendInterval;
        ChargeLoops();
        var shelter = EntityManager.System<WFCrewShelterSystem>();
        var watched = new HashSet<EntityUid>();
        var query = EntityQueryEnumerator<WFCrewEngineerComponent, WFCrewComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var crew, out var xform))
        {
            if (!_mobs.IsAlive(uid) || HasComp<ActorComponent>(uid) || crew.Duty != WFCrewDuties.Guard
                || xform.GridUid is not { } grid || (crew.Post?.EntityId ?? grid) != grid)
                continue;

            // From his post the engineer keeps an eye on the reactor gauges whatever else he is at.
            if (watched.Add(grid))
                WatchReactors(grid);

            if (_jobs.ContainsKey(uid) || _weapons.HasLiveThreat(uid) || shelter.IsSheltering(uid))
                continue;

            if (!FindPlantWork(grid, uid, out var machine, out var fuel))
                continue;

            // With fuel to fetch the job starts at the stores; a plant that only wants switching on is walked to at once.
            var home = crew.Post ?? xform.Coordinates;
            _jobs[uid] = new Job(grid, crew.Group, WFCrewObjectiveKind.Resupply, fuel ?? machine, home, null, _timing.CurTime)
            {
                Deliver = machine,
                Returning = fuel == null,
            };
        }
    }

    /// <summary>The first machine of the ship's plant that wants fuel or switching on, with the fuel for it if any.</summary>
    private bool FindPlantWork(EntityUid grid, EntityUid engineer, out EntityUid machine, out EntityUid? fuel)
    {
        machine = default;
        fuel = null;
        var generators = EntityQueryEnumerator<FuelGeneratorComponent, TransformComponent>();
        while (generators.MoveNext(out var uid, out var generator, out var xform))
        {
            if (xform.GridUid != grid || !xform.Anchored || Tended(uid))
                continue;

            var left = _generators.GetFuel(uid);
            if (left < LowFuel && FindFuel(grid, uid, engineer) is { } item)
            {
                machine = uid;
                fuel = item;
                return true;
            }

            if (left > 0f && !generator.On)
            {
                machine = uid;
                return true;
            }
        }

        var reactors = EntityQueryEnumerator<AmeControllerComponent, TransformComponent>();
        while (reactors.MoveNext(out var uid, out var controller, out var xform))
        {
            if (xform.GridUid != grid || !xform.Anchored || Tended(uid))
                continue;

            var charge = Antimatter(controller);
            if (charge < LowAntimatter && FindJar(grid, engineer) is { } fresh)
            {
                machine = uid;
                fuel = fresh;
                return true;
            }

            if (charge > 0 && !controller.Injecting)
            {
                machine = uid;
                return true;
            }
        }

        var fission = EntityQueryEnumerator<NuclearReactorComponent, TransformComponent>();
        while (fission.MoveNext(out var uid, out var reactor, out var xform))
        {
            if (xform.GridUid != grid || !xform.Anchored || reactor.Melted || Tended(uid))
                continue;

            if (WantsFuel(reactor, out _) && FindRod(grid, engineer) is { } rod)
            {
                machine = uid;
                fuel = rod;
                return true;
            }
        }

        return false;
    }

    private int Antimatter(AmeControllerComponent controller)
    {
        return controller.FuelSlot.Item is { } jar && TryComp<AmeFuelContainerComponent>(jar, out var contents) ? contents.FuelAmount : 0;
    }

    /// <summary>Whether an engineer is already on his way to a machine.</summary>
    private bool Tended(EntityUid machine)
    {
        foreach (var job in _jobs.Values)
        {
            if (job.Deliver == machine)
                return true;
        }

        return false;
    }

    /// <summary>Fuel aboard that a generator burns: a stack of its material, or a can of its reagent.</summary>
    private EntityUid? FindFuel(EntityUid grid, EntityUid generator, EntityUid engineer)
    {
        if (TryComp<SolidFuelGeneratorAdapterComponent>(generator, out var solid))
        {
            var stacks = EntityQueryEnumerator<PhysicalCompositionComponent, StackComponent, TransformComponent>();
            while (stacks.MoveNext(out var uid, out var composition, out _, out var xform))
            {
                if (xform.GridUid == grid && composition.MaterialComposition.ContainsKey(solid.FuelMaterial) && Loose(uid, engineer))
                    return uid;
            }

            return null;
        }

        if (!TryComp<ChemicalFuelGeneratorAdapterComponent>(generator, out var chemical))
            return null;

        var cans = EntityQueryEnumerator<DrainableSolutionComponent, TransformComponent>();
        while (cans.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid != grid || !Loose(uid, engineer) || !_solutions.TryGetDrainableSolution(uid, out _, out var solution))
                continue;

            foreach (var reagent in chemical.Reagents.Keys)
            {
                if (solution.GetTotalPrototypeQuantity(reagent.Id) > 0)
                    return uid;
            }
        }

        return null;
    }

    private EntityUid? FindJar(EntityUid grid, EntityUid engineer)
    {
        var jars = EntityQueryEnumerator<AmeFuelContainerComponent, TransformComponent>();
        while (jars.MoveNext(out var uid, out var jar, out var xform))
        {
            if (xform.GridUid == grid && jar.FuelAmount >= LowAntimatter && Loose(uid, engineer))
                return uid;
        }

        return null;
    }

    /// <summary>Whether an item is there for the taking: lying about or in a crate, not in a machine or somebody else's hands.</summary>
    private bool Loose(EntityUid item, EntityUid engineer)
    {
        if (Claimed(item))
            return false;
        if (!_containers.TryGetContainingContainer(item, out var container))
            return true;

        var owner = container.Owner;
        return owner == engineer
            || !HasComp<MobStateComponent>(owner) && !HasComp<MaterialStorageComponent>(owner) && !HasComp<AmeControllerComponent>(owner)
            && !HasComp<NuclearReactorComponent>(owner);
    }

    /// <summary>
    /// The engineer at the machine: feeds it what he carries, swapping out the reactor's spent jar first, then starts it.
    /// </summary>
    private bool Tend(EntityUid mob, Job job, EntityUid machine)
    {
        if (TerminatingOrDeleted(machine))
        {
            _jobs.Remove(mob);
            return true;
        }

        if (job.Target != machine)
        {
            if (!_hands.IsHolding(mob, job.Target, out _))
            {
                Fail(job, false);
                return true;
            }

            // A fuel rod goes straight into the grid, the spent one coming out onto the deck beside the reactor.
            if (TryComp<NuclearReactorComponent>(machine, out var reactor))
            {
                if (!Load((machine, reactor), job.Target))
                    _hands.TryDrop(mob, job.Target);
                _jobs.Remove(mob);
                return true;
            }

            if (TryComp<AmeControllerComponent>(machine, out var controller))
            {
                if (controller.Injecting)
                    _ame.SetInjecting(machine, false, mob, controller);
                if (controller.FuelSlot.Item != null)
                    _slots.TryEject(machine, controller.FuelSlot, mob, out _);
            }

            _hands.TrySelect(mob, job.Target);
            _interaction.InteractUsing(mob, job.Target, machine, Transform(machine).Coordinates);
        }

        Start(machine, mob);
        _jobs.Remove(mob);
        return true;
    }

    /// <summary>Switches on a generator with fuel in it, or sets a fuelled reactor injecting at the textbook rate.</summary>
    private void Start(EntityUid machine, EntityUid? user)
    {
        if (TryComp<FuelGeneratorComponent>(machine, out var generator))
        {
            if (!generator.On && _generators.GetFuel(machine) > 0f)
                _generators.SetFuelGeneratorOn(machine, true, generator);
            return;
        }

        if (!TryComp<AmeControllerComponent>(machine, out var controller) || Antimatter(controller) <= 0)
            return;

        var grid = Transform(machine).GridUid;
        var cores = 0;
        var shields = EntityQueryEnumerator<AmeShieldComponent, TransformComponent>();
        while (shields.MoveNext(out _, out var shield, out var xform))
        {
            if (xform.GridUid == grid && xform.Anchored && shield.IsCore)
                cores++;
        }

        // Two a core is the rate the reactor is built for; past four it starts to come apart.
        if (!controller.Injecting)
        {
            _ame.SetInjectionAmount(machine, Math.Max(2, cores * 2), user, controller);
            _ame.SetInjecting(machine, true, user, controller);
        }
    }

    /// <summary>
    /// Tops up a plant that has run low with no engineer to do it: a fighter's crew see to their own generator between
    /// sorties. Generators are filled and lit, a spent antimatter jar is swapped for a fresh one, spent fuel rods for
    /// fresh ones, and the control rods are trimmed.
    /// </summary>
    public void Refuel(EntityUid grid)
    {
        foreach (var machine in Plant(grid))
        {
            if (TryComp<FuelGeneratorComponent>(machine, out _))
            {
                if (_generators.GetFuel(machine) < LowFuel)
                    Fill(machine);
            }
            else if (TryComp<NuclearReactorComponent>(machine, out var reactor))
            {
                Prime((machine, reactor));
            }
            else if (TryComp<AmeControllerComponent>(machine, out var controller) && Antimatter(controller) < LowAntimatter)
            {
                if (controller.Injecting)
                    _ame.SetInjecting(machine, false, null, controller);
                if (controller.FuelSlot.Item is { } spent && _slots.TryEject(machine, controller.FuelSlot, null, out _))
                    QueueDel(spent);
                var jar = Spawn(Jar, Transform(machine).Coordinates);
                if (!_slots.TryInsert(machine, controller.FuelSlot, jar, null))
                    QueueDel(jar);
            }

            Start(machine, null);
        }

        WatchReactors(grid);
        TrimTurbines(grid);
    }

    /// <summary>Fills a generator with what it burns.</summary>
    private void Fill(EntityUid generator)
    {
        if (TryComp<SolidFuelGeneratorAdapterComponent>(generator, out var solid) && TryComp<MaterialStorageComponent>(generator, out var storage)
            && storage.StorageLimit is { } limit)
        {
            var room = limit - _materials.GetMaterialAmount(generator, solid.FuelMaterial, storage);
            if (room > 0)
                _materials.TryChangeMaterialAmount(generator, solid.FuelMaterial, room, storage);
        }
        else if (TryComp<ChemicalFuelGeneratorAdapterComponent>(generator, out var chemical)
            && _solutions.ResolveSolution(generator, chemical.SolutionName, ref chemical.Solution, out var solution))
        {
            foreach (var reagent in chemical.Reagents.Keys)
            {
                _solutions.TryAddReagent(chemical.Solution.Value, reagent.Id, solution.AvailableVolume);
                break;
            }
        }
    }

    /// <summary>Whether any of a ship's plant has burned dry: a generator out of fuel, or an antimatter engine with an empty jar or none.</summary>
    public bool PlantDry(EntityUid grid)
    {
        foreach (var machine in Plant(grid))
        {
            if (HasComp<FuelGeneratorComponent>(machine) && _generators.GetFuel(machine) <= 0f
                || TryComp<AmeControllerComponent>(machine, out var controller) && Antimatter(controller) <= 0)
                return true;
        }

        return false;
    }

    /// <summary>Whether a grid has a plant an engineer could tend: a fuel generator, an antimatter engine or a fission reactor.</summary>
    public bool HasPlant(EntityUid grid) => Plant(grid).Count > 0;

    private List<EntityUid> Plant(EntityUid grid)
    {
        var plant = new List<EntityUid>();
        var generators = EntityQueryEnumerator<FuelGeneratorComponent, TransformComponent>();
        while (generators.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid == grid && xform.Anchored)
                plant.Add(uid);
        }

        var reactors = EntityQueryEnumerator<AmeControllerComponent, TransformComponent>();
        while (reactors.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid == grid && xform.Anchored)
                plant.Add(uid);
        }

        var fission = EntityQueryEnumerator<NuclearReactorComponent, TransformComponent>();
        while (fission.MoveNext(out var uid, out var reactor, out var xform))
        {
            if (xform.GridUid == grid && xform.Anchored && !reactor.Melted)
                plant.Add(uid);
        }

        return plant;
    }

    /// <summary>The nearest safe tile to the plant, within a few tiles of it, for the engineer's post.</summary>
    public bool TryFindPost(EntityUid grid, out EntityCoordinates post)
    {
        post = default;
        if (!TryComp<MapGridComponent>(grid, out var gridComp))
            return false;

        var plant = Plant(grid);
        for (var ring = 1; ring <= PostReach; ring++)
        {
            foreach (var machine in plant)
            {
                var tile = _maps.TileIndicesFor(grid, gridComp, Transform(machine).Coordinates);
                for (var dx = -ring; dx <= ring; dx++)
                {
                    for (var dy = -ring; dy <= ring; dy++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != ring || !_planner.IsSafePost(grid, tile + new Vector2i(dx, dy), gridComp))
                            continue;

                        post = _maps.GridTileToLocal(grid, gridComp, tile + new Vector2i(dx, dy));
                        return true;
                    }
                }
            }
        }

        return false;
    }

    /// <summary>How many tiles from the plant the engineer's post may be.</summary>
    private const int PostReach = 4;

    /// <summary>
    /// Puts a ship's plant in running order, as a crew that has been flying her would have: generators full and lit,
    /// the antimatter engine jarred and injecting, the fission reactor fuelled with its loop charged and its control
    /// rods half out. For hulls that come off the slip empty.
    /// </summary>
    public void Commission(EntityUid grid)
    {
        var fission = EntityQueryEnumerator<NuclearReactorComponent, TransformComponent>();
        while (fission.MoveNext(out var uid, out var reactor, out var xform))
        {
            if (xform.GridUid != grid || !xform.Anchored || reactor.Melted)
                continue;

            Prime((uid, reactor));
            SharedNuclearReactorSystem.AdjustControlRods(reactor, 1f - reactor.ControlRodInsertion);
            _uncharged.Add(uid);
        }

        PrimeTurbines(grid);

        var generators = EntityQueryEnumerator<FuelGeneratorComponent, TransformComponent>();
        while (generators.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid != grid || !xform.Anchored)
                continue;

            Fill(uid);
            Start(uid, null);
        }

        var reactors = EntityQueryEnumerator<AmeControllerComponent, TransformComponent>();
        while (reactors.MoveNext(out var uid, out var controller, out var xform))
        {
            if (xform.GridUid != grid || !xform.Anchored)
                continue;

            if (controller.FuelSlot.Item == null)
            {
                var jar = Spawn(Jar, xform.Coordinates);
                if (!_slots.TryInsert(uid, controller.FuelSlot, jar, null))
                    QueueDel(jar);
            }

            Start(uid, null);
        }
    }

    /// <summary>
    /// Stows fuel for the trip in a crate in the hold: stacks of each generator's material, a can for a chemical
    /// generator, jars for a reactor. What the engineer feeds the plant from, and what a boarder can carry off.
    /// <paramref name="stores"/> is the stacks and jars stowed per generator.
    /// </summary>
    public bool StockFuel(EntityUid grid, int stores = StoresPerMachine)
    {
        var tiles = _planner.HoldTiles(grid, 1);
        if (tiles.Count == 0)
            return false;

        var crate = Spawn(StoresCrate, tiles[0]);
        var stocked = 0;
        foreach (var machine in Plant(grid))
        {
            if (TryComp<SolidFuelGeneratorAdapterComponent>(machine, out var solid))
            {
                if (!_prototypes.TryIndex<MaterialPrototype>(solid.FuelMaterial, out var material) || material.StackEntity is not { } stackId)
                    continue;

                for (var i = 0; i < stores; i++)
                {
                    var stack = Spawn(stackId, tiles[0]);
                    _stacks.SetCount(stack, StackSize);
                    Stow(stack, crate);
                    stocked++;
                }
            }
            else if (HasComp<ChemicalFuelGeneratorAdapterComponent>(machine))
            {
                Stow(Spawn(WeldingFuelCan, tiles[0]), crate);
                stocked++;
            }
            else if (HasComp<AmeControllerComponent>(machine))
            {
                for (var i = 0; i < stores; i++)
                {
                    Stow(Spawn(Jar, tiles[0]), crate);
                    stocked++;
                }
            }
            else if (TryComp<NuclearReactorComponent>(machine, out var reactor))
            {
                // One fresh rod for every fuel slot: a full change of core.
                for (var i = 0; i < FuelSlots(reactor).Count; i++)
                {
                    Stow(Spawn(FuelRod, tiles[0]), crate);
                    stocked++;
                }
            }
        }

        if (stocked > 0)
            return true;

        QueueDel(crate);
        return false;
    }

    /// <summary>
    /// Tops a ship's stores back up when the stacks or jars in its stores crate have fallen below the usual two for any
    /// of its generators, or restocks a crate that is gone. For a persistent ship, whose engineer would otherwise
    /// starve the plant when the first stock ran out. Returns whether anything was added.
    /// </summary>
    public bool RestockFuel(EntityUid grid)
    {
        EntityUid? crate = null;
        var crates = EntityQueryEnumerator<EntityStorageComponent, MetaDataComponent, TransformComponent>();
        while (crates.MoveNext(out var uid, out _, out var meta, out var xform))
        {
            if (xform.GridUid == grid && meta.EntityPrototype?.ID == StoresCrate.Id)
            {
                crate = uid;
                break;
            }
        }

        if (crate is not { } stores)
            return StockFuel(grid);

        var contents = Comp<EntityStorageComponent>(stores).Contents.ContainedEntities;
        var machines = new Dictionary<EntProtoId, int>();
        var jars = 0;
        foreach (var machine in Plant(grid))
        {
            if (TryComp<SolidFuelGeneratorAdapterComponent>(machine, out var solid)
                && _prototypes.TryIndex<MaterialPrototype>(solid.FuelMaterial, out var material) && material.StackEntity is { } stackId)
                machines[stackId] = machines.GetValueOrDefault(stackId) + 1;
            else if (HasComp<AmeControllerComponent>(machine))
                jars++;
        }

        var added = false;
        foreach (var (stackId, count) in machines)
        {
            var have = 0;
            foreach (var item in contents)
            {
                if (MetaData(item).EntityPrototype?.ID == stackId.Id)
                    have++;
            }

            var missing = Math.Max(0, StoresPerMachine * count - have);
            for (var i = 0; i < missing; i++)
            {
                var stack = Spawn(stackId, Transform(stores).Coordinates);
                _stacks.SetCount(stack, StackSize);
                Stow(stack, stores);
                added = true;
            }
        }

        if (jars > 0)
        {
            var have = 0;
            foreach (var item in contents)
            {
                if (TryComp<AmeFuelContainerComponent>(item, out var jar) && jar.FuelAmount >= LowAntimatter)
                    have++;
            }

            var missing = Math.Max(0, StoresPerMachine * jars - have);
            for (var i = 0; i < missing; i++)
            {
                Stow(Spawn(Jar, Transform(stores).Coordinates), stores);
                added = true;
            }
        }

        return added;
    }

    /// <summary>Puts an item in the stores crate; one that won't go in is left beside it.</summary>
    private void Stow(EntityUid item, EntityUid crate)
    {
        _storage.Insert(item, crate);
    }

    /// <summary>
    /// The slots of a reactor grid meant for fuel: those holding a fuel rod, and the empty ones the prefab left in the
    /// thick of the grid, hemmed in by control rods and exchangers on three sides or more.
    /// </summary>
    private static List<Vector2i> FuelSlots(NuclearReactorComponent reactor)
    {
        var slots = new List<Vector2i>();
        if (reactor.ComponentGrid == null)
            return slots;

        for (var x = 0; x < reactor.ReactorGridWidth; x++)
        {
            for (var y = 0; y < reactor.ReactorGridHeight; y++)
            {
                var part = reactor.ComponentGrid[x, y];
                if (part != null ? part.HasRodType(ReactorPartComponent.RodTypes.FuelRod) : Neighbours(reactor, x, y) >= 3)
                    slots.Add(new Vector2i(x, y));
            }
        }

        return slots;
    }

    /// <summary>How many of the four slots around one hold a part.</summary>
    private static int Neighbours(NuclearReactorComponent reactor, int x, int y)
    {
        var count = 0;
        if (x > 0 && reactor.ComponentGrid[x - 1, y] != null)
            count++;
        if (x < reactor.ReactorGridWidth - 1 && reactor.ComponentGrid[x + 1, y] != null)
            count++;
        if (y > 0 && reactor.ComponentGrid[x, y - 1] != null)
            count++;
        if (y < reactor.ReactorGridHeight - 1 && reactor.ComponentGrid[x, y + 1] != null)
            count++;
        return count;
    }

    /// <summary>The first fuel slot that is empty or holds a spent rod, if there is one.</summary>
    private bool WantsFuel(NuclearReactorComponent reactor, out Vector2i slot)
    {
        foreach (var candidate in FuelSlots(reactor))
        {
            var part = _reactors.GetPart(reactor, candidate);
            if (part == null || Spent(part))
            {
                slot = candidate;
                return true;
            }
        }

        slot = default;
        return false;
    }

    /// <summary>Whether a fuel rod has given up its radioactivity. A melted rod is past replacing and never spent.</summary>
    private bool Spent(ReactorPartComponent part)
    {
        if (part.Melted || !part.HasRodType(ReactorPartComponent.RodTypes.FuelRod))
            return false;

        if (part.Properties == null)
            _parts.SetProperties(part, out part.Properties);

        return part.Properties.NeutronRadioactivity + part.Properties.Radioactivity < SpentRod;
    }

    /// <summary>Whether a reactor has live fuel in it.</summary>
    private bool Fuelled(NuclearReactorComponent reactor)
    {
        foreach (var slot in FuelSlots(reactor))
        {
            if (_reactors.GetPart(reactor, slot) is { Melted: false } part && !Spent(part))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Whether the reactor's coolant loop has gas in it, on either side of the core. One waiting on its commissioning
    /// charge counts as cooled.
    /// </summary>
    private bool Cooled(EntityUid uid, NuclearReactorComponent reactor)
    {
        if (_uncharged.Contains(uid))
            return true;

        return Holds(reactor.InletEnt, reactor.PipeName) || Holds(reactor.OutletEnt, reactor.PipeName);
    }

    private bool Holds(EntityUid? pipe, string name)
    {
        return pipe is { } uid && _nodes.TryGetNode(uid, name, out PipeNode? node) && node.Air.TotalMoles >= DryLoop;
    }

    /// <summary>A fresh fuel rod aboard, for the taking.</summary>
    private EntityUid? FindRod(EntityUid grid, EntityUid engineer)
    {
        var rods = EntityQueryEnumerator<ReactorPartComponent, TransformComponent>();
        while (rods.MoveNext(out var uid, out var part, out var xform))
        {
            if (xform.GridUid == grid && part.HasRodType(ReactorPartComponent.RodTypes.FuelRod) && !Spent(part) && Loose(uid, engineer))
                return uid;
        }

        return null;
    }

    /// <summary>Puts a fresh rod into the first fuel slot that wants one, the spent rod coming out onto the deck first.</summary>
    private bool Load(Entity<NuclearReactorComponent> reactor, EntityUid rod)
    {
        if (!WantsFuel(reactor.Comp, out var slot))
            return false;
        if (_reactors.GetPart(reactor.Comp, slot) != null && _reactors.TryUnloadPart(reactor, slot) == null)
            return false;

        return _reactors.TryLoadPart(reactor, slot, rod);
    }

    /// <summary>Fuels every slot of a reactor that wants it with a fresh rod from nowhere; spent rods are scrapped.</summary>
    private void Prime(Entity<NuclearReactorComponent> reactor)
    {
        while (WantsFuel(reactor.Comp, out var slot))
        {
            if (_reactors.GetPart(reactor.Comp, slot) != null)
            {
                if (_reactors.TryUnloadPart(reactor, slot) is not { } spent)
                    return;
                QueueDel(spent);
            }

            var fresh = Spawn(FuelRod, Transform(reactor).Coordinates);
            if (_reactors.TryLoadPart(reactor, slot, fresh))
                continue;

            QueueDel(fresh);
            return;
        }
    }

    /// <summary>Charges the coolant loops of the reactors commissioned since, once their pipes have appeared.</summary>
    private void ChargeLoops()
    {
        if (_uncharged.Count == 0)
            return;

        var charged = new List<EntityUid>();
        foreach (var uid in _uncharged)
        {
            if (!TryComp<NuclearReactorComponent>(uid, out var reactor) || TerminatingOrDeleted(uid))
            {
                charged.Add(uid);
                continue;
            }

            if (reactor.InletEnt is not { } inlet || reactor.OutletEnt is not { } outlet
                || !_nodes.TryGetNode(inlet, reactor.PipeName, out PipeNode? inletNode) || inletNode.Air.Immutable
                || !_nodes.TryGetNode(outlet, reactor.PipeName, out PipeNode? outletNode) || outletNode.Air.Immutable)
                continue;

            Charge(inletNode.Air);
            if (!ReferenceEquals(outletNode.Air, inletNode.Air))
                Charge(outletNode.Air);
            charged.Add(uid);
        }

        foreach (var uid in charged)
            _uncharged.Remove(uid);
    }

    /// <summary>Brings a pipe net up to the commissioning pressure with nitrogen at room temperature.</summary>
    private void Charge(GasMixture air)
    {
        if (air.Volume <= 0f)
            return;

        var moles = CoolantPressure * air.Volume / (Atmospherics.R * Atmospherics.T20C) - air.TotalMoles;
        if (moles <= 0f)
            return;

        var nitrogen = new GasMixture(air.Volume) { Temperature = Atmospherics.T20C };
        nitrogen.AdjustMoles(Gas.Nitrogen, moles);
        _atmos.Merge(air, nitrogen);
    }

    /// <summary>Has every living posted engineer trim the turbines of his ship.</summary>
    private void TrimTurbines()
    {
        var trimmed = new HashSet<EntityUid>();
        var query = EntityQueryEnumerator<WFCrewEngineerComponent, WFCrewComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var crew, out var xform))
        {
            if (!_mobs.IsAlive(uid) || HasComp<ActorComponent>(uid) || crew.Duty != WFCrewDuties.Guard
                || xform.GridUid is not { } grid || (crew.Post?.EntityId ?? grid) != grid || !trimmed.Add(grid))
                continue;

            TrimTurbines(grid);
        }
    }

    /// <summary>Trims the stator load of every working turbine on a grid.</summary>
    private void TrimTurbines(EntityUid grid)
    {
        var turbines = EntityQueryEnumerator<TurbineComponent, TransformComponent>();
        while (turbines.MoveNext(out var uid, out var turbine, out var xform))
        {
            if (xform.GridUid == grid && xform.Anchored && Trim(turbine))
                Dirty(uid, turbine);
        }
    }

    /// <summary>
    /// Sets a turbine's stator load to hold it at its best speed, as an engineer at the panel would. The speed settles
    /// at the heat coming through over the load, so the load is scaled by how far off the speed is: up while it runs
    /// fast, in big steps while the blades overspeed and take damage, down while it runs slow, and cut hard when the
    /// load has stalled it. Nothing is read from a turbine standing still or fed cold gas. True when the load changed.
    /// </summary>
    public static bool Trim(TurbineComponent turbine)
    {
        if (turbine.Ruined || turbine.Undertemp)
            return false;

        var best = turbine.BestRPM;
        var load = turbine.StatorLoad;
        float next;
        if (turbine.Stalling)
            next = load * StallCut;
        else if (turbine.RPM > best * TurbineHighBand)
            next = load * MathF.Max(turbine.RPM / best, turbine.Overspeed ? OverspeedStep : 1f);
        else if (turbine.RPM < best * TurbineLowBand && turbine.RPM > IdleRpm)
            next = load * MathF.Max(turbine.RPM / best, StallCut);
        else
            return false;

        next = Math.Clamp(next, MinStatorLoad, turbine.StatorLoadMax);
        if (MathF.Abs(next - load) < 1f)
            return false;

        turbine.StatorLoad = next;
        return true;
    }

    /// <summary>
    /// Sets each turbine's flow rate to what its core can pass, the textbook figure: a hundred litres a second for
    /// every gas channel in the nearest reactor and two hundred over.
    /// </summary>
    private void PrimeTurbines(EntityUid grid)
    {
        var reactors = new List<(Vector2 Position, int Channels)>();
        var fission = EntityQueryEnumerator<NuclearReactorComponent, TransformComponent>();
        while (fission.MoveNext(out var uid, out var reactor, out var xform))
        {
            if (xform.GridUid == grid && xform.Anchored && !reactor.Melted)
                reactors.Add((_transform.GetWorldPosition(uid), GasChannels(reactor)));
        }

        if (reactors.Count == 0)
            return;

        var turbines = EntityQueryEnumerator<TurbineComponent, TransformComponent>();
        while (turbines.MoveNext(out var uid, out var turbine, out var xform))
        {
            if (xform.GridUid != grid || !xform.Anchored || turbine.Ruined)
                continue;

            var here = _transform.GetWorldPosition(uid);
            var channels = 0;
            var nearest = float.MaxValue;
            foreach (var (position, count) in reactors)
            {
                var distance = (position - here).LengthSquared();
                if (distance >= nearest)
                    continue;

                nearest = distance;
                channels = count;
            }

            turbine.FlowRate = Math.Clamp(FlowBase + FlowPerChannel * channels, 0f, turbine.FlowRateMax);
            Dirty(uid, turbine);
        }
    }

    /// <summary>How many gas channels a reactor's grid holds.</summary>
    private static int GasChannels(NuclearReactorComponent reactor)
    {
        var channels = 0;
        if (reactor.ComponentGrid == null)
            return channels;

        for (var x = 0; x < reactor.ReactorGridWidth; x++)
        {
            for (var y = 0; y < reactor.ReactorGridHeight; y++)
            {
                if (reactor.ComponentGrid[x, y] is { } part && part.HasRodType(ReactorPartComponent.RodTypes.GasChannel))
                    channels++;
            }
        }

        return channels;
    }

    /// <summary>
    /// Minds the reactors on a grid as an engineer at the gauges would: rods in when the casing runs hot, out when it
    /// runs cold, and all the way in when the fuel is spent, the loop dry or the casing overheating, since a reactor
    /// run dry melts.
    /// </summary>
    private void WatchReactors(EntityUid grid)
    {
        var reactors = EntityQueryEnumerator<NuclearReactorComponent, TransformComponent>();
        while (reactors.MoveNext(out var uid, out var reactor, out var xform))
        {
            if (xform.GridUid != grid || !xform.Anchored || reactor.Melted)
                continue;

            if (!Fuelled(reactor) || !Cooled(uid, reactor) || reactor.Temperature >= reactor.ReactorOverheatTemp)
                SharedNuclearReactorSystem.AdjustControlRods(reactor, 2f);
            else if (reactor.Temperature > ReactorHot)
                SharedNuclearReactorSystem.AdjustControlRods(reactor, 0.2f);
            else if (reactor.Temperature < ReactorCold)
                SharedNuclearReactorSystem.AdjustControlRods(reactor, -0.1f);
        }
    }
}
