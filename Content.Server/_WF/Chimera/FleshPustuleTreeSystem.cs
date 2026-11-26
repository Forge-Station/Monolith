using Content.Server.Hands.Systems;
using Content.Server.Parallax;
using Content.Shared.Interaction;
using Content.Shared._WF.Chimera;
using Content.Shared.Maps;
using Content.Shared.Parallax.Biomes;
using Content.Shared.Physics;
using Content.Shared.Popups;
using Content.Shared.Throwing;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Chimera;

/// <summary>Harvests pustule items from flesh trees and plants or bursts them.</summary>
public sealed partial class WFFleshPustuleTreeSystem : EntitySystem
{
    private static readonly EntProtoId Item = "WFFleshPustuleItem";
    private static readonly EntProtoId Nest = "WFFleshPustule";
    private static readonly SoundSpecifier HarvestSound = new SoundPathSpecifier("/Audio/_WF/Chimera/FleshTick/pustule_pop.ogg");
    [Dependency] private HandsSystem _hands = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private TurfSystem _turf = default!;
    [Dependency] private BiomeSystem _biomes = default!;
    [Dependency] private WFFleshPustuleSystem _pustules = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<WFFleshPustuleTreeComponent, MapInitEvent>(OnTreeInit);
        SubscribeLocalEvent<WFFleshPustuleTreeComponent, InteractHandEvent>(OnHarvest);
        SubscribeLocalEvent<WFFleshPustuleItemComponent, AfterInteractEvent>(OnPlant);
        SubscribeLocalEvent<WFFleshPustuleItemComponent, ThrownEvent>(OnThrown);
        SubscribeLocalEvent<WFFleshPustuleItemComponent, ThrowDoHitEvent>(OnImpact);
        SubscribeLocalEvent<WFFleshPustuleItemComponent, LandEvent>(OnLand);
    }

    private void OnTreeInit(Entity<WFFleshPustuleTreeComponent> ent, ref MapInitEvent args)
    {
        _appearance.SetData(ent, WFFleshTreeVisuals.Harvested, ent.Comp.Harvested);
        if (ent.Comp.Harvested) PinHarvest(ent);
    }

    private void OnHarvest(Entity<WFFleshPustuleTreeComponent> ent, ref InteractHandEvent args)
    {
        if (args.Handled) return;
        args.Handled = true;
        if (!TryHarvest(ent, args.User, out _))
            _popup.PopupEntity(Loc.GetString("wf-pustule-tree-empty"), ent, args.User);
    }

    public bool TryHarvest(Entity<WFFleshPustuleTreeComponent> tree, EntityUid user, out EntityUid? item)
    {
        item = null;
        if (tree.Comp.Harvested || !_interaction.InRangeUnobstructed(user, Transform(tree).Coordinates, predicate: uid => uid == tree.Owner)) return false;
        tree.Comp.Harvested = true;
        _appearance.SetData(tree, WFFleshTreeVisuals.Harvested, true);
        PinHarvest(tree);
        item = Spawn(Item, Transform(user).Coordinates);
        _hands.TryPickupAnyHand(user, item.Value);
        _audio.PlayPvs(HarvestSound, tree);
        return true;
    }

    private void PinHarvest(EntityUid tree)
    {
        var xform = Transform(tree);
        if (xform.MapUid is not { } ground || xform.GridUid != ground ||
            !TryComp<BiomeComponent>(ground, out var biome) || !TryComp<MapGridComponent>(ground, out var grid)) return;
        var tile = _map.TileIndicesFor(ground, grid, _transform.WithEntityId(xform.Coordinates, ground));
        _biomes.WfPinTiles((ground, biome), new[] { tile });
    }

    private void OnPlant(Entity<WFFleshPustuleItemComponent> ent, ref AfterInteractEvent args)
    {
        // Clicking a creature remains the normal food interaction; ground clicks plant the item.
        if (args.Handled || !args.CanReach || args.Target != null || ent.Comp.Thrown) return;
        if (TryPlantItem(ent, args.User, args.ClickLocation)) args.Handled = true;
    }

    public bool TryPlantItem(Entity<WFFleshPustuleItemComponent> item, EntityUid user, EntityCoordinates coordinates)
    {
        if (item.Comp.Consumed || item.Comp.Thrown ||
            !_interaction.InRangeUnobstructed(user, Transform(item).Coordinates, predicate: uid => uid == item.Owner) ||
            !_interaction.InRangeUnobstructed(user, coordinates, WFFleshPustuleSystem.PlantRange)) return false;
        if (_turf.GetTileRef(coordinates) is not { } tile || tile.Tile.IsEmpty ||
            _turf.IsTileBlocked(tile, CollisionGroup.MobMask) ||
            !TryComp<MapGridComponent>(tile.GridUid, out var grid)) return false;
        foreach (var entity in _map.GetAnchoredEntities(tile.GridUid, grid, tile.GridIndices))
            if (HasComp<WFFleshPustuleComponent>(entity)) return false;
        item.Comp.Consumed = true;
        Spawn(Nest, _map.GridTileToLocal(tile.GridUid, grid, tile.GridIndices));
        QueueDel(item);
        return true;
    }

    private void OnThrown(Entity<WFFleshPustuleItemComponent> ent, ref ThrownEvent args) => ent.Comp.Thrown = true;
    private void OnImpact(Entity<WFFleshPustuleItemComponent> ent, ref ThrowDoHitEvent args) => BurstItem(ent);
    private void OnLand(Entity<WFFleshPustuleItemComponent> ent, ref LandEvent args) => BurstItem(ent);

    public bool BurstItem(Entity<WFFleshPustuleItemComponent> item)
    {
        if (!item.Comp.Thrown || item.Comp.Consumed) return false;
        item.Comp.Consumed = true;
        var coordinates = Transform(item).Coordinates;
        var nest = Spawn(Nest, coordinates);
        // An impact always pops, even when the shared tick population budget is full.
        _pustules.TryBurst((nest, Comp<WFFleshPustuleComponent>(nest)), forceImpact: true);
        QueueDel(item);
        return true;
    }
}
