using System.Diagnostics;
using System.Linq;
using Content.Server._Mono.ShipRepair;
using Content.Server._WF.Encounters.Components;
using Content.Server._WF.NpcCrew;
using Content.Server._WF.NpcCrew.Components;
using Content.Server._WF.NpcCrew.Systems;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._WF.Encounters;
using Content.Shared._WF.NpcCrew;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Random;

namespace Content.Server._WF.Encounters.Systems;

/// <summary>
/// Building a ship's hull in stages. A ship spawned with its encounter is built in one go. One spawned from simulation is
/// built over several ticks so no tick carries it all: the hull is loaded onto a paused staging map, map-initialised a
/// batch at a time, moved into place, refitted, crewed a few men at a time, then powered up and handed its orders.
/// </summary>
public sealed partial class WFEncounterSystem
{
    [Dependency] private MapLoaderSystem _mapLoader = default!;
    [Dependency] private ShipRepairSystem _shipRepair = default!;

    /// <summary>How many entities of a staged hull are map-initialised a tick.</summary>
    private const int MapInitBatch = 250;

    /// <summary>How many crewmen of a staged ship are spawned a tick.</summary>
    private const int CrewBatch = 3;

    private enum BuildStage : byte
    {
        Load,
        MapInit,
        Place,
        Refit,
        Crew,
        Ready,
        Done,
    }

    /// <summary>A ship being built and how far it has got.</summary>
    private sealed class ShipBuild
    {
        public required Entity<WFEncounterComponent> Encounter;
        public required WFEncounterShip Ship;
        public required WFEncounterShipState State;
        public required MapCoordinates At;
        public WFEncounterManifestPrototype? Manifest;
        public EntityUid? Spawner;
        public BuildStage Stage = BuildStage.Load;
        public EntityUid Grid;
        public VesselPrototype Vessel = default!;
        public bool Engineer;
        public List<EntityUid> Crewmen = new();

        /// <summary>The crew still to spawn, and the mission they spawn under.</summary>
        public List<WFCrewSetupPost>? Posts;
        public WFCrewMission? Mission;
        public bool ProfileSkills;

        /// <summary>Entities of a staged hull still to map-initialise, parents first.</summary>
        public readonly Stack<EntityUid> Uninitialised = new();

        /// <summary>The simulated stand-in it replaces, if it is spawned from simulation; staged builds have one.</summary>
        public EntityUid? Proxy;

        /// <summary>The ship the stand-in counted as docked with.</summary>
        public EntityUid? Docked;

        /// <summary>Milliseconds spent on each stage, and the most on any one tick.</summary>
        public readonly double[] Spent = new double[(int) BuildStage.Done];
        public double Worst;
        public int Ticks;

        public bool Staged => Proxy != null;
    }

    /// <summary>The ship being built from simulation over several ticks, if any; the spawn queue waits on it.</summary>
    private ShipBuild? _building;

    /// <summary>A paused map nothing runs on, where staged hulls are loaded and initialised before they are placed.</summary>
    private EntityUid? _staging;

    /// <summary>Builds a planned ship whole where it stands: the real ship, spawned with its encounter. False if its hull or crew can't be had.</summary>
    private bool Build(Entity<WFEncounterComponent> encounter, WFEncounterShip ship, WFEncounterShipState state, MapCoordinates at,
        WFEncounterManifestPrototype? manifest, EntityUid? spawner)
    {
        var build = new ShipBuild { Encounter = encounter, Ship = ship, State = state, At = at, Manifest = manifest, Spawner = spawner };
        while (build.Stage != BuildStage.Done)
        {
            if (!StepBuild(build))
                return false;
        }

        return true;
    }

    /// <summary>Runs a build's next step and times it. False if the build failed; whatever it made is then removed.</summary>
    private bool StepBuild(ShipBuild build)
    {
        var watch = Stopwatch.StartNew();
        var stage = build.Stage;
        bool ok;
        try
        {
            ok = stage switch
            {
                BuildStage.Load => BuildLoad(build),
                BuildStage.MapInit => BuildMapInit(build),
                BuildStage.Place => BuildPlace(build),
                BuildStage.Refit => BuildRefit(build),
                BuildStage.Crew => BuildCrew(build),
                _ => BuildReady(build),
            };
        }
        finally
        {
            var spent = watch.Elapsed.TotalMilliseconds;
            build.Spent[(int) stage] += spent;
            build.Worst = Math.Max(build.Worst, spent);
            build.Ticks++;
        }

        if (!ok)
        {
            Log.Error($"Encounter ship '{build.Ship.Key}' could not be built at its {stage} stage.");
            DiscardHull(build);
            build.State.Building = false;
            return false;
        }

        if (build.Stage == BuildStage.Done)
        {
            build.State.Building = false;
            var spentByStage = string.Join(", ", Enum.GetValues<BuildStage>().Where(s => s != BuildStage.Done)
                .Select(s => $"{s.ToString().ToLowerInvariant()} {build.Spent[(int) s]:0.0}"));
            Log.Info($"Built {ToPrettyString(build.Grid)} ({build.Vessel.ID}) over {build.Ticks} steps, worst {build.Worst:0.0} ms: {spentByStage} ms.");
        }

        return true;
    }

    /// <summary>Removes a build's hull, staged or placed, with any crew it got; players aboard are left in space.</summary>
    private void DiscardHull(ShipBuild build)
    {
        if (!build.Grid.IsValid() || TerminatingOrDeleted(build.Grid))
            return;

        _setup.ClearCrew(build.Grid, build.State.Group, keepCorpses: false);
        _lifecycle.UnparentPlayersFromGrid(build.Grid, true);
    }

    /// <summary>Removes everything an abandoned build made: its hull and the stand-in it was to replace, orders and all.</summary>
    private void Discard(ShipBuild build)
    {
        DiscardHull(build);
        if (build.Proxy is not { } proxy || TerminatingOrDeleted(proxy))
            return;

        _objectives.Cancel(proxy, build.State.Group);
        Del(proxy);
    }

    /// <summary>
    /// Loads the hull. A ship built in one go is loaded where it stands; a staged one onto the paused staging map, its
    /// map-initialisation queued for the ticks that follow.
    /// </summary>
    private bool BuildLoad(ShipBuild build)
    {
        var state = build.State;
        if (state.Vessel is not { } id || !_prototypes.TryIndex(id, out var vessel))
            return false;

        build.Vessel = vessel;
        if (!build.Staged)
        {
            if (!_vessels.TrySpawnVessel(vessel, build.At.MapId, build.At.Position, build.Spawner, out var spawned))
                return false;

            build.Grid = spawned.Value;
            Claim(build);
            build.Stage = BuildStage.Refit;
            return true;
        }

        if (!_mapLoader.TryLoadGrid(StagingMap(), vessel.ShuttlePath, out var grid))
            return false;

        build.Grid = grid.Value.Owner;
        EntityManager.AddComponents(build.Grid, vessel.AddComponents);
        build.Uninitialised.Push(build.Grid);
        build.Stage = BuildStage.MapInit;
        return true;
    }

    /// <summary>The staging map, made paused and uninitialised the first time it is needed.</summary>
    private MapId StagingMap()
    {
        if (_staging is { } existing && !TerminatingOrDeleted(existing))
            return Transform(existing).MapID;

        var uid = _mapSystem.CreateMap(out var id, runMapInit: false);
        _mapSystem.SetPaused(uid, true);
        _meta.SetEntityName(uid, "encounter staging");
        _staging = uid;
        return id;
    }

    /// <summary>Map-initialises a batch of the staged hull, parents before children, so what one spawns as it starts is initialised in turn.</summary>
    private bool BuildMapInit(ShipBuild build)
    {
        if (TerminatingOrDeleted(build.Grid))
            return false;

        for (var i = 0; i < MapInitBatch && build.Uninitialised.TryPop(out var uid); i++)
        {
            if (TerminatingOrDeleted(uid))
                continue;

            var meta = MetaData(uid);
            if (meta.EntityLifeStage < EntityLifeStage.MapInitialized)
                EntityManager.RunMapInit(uid, meta);

            var children = Transform(uid).ChildEnumerator;
            while (children.MoveNext(out var child))
            {
                build.Uninitialised.Push(child);
            }
        }

        if (build.Uninitialised.Count == 0)
            build.Stage = BuildStage.Place;
        return true;
    }

    /// <summary>Moves the initialised hull from staging to where the stand-in is, and makes it the encounter's.</summary>
    private bool BuildPlace(ShipBuild build)
    {
        if (TerminatingOrDeleted(build.Grid) || !_mapSystem.TryGetMap(build.At.MapId, out var map))
            return false;

        _transform.SetCoordinates(build.Grid, new EntityCoordinates(map.Value, build.At.Position));
        _meta.SetEntityName(build.Grid, build.Vessel.Name);
        // The snapshot a ship repair device rebuilds the ship from, as the vessel spawner takes it.
        _shipRepair.GenerateRepairData(build.Grid);
        Claim(build);
        build.Stage = BuildStage.Refit;
        return true;
    }

    /// <summary>
    /// Marks the hull as the encounter's and readies its name and IFF. Spawned from simulation, it takes over from the
    /// stand-in here: clear of other grids, with whatever was aimed at the stand-in and its docking; its own orders follow
    /// once its crew are aboard.
    /// </summary>
    private void Claim(ShipBuild build)
    {
        var (state, grid) = (build.State, build.Grid);
        var marker = AddComp<WFEncounterGridComponent>(grid);
        marker.Encounter = build.Encounter;
        marker.Key = build.Ship.Key;
        Unload(grid);
        // A crew that is not allowed the hull's best plating gets it swapped out before anything else goes aboard.
        _setup.ApplyHullSwaps(grid, build.Ship.Profile);
        _setup.MarkProfile(grid, build.Ship.Profile);
        _meta.SetEntityName(grid, state.Name);
        // Warship designs come off the line with their IFF switched off. A crew flies with it on, so a scope shows
        // who is who; one lying in wait hides it again later.
        _shuttles.RemoveIFFFlag(grid, Content.Shared.Shuttles.Components.IFFFlags.HideLabel | Content.Shared.Shuttles.Components.IFFFlags.Hide);
        if (state.Color is { } color)
            _shuttles.SetIFFColor(grid, color);

        state.Grid = grid;
        state.Virtual = false;
        // Not judged, tended or taken out of the fight while it is half built.
        state.Building = true;
        if (build.Proxy is not { } proxy)
            return;

        Clear(grid, build.Docked);
        _objectives.Retarget(proxy, grid);
        RepointDocks(proxy, grid);
        if (build.Docked is { } partner && !TerminatingOrDeleted(partner))
            state.RedockTo = partner;
    }

    /// <summary>Raiders refit what they took: guns on its hardpoints and grime on its decks, before anyone goes aboard.</summary>
    private bool BuildRefit(ShipBuild build)
    {
        var state = build.State;
        // The seed keeps the same refit each time a simulated ship is spawned.
        if (state.Retrofit is { } retrofitId && _prototypes.TryIndex(retrofitId, out var retrofit))
            state.Retrofitted = _retrofit.Apply(build.Grid, retrofit, state.Seed);

        // Boarders come for the ship, not to shoot it up: their guns answer ship guns only. Docked, their clamps hold it.
        if (state.Hunt)
        {
            EnsureComp<WFHoldFireComponent>(build.Grid);
            EnsureComp<WFDockingClampComponent>(build.Grid);
        }

        if (state.Stealth)
            _shuttles.AddIFFFlag(build.Grid, Content.Shared.Shuttles.Components.IFFFlags.HideLabel);
        build.Stage = BuildStage.Crew;
        return true;
    }

    /// <summary>
    /// Loads the cargo and plans the crew, or wrecks the ship for a hulk; then spawns the crew, all at once or, staged, a
    /// few each tick.
    /// </summary>
    private bool BuildCrew(ShipBuild build)
    {
        if (build.Posts == null)
        {
            if (build.Ship.Derelict is { } derelict)
            {
                LoadCargo(build);
                Wreck(build.State, derelict, build.Vessel.ID);
                HandOver(build);
                build.Stage = BuildStage.Done;
                return true;
            }

            LoadCargo(build);
            if (!PlanCrew(build))
                return false;
        }

        var posts = build.Posts!;
        var count = build.Staged ? Math.Min(CrewBatch, posts.Count) : posts.Count;
        var batch = posts.GetRange(0, count);
        posts.RemoveRange(0, count);
        // A skill set on the ship is the crew's; left unset, the profile's pool gives the crew one, rolled once.
        if (!_setup.TrySpawn(build.Grid, batch, build.Mission!, build.ProfileSkills, out var crewmen))
            return false;

        build.ProfileSkills = false;
        build.Crewmen.AddRange(crewmen);
        if (posts.Count > 0)
            return true;

        if (build.Ship.Deed is { } deed)
            GiveDeed(build.Grid, build.Crewmen, deed);
        build.Stage = BuildStage.Ready;
        return true;
    }

    /// <summary>Cargo goes aboard first, so the crew are posted around it and not on it, and where it walls off no route.</summary>
    private void LoadCargo(ShipBuild build)
    {
        if (build.Manifest is not { Crates.Count: > 0 } manifest)
            return;

        foreach (var tile in _planner.CargoTiles(build.Grid, build.Ship.Cargo))
        {
            Spawn(_random.Pick(manifest.Crates), tile);
        }
    }

    /// <summary>Where the crew are posted and under what mission. False if the hull has nowhere to post them.</summary>
    private bool PlanCrew(ShipBuild build)
    {
        var (encounter, ship, state, grid) = (build.Encounter, build.Ship, build.State, build.Grid);
        var mission = new WFCrewMission
        {
            Group = state.Group,
            Callsign = Capped(state.Name, WFCrewLimits.MaxCallsign),
            Battlegroup = ship.Side.Length > 0 ? Capped($"enc{encounter.Owner.Id}-{ship.Side}", WFCrewLimits.MaxBattlegroup) : string.Empty,
            Profile = ship.Profile?.Id ?? string.Empty,
            Company = ship.Company?.Id ?? string.Empty,
            Faction = ship.Faction.Id,
            BoardingResponse = ship.Boarding,
            DockingResponse = ship.Docking,
            HeaveTo = ship.Evades,
            // Traders carry no fight worth having: attacked, they run.
            FleeAttackers = ship.Passengers.Count > 0,
            FaceThreats = ship.Platform,
            CallsForHelp = ship.Distress && ship.Passengers.Count == 0,
            Disengage = ship.Disengage,
            DisengageRange = ship.DisengageRange,
            Skill = state.Skill ?? WFCrewSkill.Veteran,
        };
        if (ship.Navigation is { } navigation && _prototypes.TryIndex(navigation, out var profile))
            mission.Navigation = profile.Settings.Clone();

        var posts = _setup.Plan(grid, ship.Deckhands + ship.Guards, ship.Captain);
        if (ship.Roles.Count > 0)
            posts.RemoveAll(post => !ship.Roles.Any(role => role.Id == post.Role));
        // The last deckhands of the plan are the ship's guards; a ship meant to have hands keeps one to work.
        var guards = ship.Deckhands > 0
            ? Math.Min(ship.Guards, posts.Count(post => post.Role == WFCrewRoles.Deckhand.Id) - 1)
            : ship.Guards;
        for (var i = posts.Count - 1; i >= 0 && guards > 0; i--)
        {
            if (posts[i].Role != WFCrewRoles.Deckhand.Id)
                continue;

            posts[i].Role = WFCrewRoles.Marine.Id;
            guards--;
        }

        // No deckhands asked for: the planner's posts inside the airlocks go unmanned too.
        if (ship.Deckhands <= 0)
            posts.RemoveAll(post => post.Role == WFCrewRoles.Deckhand.Id);

        // An engineer keeps the plant fuelled from the stores, where the ship's roles allow one: posted beside the plant,
        // or failing a safe tile there, in a deckhand's place or alongside the last of the crew.
        if (ship.Engineer && (ship.Roles.Count == 0 || ship.Roles.Any(role => role == WFCrewRoles.Engineer)) && _work.HasPlant(grid))
        {
            if (_work.TryFindPost(grid, out var plant))
                posts.Add(new WFCrewSetupPost { Role = WFCrewRoles.Engineer.Id, Position = plant.Position });
            else if (posts.FindLastIndex(post => post.Role == WFCrewRoles.Deckhand.Id) is var hand && hand >= 0)
                posts[hand].Role = WFCrewRoles.Engineer.Id;
            else if (posts.Count > 0)
                posts.Add(new WFCrewSetupPost { Role = WFCrewRoles.Engineer.Id, Position = posts[^1].Position });
            build.Engineer = posts.Any(post => post.Role == WFCrewRoles.Engineer.Id);
        }

        if (posts.Count == 0)
            return false;

        build.Posts = posts;
        build.Mission = mission;
        build.ProfileSkills = state.Skill == null;
        return true;
    }

    /// <summary>
    /// Powers the ship up, strands or masks it as planned and brings its passengers aboard. Spawned from simulation, it
    /// takes the stand-in's orders now, with its crew aboard to fly them, and the stand-in goes.
    /// </summary>
    private bool BuildReady(ShipBuild build)
    {
        var (ship, state, grid) = (build.Ship, build.State, build.Grid);
        // Shipyard hulls come with empty reactors and flat batteries; the crew arrive with theirs charged, unless the
        // ship is out of fuel. Stranding comes after the crew, whose arrival records the hull for repair devices.
        if (state.Stranding != WFEncounterStranding.Fuel)
        {
            // The crew arrive on a running ship: plant fuelled and lit, batteries charged, and fuel for the trip stowed
            // for the engineer. From here the plant burns what it has; nobody tops it up but him, from those stores.
            _power.SetPower(true, grid, false);
            _work.Commission(grid);
            if (build.Engineer)
                _work.StockFuel(grid, ship.FuelStores);
        }
        // A fighter with nobody to post by the plant keeps itself fuelled instead.
        state.AutoRefuel = !build.Engineer;
        if (state.Stranding != WFEncounterStranding.None)
            LeaveStranded(grid, state.Stranding);
        state.NextPower = _timing.CurTime + PowerInterval;

        if (state.Lurking)
            Mask(state);

        var berths = _planner.HoldTiles(grid, ship.Passengers.Count);
        for (var i = 0; i < ship.Passengers.Count && berths.Count > 0; i++)
        {
            var passenger = Spawn(ship.Passengers[i], berths[i % berths.Count]);
            var aboard = AddComp<WFEncounterPassengerComponent>(passenger);
            aboard.Encounter = build.Encounter;
            aboard.Key = ship.Key;
        }

        // Whatever came aboard with the ship is the crew's: taking it in front of them is theft.
        EntityManager.System<WFCrewConductSystem>().MarkProperty(grid, state.Group);
        state.Crew = build.Crewmen.Count;
        HandOver(build);
        build.Stage = BuildStage.Done;
        return true;
    }

    /// <summary>Spawned from simulation, the ship takes the stand-in's orders, with its crew aboard to fly them, and the stand-in goes.</summary>
    private void HandOver(ShipBuild build)
    {
        if (build.Proxy is not { } proxy)
            return;

        var state = build.State;
        _objectives.MoveQueue(proxy, build.Grid, state.Group);
        // It was alongside another ship: it comes alongside again for real, and counts as docked until then. A partner
        // still simulated is flown at until it is spawned too, when the docking order follows it to its hull.
        if (state.RedockTo is { } partner && !TerminatingOrDeleted(partner))
            _objectives.Prepend(build.Grid, state.Group, new WFCrewObjective { Kind = WFCrewObjectiveKind.Dock, Target = GetNetEntity(partner) });
        if (!TerminatingOrDeleted(proxy))
            Del(proxy);
    }

    /// <summary>
    /// Runs the next step of the ship being built from simulation, one a tick; finished, the ship is announced as
    /// spawned. False with nothing being built.
    /// </summary>
    private bool StepBuilding()
    {
        if (_building is not { } build)
            return false;

        // Its encounter ended, or its ship or stand-in went, while it was building (beaten in a simulated fight, say):
        // whatever it made goes, hull and stand-in both.
        if (TerminatingOrDeleted(build.Encounter) || build.State.Removed
            || build.Proxy is { } proxy && TerminatingOrDeleted(proxy) && build.Stage < BuildStage.Place)
        {
            Discard(build);
            build.State.Building = false;
            _building = null;
            return true;
        }

        if (!StepBuild(build))
        {
            // The hull or crew could not be had: the ship is lost, stand-in and all.
            if (build.Proxy is { } stale && !TerminatingOrDeleted(stale))
            {
                build.State.Grid = stale;
                build.State.Virtual = true;
            }

            RemoveShip(build.State);
            _building = null;
            return true;
        }

        if (build.Stage != BuildStage.Done)
            return true;

        _building = null;
        var ev = new WFEncounterShipSpawnedEvent(build.Encounter, build.Ship.Key, build.Grid);
        RaiseLocalEvent(ref ev);
        return true;
    }
}
