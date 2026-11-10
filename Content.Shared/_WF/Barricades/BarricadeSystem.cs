using Content.Shared.Damage;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.Item.ItemToggle.Components;
using Content.Shared.Maps;
using Content.Shared.Physics;
using Content.Shared.Popups;
using Content.Shared.Stacks;
using Content.Shared.Timing;
using Content.Shared.Timing.Components;
using Content.Shared.Timing.Systems;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using static Content.Shared.Physics.CollisionGroup;

namespace Content.Shared._WF.Barricades;

/// <summary>Barricade placement rules, sandbag filling and building, and the entrenching tool.</summary>
public sealed partial class BarricadeSystem : EntitySystem
{
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedMapSystem _mapSystem = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedStackSystem _stack = default!;
    [Dependency] private ITileDefinitionManager _tiles = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private TurfSystem _turf = default!;
    [Dependency] private UseDelaySystem _useDelay = default!;

    private EntityQuery<BarricadeComponent> _barricadeQuery;

    public override void Initialize()
    {
        _barricadeQuery = GetEntityQuery<BarricadeComponent>();

        SubscribeLocalEvent<EntrenchingToolComponent, AfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<EntrenchingToolComponent, EntrenchingToolDoAfterEvent>(OnDoAfter);
        SubscribeLocalEvent<EntrenchingToolComponent, ItemToggledEvent>(OnItemToggled);
        SubscribeLocalEvent<EntrenchingToolComponent, SandbagFillDoAfterEvent>(OnSandbagFillDoAfter);
        SubscribeLocalEvent<EntrenchingToolComponent, SandbagDismantleDoAfterEvent>(OnSandbagDismantleDoAfter);

        SubscribeLocalEvent<EmptySandbagComponent, InteractUsingEvent>(OnEmptyInteractUsing);

        SubscribeLocalEvent<FullSandbagComponent, ActivateInWorldEvent>(OnFullActivateInWorld);
        SubscribeLocalEvent<FullSandbagComponent, AfterInteractEvent>(OnFullAfterInteract);
        SubscribeLocalEvent<FullSandbagComponent, SandbagBuildDoAfterEvent>(OnFullBuildDoAfter);
    }

    private void OnAfterInteract(Entity<EntrenchingToolComponent> tool, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach)
            return;

        if (HasComp<BarricadeSandbagComponent>(args.Target))
        {
            DismantleSandbagBarricade(tool, ref args);
            args.Handled = true;
            return;
        }

        if (StartDigging(tool, args.User, args.ClickLocation))
            args.Handled = true;
    }

    private void DismantleSandbagBarricade(Entity<EntrenchingToolComponent> tool, ref AfterInteractEvent args)
    {
        if (TryComp(tool, out ItemToggleComponent? toggle) && !toggle.Activated)
            return;

        _popup.PopupClient(Loc.GetString("cm-entrenching-dismantle"), args.User, args.User);

        var ev = new SandbagDismantleDoAfterEvent(GetNetCoordinates(args.ClickLocation));
        var doAfter = new DoAfterArgs(EntityManager, args.User, tool.Comp.DigDelay, ev, tool, args.Target, tool)
        {
            BreakOnMove = true,
        };
        _doAfter.TryStartDoAfter(doAfter);
    }

    private void OnSandbagDismantleDoAfter(Entity<EntrenchingToolComponent> tool, ref SandbagDismantleDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        args.Handled = true;

        if (_net.IsClient)
            return;

        if (!TryComp(args.Target, out BarricadeSandbagComponent? barricade))
            return;

        var coordinates = GetCoordinates(args.Coordinates);
        var full = Spawn(barricade.Material, coordinates);

        var bagsSalvaged = barricade.MaxMaterial;
        if (bagsSalvaged <= 0 && TryComp(full, out FullSandbagComponent? fullSandbag))
            bagsSalvaged = fullSandbag.StackRequired;
        if (TryComp(args.Target, out DamageableComponent? damageable))
            bagsSalvaged -= Math.Max((int) damageable.TotalDamage / barricade.MaterialLossDamageInterval - 1, 0);

        if (TryComp(args.Target, out BarbedComponent? barbed) && barbed.IsBarbed)
            Spawn(barbed.Spawn, coordinates);

        Del(args.Target);

        if (bagsSalvaged <= 0)
        {
            Del(full);
            return;
        }

        if (TryComp(full, out StackComponent? fullStack))
            _stack.SetCount(full, bagsSalvaged, fullStack);
    }

    private void OnDoAfter(Entity<EntrenchingToolComponent> tool, ref EntrenchingToolDoAfterEvent args)
    {
        if (args.Handled)
            return;

        if (args.Cancelled)
        {
            _popup.PopupClient(Loc.GetString("cm-entrenching-stop-digging"), args.User, args.User);
            return;
        }

        var coordinates = GetCoordinates(args.Coordinates);
        if (!CanDig(tool, args.User, coordinates, false, out _, out _))
            return;

        args.Handled = true;
        tool.Comp.TotalLayers = tool.Comp.LayersPerDig;
        Dirty(tool);

        var userCoordinates = _transform.GetMoverCoordinates(args.User);
        foreach (var empty in _lookup.GetEntitiesInRange<EmptySandbagComponent>(userCoordinates, 1.5f))
        {
            StartFill(tool, empty, args.User);
            break;
        }
    }

    private void OnItemToggled(Entity<EntrenchingToolComponent> tool, ref ItemToggledEvent args)
    {
        tool.Comp.TotalLayers = 0;
        Dirty(tool);
    }

    private void OnSandbagFillDoAfter(Entity<EntrenchingToolComponent> tool, ref SandbagFillDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        args.Handled = true;
        var userCoordinates = _transform.GetMoverCoordinates(args.User);
        var filled = false;
        foreach (var empty in _lookup.GetEntitiesInRange<EmptySandbagComponent>(userCoordinates, 1.5f))
        {
            if (filled)
            {
                args.Repeat = true;
                break;
            }

            filled = true;
            Fill(tool, empty, args.User, tool.Comp.TotalLayers);
            if (!TerminatingOrDeleted(empty))
            {
                args.Repeat = true;
                break;
            }
        }

        if (tool.Comp.TotalLayers <= 0)
        {
            args.Repeat = false;
            StartDigging(tool, args.User, tool.Comp.LastDigLocation);
        }
    }

    private void OnEmptyInteractUsing(Entity<EmptySandbagComponent> empty, ref InteractUsingEvent args)
    {
        if (args.Handled || !TryComp(args.Used, out EntrenchingToolComponent? toolComp))
            return;

        args.Handled = true;
        StartFill((args.Used, toolComp), empty, args.User);
    }

    private void StartFill(Entity<EntrenchingToolComponent> tool, EntityUid empty, EntityUid user)
    {
        var doAfter = new DoAfterArgs(EntityManager, user, tool.Comp.FillDelay, new SandbagFillDoAfterEvent(), tool, empty, tool)
        {
            BreakOnMove = true,
        };

        if (_doAfter.TryStartDoAfter(doAfter))
            _popup.PopupClient(Loc.GetString("cm-entrenching-begin-filling"), user, user);
    }

    private void OnFullActivateInWorld(Entity<FullSandbagComponent> full, ref ActivateInWorldEvent args)
    {
        if (args.Handled || !TryComp(args.User, out TransformComponent? transform))
            return;

        var coordinates = _transform.GetMoverCoordinates(args.User, transform);
        var direction = transform.LocalRotation.GetCardinalDir();
        if (Build(full, args.User, coordinates, direction, out var handled))
            args.Handled = handled;
    }

    private void OnFullAfterInteract(Entity<FullSandbagComponent> full, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || !TryComp(args.User, out TransformComponent? transform))
            return;

        var direction = transform.LocalRotation.GetCardinalDir();
        if (Build(full, args.User, args.ClickLocation, direction, out var handled))
            args.Handled = handled;
    }

    private void OnFullBuildDoAfter(Entity<FullSandbagComponent> full, ref SandbagBuildDoAfterEvent args)
    {
        if (_net.IsClient || args.Cancelled || args.Handled)
            return;

        var coordinates = GetCoordinates(args.Coordinates);
        if (!_mapSystem.TryFindGridAt(_transform.ToMapCoordinates(coordinates), out var gridId, out var gridComp) ||
            !_interaction.InRangeUnobstructed(full, coordinates, popup: false) ||
            !_turf.TryGetTileRef(coordinates, out var turf) ||
            !CanBuild(full, (gridId, gridComp), args.User, turf.Value, args.Direction))
        {
            return;
        }

        var count = _stack.GetCount(full);
        if (count < full.Comp.StackRequired)
            return;

        if (TryComp(full, out StackComponent? fullStack))
            _stack.SetCount(full, count - full.Comp.StackRequired, fullStack);
        else
            QueueDel(full);

        var tileCenter = _mapSystem.GridTileToLocal(gridId, gridComp, turf.Value.GridIndices);
        var built = SpawnAtPosition(full.Comp.Builds, tileCenter);
        _transform.SetLocalRotation(built, args.Direction.ToAngle());

        args.Handled = true;
    }

    private bool StartDigging(Entity<EntrenchingToolComponent> tool, EntityUid user, EntityCoordinates clicked)
    {
        if (!CanDig(tool, user, clicked, true, out var grid, out var tile))
            return false;

        var coordinates = _mapSystem.GridTileToLocal(grid, grid, tile.GridIndices);
        tool.Comp.LastDigLocation = coordinates;
        Dirty(tool);

        var ev = new EntrenchingToolDoAfterEvent(GetNetCoordinates(coordinates));
        var doAfter = new DoAfterArgs(EntityManager, user, tool.Comp.DigDelay, ev, tool, used: tool)
        {
            BreakOnMove = true,
            NeedHand = true,
            BreakOnHandChange = true,
        };

        if (!_doAfter.TryStartDoAfter(doAfter))
            return false;

        _popup.PopupClient(Loc.GetString("cm-entrenching-start-digging"), user, user);
        _audio.PlayPredicted(tool.Comp.DigSound, user, user);

        if (TryComp(tool, out UseDelayComponent? useDelay))
            _useDelay.TryResetDelay((tool.Owner, useDelay));

        return true;
    }

    private void Fill(Entity<EntrenchingToolComponent> tool, Entity<EmptySandbagComponent> empty, EntityUid user, int amount)
    {
        if (amount <= 0 || tool.Comp.TotalLayers < amount)
            return;

        var toRemove = amount;
        var coordinates = _transform.GetMoverCoordinates(empty);

        if (TryComp(empty, out StackComponent? stack))
        {
            var stackCount = _stack.GetCount(empty, stack);
            toRemove = Math.Min(toRemove, stackCount);
            _stack.SetCount(empty, stackCount - toRemove, stack);

            if (_net.IsServer)
            {
                var filled = Spawn(empty.Comp.Filled, coordinates);
                var filledStack = EnsureComp<StackComponent>(filled);
                _stack.SetCount(filled, toRemove, filledStack);
            }
        }
        else if (_net.IsServer)
        {
            Del(empty);
        }

        tool.Comp.TotalLayers -= toRemove;
        Dirty(tool);
        _audio.PlayPredicted(tool.Comp.FillSound, user, user);
    }

    private bool Build(Entity<FullSandbagComponent> full, EntityUid user, EntityCoordinates coordinates, Direction direction, out bool handled)
    {
        handled = false;
        if (!_mapSystem.TryFindGridAt(_transform.ToMapCoordinates(coordinates), out var gridId, out var gridComp) ||
            !_turf.TryGetTileRef(coordinates, out var tile))
        {
            return false;
        }

        handled = true;
        if (!CanBuild(full, (gridId, gridComp), user, tile.Value, direction))
            return false;

        var ev = new SandbagBuildDoAfterEvent(GetNetCoordinates(coordinates), direction);
        var doAfter = new DoAfterArgs(EntityManager, user, full.Comp.BuildDelay, ev, full, full)
        {
            BreakOnMove = true,
        };

        _doAfter.TryStartDoAfter(doAfter);
        return true;
    }

    private bool CanDig(
        Entity<EntrenchingToolComponent> tool,
        EntityUid user,
        EntityCoordinates coordinates,
        bool checkUseDelay,
        out Entity<MapGridComponent> grid,
        out TileRef tileRef)
    {
        grid = default;
        tileRef = default;

        if (checkUseDelay &&
            TryComp(tool, out UseDelayComponent? useDelay) &&
            _useDelay.IsDelayed((tool.Owner, useDelay)))
        {
            return false;
        }

        if (!_interaction.InRangeUnobstructed(user, coordinates, popup: false))
            return false;

        if (TryComp(tool, out ItemToggleComponent? toggle) && !toggle.Activated)
            return false;

        if (!_mapSystem.TryFindGridAt(_transform.ToMapCoordinates(coordinates), out var gridId, out var gridComp))
            return false;

        tileRef = _mapSystem.GetTileRef(gridId, gridComp, coordinates);
        if (!CanDigTile(tool.Comp, tileRef) || !TileSolidAndNotBlocked(tileRef))
            return false;

        grid = (gridId, gridComp);
        return true;
    }

    private bool CanDigTile(EntrenchingToolComponent tool, TileRef tileRef)
    {
        var id = _tiles[tileRef.Tile.TypeId].ID;
        foreach (var prefix in tool.DigTilePrefixes)
        {
            if (id.StartsWith(prefix, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private bool TileSolidAndNotBlocked(TileRef tile)
    {
        return !_turf.IsSpace(tile) &&
               _turf.GetContentTileDefinition(tile).Sturdy &&
               !_turf.IsTileBlocked(tile, Impassable);
    }

    private bool CanBuild(
        Entity<FullSandbagComponent> full,
        Entity<MapGridComponent> grid,
        EntityUid user,
        TileRef tile,
        Direction direction)
    {
        if (_stack.GetCount(full) < full.Comp.StackRequired)
            return false;

        var coordinates = new EntityCoordinates(tile.GridUid, tile.X, tile.Y).Offset(grid.Comp.TileSizeHalfVector);
        var mask = Impassable | InteractImpassable | TableLayer;
        var popup = _net.IsClient;
        if (!_interaction.InRangeUnobstructed(user, coordinates, collisionMask: mask, popup: popup))
            return false;

        if (!TileSolidAndNotBlocked(tile))
            return false;

        if (HasBarricadeFacing(coordinates, direction))
        {
            if (popup)
                _popup.PopupClient(Loc.GetString("rmc-construction-not-barricade-clear"), user, user, PopupType.SmallCaution);

            return false;
        }

        return true;
    }

    /// <summary>Whether an anchored barricade on this tile already faces this way.</summary>
    public bool HasBarricadeFacing(EntityCoordinates coordinates, Direction direction)
    {
        if (_transform.GetGrid(coordinates) is not { } gridId ||
            !TryComp(gridId, out MapGridComponent? grid))
        {
            return false;
        }

        var indices = _mapSystem.TileIndicesFor(gridId, grid, coordinates);
        var anchored = _mapSystem.GetAnchoredEntities(gridId, grid, indices);
        while (anchored.MoveNext(out var uid))
        {
            if (_barricadeQuery.HasComp(uid) &&
                _transform.GetWorldRotation(uid.Value).GetCardinalDir() == direction)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Checks a barricade can be put down here facing this way; sets a popup when it can't.</summary>
    public bool CanPlaceBarricade(EntityCoordinates coordinates, Direction direction, out string? popup, CollisionGroup? collision = null)
    {
        popup = null;
        if (_transform.GetGrid(coordinates) is not { } gridId ||
            !TryComp(gridId, out MapGridComponent? grid) ||
            !_mapSystem.TryGetTileRef(gridId, grid, coordinates, out var tile) ||
            _turf.IsSpace(tile))
        {
            popup = Loc.GetString("rmc-construction-not-proper-surface");
            return false;
        }

        if (HasBarricadeFacing(coordinates, direction))
        {
            popup = Loc.GetString("rmc-construction-not-barricade-clear");
            return false;
        }

        if (collision is { } group && _turf.IsTileBlocked(tile, group))
        {
            popup = Loc.GetString("rmc-construction-not-proper-surface");
            return false;
        }

        return true;
    }
}
