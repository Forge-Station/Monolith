using System.Numerics;
using Content.Server._WF.Dormancy;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Shared._WF.WildSlimes;
using Content.Shared._WF.Xenobiology.Components;
using Content.Shared._WF.Xenobiology.Systems;
using Content.Shared.Maps;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Physics;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.WildSlimes;

/// <summary>
/// Keeps each world's wild slimes: fills its patches and grottos while a player is near and retires them once nobody
/// is, keeps them docile, fed and at home while wild, and makes them ordinary slimes once caught, tamed or taken away.
/// </summary>
public sealed partial class WFWildSlimeSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private ITileDefinitionManager _tileDefs = default!;
    [Dependency] private HTNSystem _htn = default!;
    [Dependency] private HungerSystem _hunger = default!;
    [Dependency] private MobStateSystem _mob = default!;
    [Dependency] private NpcFactionSystem _faction = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private ThirstSystem _thirst = default!;
    [Dependency] private TurfSystem _turf = default!;
    [Dependency] private WFDormancySystem _dormancy = default!;
    [Dependency] private XenobiologySystem _xeno = default!;

    /// <summary>How often patches are filled or emptied.</summary>
    public static readonly TimeSpan PatchInterval = TimeSpan.FromSeconds(2);

    /// <summary>How often each wild slime is fed and checked for capture.</summary>
    public static readonly TimeSpan SlimeInterval = TimeSpan.FromSeconds(1);

    /// <summary>How long a patch nobody is near keeps its slimes.</summary>
    public static readonly TimeSpan RetireDelay = TimeSpan.FromSeconds(30);

    /// <summary>Tiles past a patch's radius a slime may wander before it heads home.</summary>
    public const float LeashMargin = 2f;

    /// <summary>Hunger a wild slime is kept between; below what it grows or splits at.</summary>
    public const float MinWildHunger = 30f;
    public const float MaxWildHunger = 60f;

    public override void Initialize()
    {
        base.Initialize();

        InitializePatches();
        SubscribeLocalEvent<WFWildSlimeComponent, EntGotInsertedIntoContainerMessage>(OnInserted);
    }

    private void OnInserted(Entity<WFWildSlimeComponent> ent, ref EntGotInsertedIntoContainerMessage args)
    {
        Release(ent);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var grounds = EntityQueryEnumerator<WFWildSlimeGroundComponent>();
        while (grounds.MoveNext(out var uid, out var ground))
        {
            if (now < ground.NextUpdate || !_proto.TryIndex(ground.Habitat, out var habitat))
                continue;

            ground.NextUpdate = now + PatchInterval;
            foreach (var patch in ground.Patches)
            {
                UpdatePatch((uid, ground), habitat, patch, now);
            }
        }

        var slimes = EntityQueryEnumerator<WFWildSlimeComponent, TransformComponent>();
        while (slimes.MoveNext(out var uid, out var wild, out var xform))
        {
            if (now < wild.NextCheck)
                continue;

            wild.NextCheck = now + SlimeInterval;
            CheckWild((uid, wild), xform);
        }
    }

    /// <summary>Retires a patch's slimes once nobody has been near for a while, and frees the places of those gone.</summary>
    private void UpdatePatch(Entity<WFWildSlimeGroundComponent> ground, WFWildSlimeHabitatPrototype habitat, WFWildSlimePatch patch, TimeSpan now)
    {
        var active = _dormancy.IsActiveAt(ground, patch.Middle);
        if (active)
            patch.IdleSince = null;
        else
            patch.IdleSince ??= now;

        var retire = patch.IdleSince is { } since && now - since >= RetireDelay;
        foreach (var slot in patch.Surface)
        {
            RefreshSlot(slot, habitat, retire, now);
        }

        foreach (var slot in patch.Below)
        {
            RefreshSlot(slot, habitat, retire, now);
        }

        if (active)
            Populate(ground, habitat, patch, now);
    }

    private void RefreshSlot(WFWildSlimeSlot slot, WFWildSlimeHabitatPrototype habitat, bool retire, TimeSpan now)
    {
        if (slot.Slime is not { } slime)
            return;

        // Caught, tamed, carried off or dead: its place fills again after the respawn delay.
        if (TerminatingOrDeleted(slime) || !HasComp<WFWildSlimeComponent>(slime) || _mob.IsDead(slime))
        {
            slot.Slime = null;
            slot.ReadyAt = now + habitat.RespawnDelay;
            return;
        }

        if (!retire)
            return;

        // Nobody near: it goes, and comes back as soon as someone is.
        QueueDel(slime);
        slot.Slime = null;
        slot.ReadyAt = now;
    }

    /// <summary>Fills every free place in a patch and its grotto whose ground is loaded; returns how many slimes spawned.</summary>
    public int Populate(Entity<WFWildSlimeGroundComponent> ground, WFWildSlimeHabitatPrototype habitat, WFWildSlimePatch patch, TimeSpan now)
    {
        var spawned = 0;
        var leash = patch.Radius + LeashMargin;
        var patchTile = _tileDefs[habitat.PatchTile].TileId;

        foreach (var slot in patch.Surface)
        {
            if (slot.Slime != null || now < slot.ReadyAt)
                continue;

            slot.Slime = TrySpawn(ground, patch.Tiles, patchTile, habitat, patch.Middle, leash, ground.Comp.Cavern);
            if (slot.Slime != null)
                spawned++;
        }

        if (ground.Comp.Cavern is not { } cavern || TerminatingOrDeleted(cavern) || habitat.GrottoTile is not { } grottoId)
            return spawned;

        var grottoTile = _tileDefs[grottoId].TileId;
        foreach (var slot in patch.Below)
        {
            if (slot.Slime != null || now < slot.ReadyAt)
                continue;

            slot.Slime = TrySpawn(cavern, patch.Grotto, grottoTile, habitat, patch.Middle, leash, ground.Owner);
            if (slot.Slime != null)
                spawned++;
        }

        return spawned;
    }

    /// <summary>Spawns a wild slime on a free, loaded tile of the given kind among <paramref name="tiles"/>.</summary>
    private EntityUid? TrySpawn(EntityUid map, List<Vector2i> tiles, int tileId, WFWildSlimeHabitatPrototype habitat,
        Vector2 home, float leash, EntityUid? otherMap)
    {
        if (tiles.Count == 0 || !TryComp<MapGridComponent>(map, out var grid))
            return null;

        for (var attempt = 0; attempt < 8; attempt++)
        {
            var index = _random.Pick(tiles);
            if (!_map.TryGetTileRef(map, grid, index, out var tile)
                || tile.Tile.IsEmpty
                || tile.Tile.TypeId != tileId
                || _turf.IsTileBlocked(tile, CollisionGroup.MobMask))
                continue;

            var proto = _random.Prob(habitat.AdultChance) ? habitat.AdultPrototype : habitat.BabyPrototype;
            var coordinates = new EntityCoordinates(map, new Vector2(index.X + 0.5f, index.Y + 0.5f));
            if (_xeno.SpawnSlimeAt(coordinates, proto, habitat.Breed) is not { } slime)
                return null;

            _xeno.RandomizeStats(slime);
            MakeWild(slime, habitat, map, home, leash, otherMap);
            return slime;
        }

        return null;
    }

    /// <summary>Turns a slime wild: docile, in the habitat's wild faction, leashed to a spot on its map.</summary>
    public void MakeWild(EntityUid uid, WFWildSlimeHabitatPrototype habitat, EntityUid homeMap, Vector2 home, float leash, EntityUid? otherMap = null)
    {
        var wild = EnsureComp<WFWildSlimeComponent>(uid);
        wild.Habitat = habitat.ID;
        wild.HomeMap = homeMap;
        wild.OtherMap = otherMap;
        wild.Home = home;
        wild.Leash = leash;
        wild.NextCheck = _timing.CurTime + SlimeInterval;

        // Only whoever hurts it becomes its enemy; nobody is hostile to its faction.
        EnsureComp<NPCRetaliationComponent>(uid);
        var faction = EnsureComp<NpcFactionMemberComponent>(uid);
        _faction.ClearFactions((uid, faction), false);
        _faction.AddFaction((uid, faction), habitat.WildFaction);

        SetRootTask(uid, habitat.WildRootTask);
    }

    /// <summary>Makes a wild slime an ordinary xenobiology slime.</summary>
    public void Release(Entity<WFWildSlimeComponent> ent)
    {
        if (!_proto.TryIndex(ent.Comp.Habitat, out var habitat))
            return;

        SetRootTask(ent, habitat.TameRootTask);
        if (TryComp<NpcFactionMemberComponent>(ent, out var faction))
        {
            _faction.ClearFactions((ent, faction), false);
            _faction.AddFactions((ent, faction), habitat.TameFactions);
        }

        RemComp<NPCRetaliationComponent>(ent);
        RemCompDeferred<WFWildSlimeComponent>(ent);
    }

    private void SetRootTask(EntityUid uid, string task)
    {
        if (!TryComp<HTNComponent>(uid, out var htn))
            return;

        htn.RootTask = new HTNCompoundTask { Task = task };
        _htn.Replan(htn);
    }

    /// <summary>Releases a slime that was tamed, caught or carried off its world; otherwise keeps it fed and watered.</summary>
    private void CheckWild(Entity<WFWildSlimeComponent> ent, TransformComponent xform)
    {
        if (_mob.IsDead(ent))
            return;

        if (TryComp<SlimeComponent>(ent, out var slime) && slime.Tamer != null
            || _container.IsEntityInContainer(ent)
            || xform.MapUid != ent.Comp.HomeMap && xform.MapUid != ent.Comp.OtherMap)
        {
            Release(ent);
            return;
        }

        // Wild slimes neither starve nor grow fat enough to split.
        if (TryComp<HungerComponent>(ent, out var hunger))
        {
            var value = _hunger.GetHunger(hunger);
            if (value < MinWildHunger || value > MaxWildHunger)
                _hunger.SetHunger(ent, XenobiologySystem.StartingHunger, hunger);
        }

        if (TryComp<ThirstComponent>(ent, out var thirst))
        {
            var thresholds = thirst.ThirstThresholds;
            if (thresholds.TryGetValue(ThirstThreshold.Okay, out var okay) && thirst.CurrentThirst < okay)
                _thirst.SetThirst(ent, thirst, okay + 25f);
        }
    }
}
