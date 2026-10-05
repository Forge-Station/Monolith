using Content.Server.Administration.Logs;
using Content.Server.Parallax;
using Content.Shared._WF.Caverns;
using Content.Shared.Burial.Components;
using Content.Shared.Database;
using Content.Shared.DoAfter;
using Content.Shared.Fluids.Components;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction;
using Content.Shared.Maps;
using Content.Shared.Parallax.Biomes;
using Content.Shared.Popups;
using Content.Shared.Tools.Components;
using Content.Shared.Tools.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Caverns;

/// <summary>
/// Lets a shovel dig a slow shaft into the cavern through any natural ground it can't otherwise dig, and keeps acid
/// from opening ground over a cavern that no tool can dig.
/// </summary>
// The hole queue fits out the shaft once the tile is empty. Ground a shovel digs anyway keeps its own dig.
public sealed partial class WFCavernDigSystem : EntitySystem
{
    [Dependency] private BiomeSystem _biome = default!;
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private IMapManager _mapManager = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private ITileDefinitionManager _tileDefs = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedToolSystem _tool = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    /// <summary>The tool speed <see cref="WFCavernMouthSpec.ShaftSeconds"/> is timed at: a standard shovel's.</summary>
    public const float ShaftToolSpeed = 0.5f;

    /// <summary>The tool quality that digs shafts.</summary>
    public const string DiggingQuality = "Digging";

    private static readonly Entity<MapGridComponent>? NoGrid = null;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ShovelComponent, AfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<ShovelComponent, WFCavernShaftDigDoAfterEvent>(OnShaftDug);
    }

    // Ground the shovel digs anyway is left to ToolTileCompatible, so the order of the two handlers doesn't matter.
    private void OnAfterInteract(Entity<ShovelComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target != null && !HasComp<PuddleComponent>(args.Target))
            return;

        if (!TryComp<ToolComponent>(ent, out var tool) || !_tool.HasQuality(ent, DiggingQuality, tool))
            return;

        var click = _transform.ToMapCoordinates(args.ClickLocation);
        if (!_mapManager.TryFindGridAt(click, out var gridUid, out var grid)
            || !TryComp<WFCavernGroundComponent>(gridUid, out var ground))
            return;

        var index = _map.TileIndicesFor(gridUid, grid, click);
        if (!CanDigShaft((gridUid, ground, grid), index, out var spec)
            || !_interaction.InRangeUnobstructed(args.User, _map.GridTileToLocal(gridUid, grid, index), popup: false))
            return;

        // Wrapped as a tool event, so the shovel's dig sound plays at the end. The world's time is the time: unlike a
        // tool's own use, the digger's hands don't shorten it.
        var dig = new SharedToolSystem.ToolDoAfterEvent(0f, new WFCavernShaftDigDoAfterEvent(GetNetEntity(gridUid), index), GetNetEntity(ent));
        var delay = TimeSpan.FromSeconds(spec.ShaftSeconds * ShaftToolSpeed / tool.SpeedModifier);
        var doAfter = new DoAfterArgs(EntityManager, args.User, delay, dig, ent, target: ent, used: ent)
        {
            BreakOnDamage = true,
            BreakOnMove = true,
            NeedHand = true,
            MultiplyDelay = false,
        };

        if (!_doAfter.TryStartDoAfter(doAfter))
            return;

        args.Handled = true;
        _popup.PopupEntity(Loc.GetString("wf-cavern-shaft-dig-start"), args.User, args.User);
        _popup.PopupEntity(Loc.GetString("wf-cavern-shaft-dig-start-others", ("user", Identity.Entity(args.User, EntityManager))), args.User,
            Filter.PvsExcept(args.User), true);
    }

    private void OnShaftDug(Entity<ShovelComponent> ent, ref WFCavernShaftDigDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        args.Handled = true;

        var gridUid = GetEntity(args.Grid);
        if (!TryComp<WFCavernGroundComponent>(gridUid, out var ground)
            || !TryComp<MapGridComponent>(gridUid, out var grid)
            || !CanDigShaft((gridUid, ground, grid), args.Tile, out _))
            return;

        _map.SetTile(gridUid, grid, args.Tile, Tile.Empty);
        _popup.PopupEntity(Loc.GetString("wf-cavern-shaft-dig-done"), args.User, args.User);
        _adminLog.Add(LogType.Tile, LogImpact.Medium,
            $"{ToPrettyString(args.User):player} dug a cavern shaft with {ToPrettyString(ent):tool} at {args.Tile} on {ToPrettyString(gridUid)}");
    }

    /// <summary>
    /// Whether a shovel may dig a shaft through a ground tile: the cavern is there below, and the tile is natural ground
    /// (or what digging it left) that the shovel can't dig further, not a mouth's lip, under no hull and clear.
    /// </summary>
    public bool CanDigShaft(Entity<WFCavernGroundComponent, MapGridComponent> ground, Vector2i index, out WFCavernMouthSpec spec)
    {
        spec = default!;

        if (TerminatingOrDeleted(ground.Comp1.Cavern)
            || !_proto.TryIndex(ground.Comp1.Prototype, out var cavern)
            || !TryComp<BiomeComponent>(ground, out var biome)
            || !_map.TryGetTileRef(ground, ground.Comp2, index, out var tileRef)
            || tileRef.Tile.IsEmpty)
            return false;

        spec = cavern.Mouths;
        var tile = (ContentTileDefinition) _tileDefs[tileRef.Tile.TypeId];

        if (tile.DeconstructTools.Contains(DiggingQuality)
            || !_biome.TryGetTile(index, biome.Layers, biome.Seed, NoGrid, out var natural)
            || !DugFrom((ContentTileDefinition) _tileDefs[natural.Value.TypeId], tile))
            return false;

        foreach (var mouth in ground.Comp1.Mouths)
        {
            if (mouth.Ring.Contains(index))
                return false;
        }

        var box = new Box2(index, index + Vector2i.One).Enlarged(-0.05f);
        var grids = new List<Entity<MapGridComponent>>();
        _mapManager.FindGridsIntersecting(Transform(ground).MapID, box, ref grids, approx: true, includeMap: false);
        if (grids.Count > 0)
            return false;

        var anchored = _map.GetAnchoredEntitiesEnumerator(ground, ground.Comp2, index);
        while (anchored.MoveNext(out var uid))
        {
            if (TryComp<PhysicsComponent>(uid, out var body) && body.CanCollide && body.Hard)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Whether acid prying a tile open must leave it: ground over a cavern that no tool can take apart, such as
    /// Aerumna's chromite. Explosions, the RCD, lattice and shafts still open it.
    /// </summary>
    public bool ResistsChemicalPrying(TileRef tile)
    {
        return !tile.Tile.IsEmpty
               && HasComp<WFCavernGroundComponent>(tile.GridUid)
               && ((ContentTileDefinition) _tileDefs[tile.Tile.TypeId]).DeconstructTools.Count == 0;
    }

    /// <summary>Whether a tile is the natural one or what digging it down leaves, such as snow dug to bedrock.</summary>
    public bool DugFrom(ContentTileDefinition natural, ContentTileDefinition tile)
    {
        var step = natural;

        // A tile chain is short; the bound only guards against a loop in the prototypes.
        for (var i = 0; i < 8; i++)
        {
            if (step.ID == tile.ID)
                return true;

            if (!step.DeconstructTools.Contains(DiggingQuality)
                || string.IsNullOrEmpty(step.BaseTurf)
                || !_tileDefs.TryGetDefinition(step.BaseTurf, out var next)
                || next is not ContentTileDefinition nextTile)
                return false;

            step = nextTile;
        }

        return false;
    }
}
