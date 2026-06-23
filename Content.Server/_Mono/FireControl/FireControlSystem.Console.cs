// Copyright Rane (elijahrane@gmail.com) 2025
// All rights reserved. Relicensed under AGPL with permission

using Content.Server._Mono.Ships.Systems;
using Content.Server.Administration.Logs;
using Content.Server.Shuttles.Systems;
using Content.Shared._Mono.FireControl;
using Content.Shared.Database;
using Content.Shared.GameTicking;
using Content.Shared._Mono.Ships.Components;
using Content.Shared.Popups;
using Content.Shared.Power;
using Content.Shared.Shuttles.BUIStates;
using Content.Shared.UserInterface;
using Content.Shared.Weapons.Ranged;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;
using System.Linq;
using System.Numerics;

namespace Content.Server._Mono.FireControl;

public sealed partial class FireControlSystem : EntitySystem
{
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private ShuttleConsoleSystem _shuttleConsoleSystem = default!;
    [Dependency] private TransformSystem _transform = default!;
    [Dependency] private CrewedShuttleSystem _crewedShuttle = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private IAdminLogManager _adminLogger = default!;
    [Dependency] private IMapManager _mapMan = default!;
    [Dependency] private EntityLookupSystem _lookup = default!; // Forge-Change

    private bool _completedCheck = false;

    private readonly HashSet<Entity<FireControlConsoleComponent>> _consoleSet = new(); // Forge-Change: reused buffer for per-grid console lookups.

    private void InitializeConsole()
    {
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnSpawnComplete);

        SubscribeLocalEvent<FireControlConsoleComponent, PowerChangedEvent>(OnPowerChanged);
        SubscribeLocalEvent<FireControlConsoleComponent, ComponentShutdown>(OnComponentShutdown);
        SubscribeLocalEvent<FireControlConsoleComponent, FireControlConsoleRefreshServerMessage>(OnRefreshServer);
        SubscribeLocalEvent<FireControlConsoleComponent, FireControlConsoleFireMessage>(OnFire);
        // Forge-Change-Start: weapon preset save/rename handlers.
        SubscribeLocalEvent<FireControlConsoleComponent, FireControlConsoleSavePresetMessage>(OnSavePreset);
        SubscribeLocalEvent<FireControlConsoleComponent, FireControlConsoleSetPresetNameMessage>(OnSetPresetName);
        // Forge-Change-End
        SubscribeLocalEvent<FireControlConsoleComponent, BoundUIOpenedEvent>(OnUIOpened);
        SubscribeLocalEvent<FireControlConsoleComponent, ActivatableUIOpenAttemptEvent>(OnConsoleUIOpenAttempt);
    }

    // scuffed one-time check of all station control consoles to ensure they're already refreshed
    // given this only happens once, we can assume all refreshed are things like Camelot's gunnery server.
    private void OnSpawnComplete(PlayerSpawnCompleteEvent ev)
    {
        if (_completedCheck)
            return;

        var query = EntityQueryEnumerator<FireControlConsoleComponent>();

        while (query.MoveNext(out var uid, out var console))
        {
            DoRefreshServer(uid, console);
        }

        _completedCheck = true;
    }

    private void OnPowerChanged(EntityUid uid, FireControlConsoleComponent component, PowerChangedEvent args)
    {
        if (args.Powered)
            TryRegisterConsole(uid, component);
        else
            UnregisterConsole(uid, component);
    }

    private void OnComponentShutdown(EntityUid uid, FireControlConsoleComponent component, ComponentShutdown args)
    {
        UnregisterConsole(uid, component);
    }

    private void DoRefreshServer(EntityUid uid, FireControlConsoleComponent component)
    {
        // First, clean up any invalid server references across all grids
        CleanupInvalidServerReferences();

        // Get the console's grid to force server reconnection on it
        var consoleGrid = _xform.GetGrid(uid);
        if (consoleGrid != null)
        {
            // Force all servers on this grid to attempt reconnection
            ForceServerReconnectionOnGrid((EntityUid)consoleGrid);
        }

        // Check if the current connected server is still valid
        if (component.ConnectedServer != null)
        {
            if (!Exists(component.ConnectedServer) || !TryComp<FireControlServerComponent>(component.ConnectedServer, out _))
            {
                // Server no longer exists, clear the connection
                component.ConnectedServer = null;
            }
        }

        // Try to register console if not connected or if connection was cleared
        if (component.ConnectedServer == null)
        {
            TryRegisterConsole(uid, component);
        }

        // Refresh controllables if we have a valid server connection
        if (component.ConnectedServer != null &&
            TryComp<FireControlServerComponent>(component.ConnectedServer, out var server) &&
            server.ConnectedGrid != null)
        {
            RefreshControllables((EntityUid)server.ConnectedGrid);
        }

        // Always update UI to reflect current state
        UpdateUi(uid, component);
    }

    private void OnRefreshServer(EntityUid uid, FireControlConsoleComponent component, FireControlConsoleRefreshServerMessage args)
    {
        DoRefreshServer(uid, component);
    }

    private void OnFire(EntityUid uid, FireControlConsoleComponent component, FireControlConsoleFireMessage args)
    {
        if (component.ConnectedServer == null
            || !TryComp<FireControlServerComponent>(component.ConnectedServer, out var server)
            || !server.Consoles.Contains(uid))
            return;

        var xform = Transform(uid);
        var grid = xform.GridUid;
        if (grid == null)
            return;

        // Forge-Change: enforce server MaxWeapons when firing from console.
        var selected = server.MaxWeapons > 0 && args.Selected.Count > server.MaxWeapons
            ? args.Selected.Take(server.MaxWeapons).ToList()
            : args.Selected;
        FireWeapons((EntityUid)component.ConnectedServer, selected, args.Coordinates, server);
        if ((component.NextLog == null || component.NextLog < _timing.CurTime) && selected.Any())
        {
            var firePos = _transform.ToMapCoordinates(GetCoordinates(args.Coordinates)).Position;
            var ourPos = _transform.GetWorldPosition(grid.Value);
            var grids = new List<Entity<MapGridComponent>>();
            var adjust = new Vector2(component.LogGridLookupRange, component.LogGridLookupRange);
            _mapMan.FindGridsIntersecting(xform.MapID, new Box2(firePos - adjust, firePos + adjust), ref grids, approx: true, includeMap: false);
            grids.RemoveAll(g => g == grid);
            EntityUid? closest = null;
            foreach (var gridUid in grids)
            {
                var newPos = _transform.GetWorldPosition(gridUid);
                if (closest == null || (newPos - firePos).LengthSquared() < (_transform.GetWorldPosition(closest.Value) - firePos).LengthSquared())
                    closest = gridUid;
            }

            _adminLogger.Add(LogType.ShipgunFired, LogImpact.High,
                    $"{ToPrettyString(args.Actor):user} fired weaponry of ship {ToPrettyString(grid):entity} from ({ourPos}) to ({firePos}), closest grid: {ToPrettyString(closest)}");

            component.NextLog = _timing.CurTime + component.LogSpacing;
        }

        UpdateUi(uid, component);

        // Raise an event to track the cursor position even when not firing
        var fireEvent = new FireControlConsoleFireEvent(args.Coordinates, selected);
        RaiseLocalEvent(uid, fireEvent);
    }

    // Forge-Change-Start: persist weapon presets on the console entity (map save via DataField).
    private void OnSavePreset(EntityUid uid, FireControlConsoleComponent component, FireControlConsoleSavePresetMessage args)
    {
        if (!IsValidPresetIndex(args.PresetIndex))
            return;

        EnsureWeaponPresets(component);
        var preset = component.WeaponPresets[args.PresetIndex];
        preset.Name = args.Name.Trim();
        preset.WeaponNames.Clear();
        preset.Weapons = args.Weapons.Select(w => new GunneryWeaponPresetWeaponData
        {
            Name = w.Name,
            WeaponEntity = w.WeaponEntity,
            HasWeaponEntity = w.HasWeaponEntity,
            GridPosition = w.GridPosition,
            HasGridPosition = w.HasGridPosition,
        }).ToList();
        UpdateUi(uid, component);
    }

    private void OnSetPresetName(EntityUid uid, FireControlConsoleComponent component, FireControlConsoleSetPresetNameMessage args)
    {
        if (!IsValidPresetIndex(args.PresetIndex))
            return;

        EnsureWeaponPresets(component);
        component.WeaponPresets[args.PresetIndex].Name = args.Name.Trim();
        UpdateUi(uid, component);
    }

    private static bool IsValidPresetIndex(int index)
    {
        return index is >= 0 and < FireControlConsoleComponent.WeaponPresetCount;
    }

    private static void EnsureWeaponPresets(FireControlConsoleComponent component)
    {
        while (component.WeaponPresets.Count < FireControlConsoleComponent.WeaponPresetCount)
            component.WeaponPresets.Add(new GunneryWeaponPresetData());

        while (component.WeaponPresets.Count > FireControlConsoleComponent.WeaponPresetCount)
            component.WeaponPresets.RemoveAt(component.WeaponPresets.Count - 1);
    }

    private static GunneryWeaponPresetState[] BuildPresetState(FireControlConsoleComponent component)
    {
        EnsureWeaponPresets(component);
        var presets = new GunneryWeaponPresetState[FireControlConsoleComponent.WeaponPresetCount];

        for (var i = 0; i < FireControlConsoleComponent.WeaponPresetCount; i++)
        {
            var preset = component.WeaponPresets[i];
            presets[i] = new GunneryWeaponPresetState(preset.Name, BuildPresetWeaponState(preset));
        }

        return presets;
    }

    private static GunneryWeaponPresetWeaponState[] BuildPresetWeaponState(GunneryWeaponPresetData preset)
    {
        if (preset.Weapons.Count > 0)
        {
            return preset.Weapons.Select(w => new GunneryWeaponPresetWeaponState(
                w.Name,
                w.WeaponEntity,
                w.HasWeaponEntity,
                w.GridPosition,
                w.HasGridPosition)).ToArray();
        }

        return preset.WeaponNames.Select(name => new GunneryWeaponPresetWeaponState(
            name,
            NetEntity.Invalid,
            hasWeaponEntity: false,
            Vector2.Zero,
            hasGridPosition: false)).ToArray();
    }
    // Forge-Change-End

    public void OnUIOpened(EntityUid uid, FireControlConsoleComponent component, BoundUIOpenedEvent args)
    {
        UpdateUi(uid, component);
    }

    private void OnConsoleUIOpenAttempt(
        EntityUid uid,
        FireControlConsoleComponent component,
        ActivatableUIOpenAttemptEvent args)
    {
        var shuttle = _transform.GetParentUid(uid);
        var uiOpen = _crewedShuttle.AnyShuttleConsoleActiveByPlayer(shuttle, args.User);
        var forceOne = HasComp<CrewedShuttleComponent>(shuttle) && !HasComp<AdvancedPilotComponent>(args.User);

        // Crewed shuttles should not allow people to have both gunnery and shuttle consoles open.
        if (uiOpen && forceOne)
        {
            args.Cancel();
            _popup.PopupClient(Loc.GetString("shuttle-console-crewed"), args.User);
        }
    }

    private void UnregisterConsole(EntityUid console, FireControlConsoleComponent? component = null)
    {
        if (!Resolve(console, ref component))
            return;

        if (component.ConnectedServer == null)
            return;

        // Check if server still exists before trying to unregister
        if (Exists(component.ConnectedServer) && TryComp<FireControlServerComponent>(component.ConnectedServer, out var server))
        {
            server.Consoles.Remove(console);
        }

        component.ConnectedServer = null;
        UpdateUi(console, component);
    }

    private bool CanRegister((EntityUid? ServerUid, FireControlServerComponent? ServerComponent) gridServer)
    {
        if (gridServer.ServerComponent == null)
            return false;

        if (gridServer.ServerComponent.EnforceMaxConsoles
            && gridServer.ServerComponent.Consoles.Count >= gridServer.ServerComponent.MaxConsoles)
            return false;

        return true;
    }

    private bool TryRegisterConsole(EntityUid console, FireControlConsoleComponent? consoleComponent = null)
    {
        if (!Resolve(console, ref consoleComponent))
            return false;

        // Clear any existing invalid connection first
        if (consoleComponent.ConnectedServer != null)
        {
            if (!Exists(consoleComponent.ConnectedServer) || !TryComp<FireControlServerComponent>(consoleComponent.ConnectedServer, out _))
            {
                consoleComponent.ConnectedServer = null;
            }
        }

        var gridServer = TryGetGridServer(console);

        if (gridServer.ServerUid == null || gridServer.ServerComponent == null)
            return false;

        var canRegister = CanRegister(gridServer);

        if (canRegister && gridServer.ServerComponent.Consoles.Add(console))
        {
            consoleComponent.ConnectedServer = gridServer.ServerUid;
            UpdateUi(console, consoleComponent);
            return true;
        }

        return false;
    }

    // Forge-Change-Start
    /// <summary>
    /// Pushes refreshed UI state to every fire control console anchored to the given grid.
    /// Uses the entity lookup to enumerate only consoles parented to the grid (O(grid children))
    /// instead of scanning the whole world, and computes the global dock state once per call.
    /// </summary>
    public void RefreshConsolesOnGrid(EntityUid gridUid)
    {
        _consoleSet.Clear();
        _lookup.GetChildEntities(gridUid, _consoleSet);
        if (_consoleSet.Count == 0)
            return;

        var docks = _shuttleConsoleSystem.GetAllDocks();
        foreach (var console in _consoleSet)
        {
            UpdateUi(console.Owner, console.Comp, docks);
        }
    }
    // Forge-Change-End

    private void UpdateUi(EntityUid uid, FireControlConsoleComponent? component = null)
    {
        if (!Resolve(uid, ref component))
            return;

        // Forge-Change: callers that update a single console pay the dock-lookup cost themselves.
        UpdateUi(uid, component, _shuttleConsoleSystem.GetAllDocks());
    }

    // Forge-Change-Start: overload that takes a pre-fetched docks dictionary so batch refreshes
    // (RefreshConsolesOnGrid) compute it once instead of per console.
    private void UpdateUi(EntityUid uid, FireControlConsoleComponent component, Dictionary<NetEntity, List<DockingPortState>> docks)
    {
        NavInterfaceState navState = _shuttleConsoleSystem.GetNavState(uid, docks);

        List<FireControllableEntry> controllables = new();
        if (component.ConnectedServer != null && TryComp<FireControlServerComponent>(component.ConnectedServer, out var server))
        {
            if (!server.Consoles.Contains(uid))
                return;

            foreach (var controllable in server.Controlled)
            {
                var controlled = new FireControllableEntry();
                controlled.NetEntity = EntityManager.GetNetEntity(controllable);
                controlled.Coordinates = GetNetCoordinates(Transform(controllable).Coordinates);
                controlled.Name = MetaData(controllable).EntityName;

                var (ammoCount, hasManualReload) = GetWeaponAmmunitionInfo(controllable);
                controlled.AmmoCount = ammoCount;
                controlled.HasManualReload = hasManualReload;

                controllables.Add(controlled);
            }
        }

        var array = controllables.ToArray();

        // Forge-Change: expose active firing cap and preset state to gunnery UI.
        var maxActiveWeapons = int.MaxValue;
        if (component.ConnectedServer != null && TryComp<FireControlServerComponent>(component.ConnectedServer, out var serverForLimit))
            maxActiveWeapons = serverForLimit.MaxWeapons;

        var state = new FireControlConsoleBoundInterfaceState(
            component.ConnectedServer != null,
            array,
            navState,
            maxActiveWeapons,
            BuildPresetState(component));
        _ui.SetUiState(uid, FireControlConsoleUiKey.Key, state);
    }
    // Forge-Change-End

    /// <summary>
    /// Gets ammo information for a weapon to determine if it has manual reload.
    /// </summary>
    private (int? ammoCount, bool hasManualReload) GetWeaponAmmunitionInfo(EntityUid weaponEntity)
    {
        if (TryComp<BasicEntityAmmoProviderComponent>(weaponEntity, out var basicAmmo))
        {
            var hasRecharge = HasComp<RechargeBasicEntityAmmoComponent>(weaponEntity);

            return (basicAmmo.Count, !hasRecharge);
        }

        if (TryComp<BallisticAmmoProviderComponent>(weaponEntity, out var ballisticAmmo))
        {
            // if we're InfiniteUnspawned consider us to be non-reloading when at 0 ammo
            return (ballisticAmmo.Count, ballisticAmmo.Cycleable && (ballisticAmmo.Count != 0 || !ballisticAmmo.InfiniteUnspawned));
        }

        if (TryComp<MagazineAmmoProviderComponent>(weaponEntity, out var magazineAmmo))
        {
            var magazineEntity = GetMagazineEntity(weaponEntity);
            if (magazineEntity != null)
            {
                if (TryComp<BallisticAmmoProviderComponent>(magazineEntity, out var magazineBallisticAmmo))
                {
                    var hasAmmo = magazineBallisticAmmo.Cycleable
                             && (magazineBallisticAmmo.Count != 0 || !magazineBallisticAmmo.InfiniteUnspawned);
                    return (magazineBallisticAmmo.Count, hasAmmo);
                }

                if (TryComp<BasicEntityAmmoProviderComponent>(magazineEntity, out var magazineBasicAmmo))
                {
                    var hasRecharge = HasComp<RechargeBasicEntityAmmoComponent>(magazineEntity);
                    return (magazineBasicAmmo.Count, !hasRecharge);
                }
            }
        }

        return (null, false);
    }

    /// <summary>
    /// Gets the magazine entity from a weapon's magazine slot.
    /// </summary>
    private EntityUid? GetMagazineEntity(EntityUid weaponEntity)
    {
        if (!_containers.TryGetContainer(weaponEntity, "gun_magazine", out var container) ||
            container is not ContainerSlot slot)
        {
            return null;
        }

        return slot.ContainedEntity;
    }
}
