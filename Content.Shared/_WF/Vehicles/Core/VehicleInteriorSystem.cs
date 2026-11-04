using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using Content.Shared._WF.Vehicles.Base;
using Content.Shared._WF.Vehicles.Compat;
using Content.Shared._WF.Vehicles.Hardpoint;
using Content.Shared.Bed.Sleep;
using Content.Shared.Buckle;
using Content.Shared.Buckle.Components;
using Content.Shared.DoAfter;
using Content.Shared.Ghost;
using Content.Shared.Interaction;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Popups;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Vehicles.Core;

/// <summary>Loads a vehicle's interior map, moves people in and out through its doorways and seats the driver.</summary>
public sealed partial class VehicleInteriorSystem : EntitySystem
{
    private static readonly EntProtoId VehicleKey = "RMCVehicleKey";

    /// <summary>How far past the hull an exit may land when the doorways are blocked.</summary>
    private const float FallbackExitReach = 5f;

    /// <summary>How long past its do-after a hold on a doorway or exit is honoured before it is taken as stale.</summary>
    private static readonly TimeSpan HoldGrace = TimeSpan.FromSeconds(1);

    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedEyeSystem _eye = default!;
    [Dependency] private VehicleExitSpotSystem _exitSpots = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private MapLoaderSystem _mapLoader = default!;
    [Dependency] private MetaDataSystem _meta = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private VehiclePullingSystem _rmcTeleporter = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private VehicleLockSystem _vehicleLock = default!;
    [Dependency] private RMCVehicleSystem _vehicles = default!;
    [Dependency] private VehicleViewToggleSystem _viewToggle = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<VehicleEnterComponent, ActivateInWorldEvent>(OnVehicleEnterActivate);
        SubscribeLocalEvent<VehicleEnterComponent, ComponentShutdown>(OnVehicleEnterShutdown);
        SubscribeLocalEvent<VehicleExitComponent, ActivateInWorldEvent>(OnVehicleExitActivate);
        SubscribeLocalEvent<VehicleEnterComponent, VehicleEnterDoAfterEvent>(OnVehicleEnterDoAfter);
        SubscribeLocalEvent<VehicleExitComponent, VehicleExitDoAfterEvent>(OnVehicleExitDoAfter);

        SubscribeLocalEvent<VehicleDriverSeatComponent, StrapAttemptEvent>(OnDriverSeatStrapAttempt);
        SubscribeLocalEvent<VehicleDriverSeatComponent, StrappedEvent>(OnDriverSeatStrapped);
        SubscribeLocalEvent<VehicleDriverSeatComponent, UnstrappedEvent>(OnDriverSeatUnstrapped);
        SubscribeLocalEvent<VehicleDriverRecoveryComponent, MobStateChangedEvent>(OnDriverRecoveryMobState);
        SubscribeLocalEvent<VehicleDriverRecoveryComponent, SleepStateChangedEvent>(OnDriverRecoverySleep);

        SubscribeLocalEvent<RMCVehicleOperatorComponent, OnVehicleEnteredEvent>(OnVehicleOperatorEntered);
        SubscribeLocalEvent<RMCVehicleOperatorComponent, OnVehicleExitedEvent>(OnVehicleOperatorExited);
        SubscribeLocalEvent<VehicleInteriorOccupantComponent, ComponentStartup>(OnOccupantStartup);
        SubscribeLocalEvent<VehicleInteriorOccupantComponent, ComponentRemove>(OnOccupantRemove);
        SubscribeLocalEvent<VehicleInteriorOccupantComponent, MapUidChangedEvent>(OnOccupantMapChanged);
        SubscribeLocalEvent<VehicleInteriorOccupantComponent, MetaFlagRemoveAttemptEvent>(OnOccupantMetaFlagRemoveAttempt);
        SubscribeLocalEvent<HardpointIntegrityComponent, VehicleCanRunEvent>(OnFrameVehicleCanRun);

        InitializeDemolition();
        InitializeCrash();
    }

    private void OnVehicleEnterActivate(Entity<VehicleEnterComponent> ent, ref ActivateInWorldEvent args)
    {
        if (_net.IsClient)
            return;

        if (args.Handled)
            return;

        if (IsEntryBlockedByLock(ent.Owner, args.User))
        {
            _popup.PopupEntity(Loc.GetString("rmc-vehicle-enter-locked"), args.User, args.User, PopupType.SmallCaution);
            args.Handled = true;
            return;
        }

        if (!TryFindEntry(ent, args.User, out var entryIndex))
        {
            _popup.PopupEntity(Loc.GetString("rmc-vehicle-enter-use-doorway"), args.User, args.User);
            return;
        }

        if (!HasComp<GhostComponent>(args.User) &&
            !TryPrepareEnter(ent, args.User, entryIndex, popup: true, out _, out _, out _, out _))
        {
            return;
        }

        if (HasComp<GhostComponent>(args.User))
        {
            args.Handled = TryEnter(ent, args.User, entryIndex);
            return;
        }

        var interior = EnsureComp<VehicleInteriorComponent>(ent.Owner);

        if (interior.EntryLocks.TryGetValue(entryIndex, out var hold))
        {
            if (IsHoldLive(hold))
            {
                _popup.PopupEntity(Loc.GetString("rmc-vehicle-enter-busy"), args.User, args.User);
                return;
            }

            // Whoever held it was deleted mid do-after, so nothing will ever release it.
            interior.EntryLocks.Remove(entryIndex);
        }

        interior.EntryLocks[entryIndex] = new VehicleBusyHold(args.User, _timing.CurTime + TimeSpan.FromSeconds(ent.Comp.EnterDoAfter) + HoldGrace);

        var doAfter = new DoAfterArgs(EntityManager, args.User, ent.Comp.EnterDoAfter, new VehicleEnterDoAfterEvent { EntryIndex = entryIndex }, ent.Owner)
        {
            BreakOnMove = true,
            BreakOnDamage = false,
            NeedHand = false,
        };

        if (!_doAfter.TryStartDoAfter(doAfter))
        {
            interior.EntryLocks.Remove(entryIndex);
            return;
        }

        args.Handled = true;
    }

    private bool TryEnter(Entity<VehicleEnterComponent> ent, EntityUid user, int entryIndex = -1)
    {
        if (!TryPrepareEnter(ent, user, entryIndex, popup: true, out _, out var coords, out var isGhost, out var pulled))
            return false;

        if (!isGhost)
            TrackOccupant(user, ent.Owner);

        if (pulled is { } pulledUid)
            TrackOccupant(pulledUid, ent.Owner);

        var targetMapCoords = _transform.ToMapCoordinates(coords);
        _rmcTeleporter.HandlePulling(user, targetMapCoords);
        FaceInteriorNorth(user);
        return true;
    }

    private bool TryPrepareEnter(
        Entity<VehicleEnterComponent> ent,
        EntityUid user,
        int entryIndex,
        bool popup,
        [NotNullWhen(true)] out VehicleInteriorComponent? interior,
        out EntityCoordinates coords,
        out bool isGhost,
        out EntityUid? pulled)
    {
        interior = null;
        coords = default;
        isGhost = HasComp<GhostComponent>(user);
        pulled = null;

        if (IsEntryBlockedByLock(ent.Owner, user))
        {
            if (popup)
                _popup.PopupEntity(Loc.GetString("rmc-vehicle-enter-locked"), user, user, PopupType.SmallCaution);

            return false;
        }

        if (!EnsureInterior(ent, out interior))
            return false;

        if (!isGhost)
            PruneTrackedOccupants(ent.Owner, interior);

        if (!isGhost && !CanEnterAsPassenger(ent, interior.Passengers))
        {
            if (popup)
                _popup.PopupEntity(Loc.GetString("rmc-vehicle-enter-passenger-full"), user, user);

            return false;
        }

        if (!TryGetInteriorEntryCoordinates(ent, entryIndex, out coords))
            return false;

        if (!isGhost &&
            TryComp(user, out PullerComponent? puller) &&
            puller.Pulling is { } pulling &&
            pulling != ent.Owner &&
            !HasComp<GhostComponent>(pulling))
        {
            pulled = pulling;
        }

        if (pulled == null)
            return true;

        var passengers = interior.Passengers;

        if (!passengers.Contains(user))
        {
            passengers = new HashSet<EntityUid>(passengers)
            {
                user,
            };
        }

        if (!CanEnterAsPassenger(ent, passengers))
        {
            if (popup)
                _popup.PopupEntity(Loc.GetString("rmc-vehicle-enter-pulled-full"), user, user);

            return false;
        }

        return true;
    }

    private bool IsInteriorLoaded(VehicleInteriorComponent interior)
    {
        return interior.MapId != MapId.Nullspace &&
               _map.MapExists(interior.MapId) &&
               interior.Grid.IsValid() &&
               !TerminatingOrDeleted(interior.Grid);
    }

    private bool EnsureInterior(Entity<VehicleEnterComponent> ent, [NotNullWhen(true)] out VehicleInteriorComponent? interior)
    {
        if (TryComp(ent.Owner, out interior))
        {
            if (IsInteriorLoaded(interior))
                return true;

            if (_net.IsClient)
            {
                interior = null;
                return false;
            }

            // The grid was deleted from under the vehicle (cleanup, an admin): drop the husk and load a fresh one.
            if (interior.MapId != MapId.Nullspace || interior.Map.IsValid())
            {
                ReleaseStaleInterior(ent.Owner, interior);
                interior = null;
            }
        }
        else if (_net.IsClient)
        {
            return false;
        }

        interior = EnsureComp<VehicleInteriorComponent>(ent.Owner);

        var deserializeOptions = new DeserializationOptions
        {
            InitializeMaps = true,
        };

        if (!_mapLoader.TryLoadMap(ent.Comp.InteriorPath, out var loadedMap, out var loadedGrids, deserializeOptions))
        {
            Log.Error($"[VehicleEnter] Failed to load interior for {ToPrettyString(ent.Owner)} at {ent.Comp.InteriorPath}");
            interior = null;
            return false;
        }

        var mapId = loadedMap.Value.Comp.MapId;
        var mapUid = loadedMap.Value.Owner;

        if (loadedGrids.Count == 0)
        {
            Log.Error($"[VehicleEnter] Interior of {ToPrettyString(ent.Owner)} at {ent.Comp.InteriorPath} has no grid.");
            _map.DeleteMap(mapId);
            interior = null;
            return false;
        }

        var interiorGrid = loadedGrids.First().Owner;
        var entryParent = interiorGrid;
        var entryCoords = new EntityCoordinates(entryParent, Vector2.Zero);

        var foundExit = false;
        var exitQuery = EntityQueryEnumerator<VehicleExitComponent, TransformComponent>();
        while (exitQuery.MoveNext(out _, out _, out var xform))
        {
            if (xform.MapID != mapId)
                continue;

            entryCoords = xform.Coordinates;
            entryParent = xform.ParentUid.IsValid() ? xform.ParentUid : entryParent;
            foundExit = true;
            break;
        }

        if (!foundExit)
            Log.Warning($"[VehicleEnter] No VehicleExitComponent found in interior for {ToPrettyString(ent.Owner)} at {ent.Comp.InteriorPath}, using grid origin as fallback entry coordinates.");

        interior.Map = mapUid;
        interior.MapId = mapId;
        interior.Entry = entryCoords;
        interior.EntryParent = entryParent;
        interior.Grid = interiorGrid;
        interior.Passengers.Clear();

        var link = EnsureComp<VehicleInteriorLinkComponent>(mapUid);
        link.Vehicle = ent.Owner;
        Dirty(mapUid, link);

        SpawnVehicleInteriorKey(ent.Owner, mapId);

        var loaded = new VehicleInteriorLoadedEvent(ent.Owner, mapUid, interiorGrid);
        RaiseLocalEvent(ent.Owner, ref loaded, broadcast: true);

        return true;
    }

    private bool TryGetInteriorEntryCoordinates(
        Entity<VehicleEnterComponent> ent,
        int entryIndex,
        out EntityCoordinates targetCoords)
    {
        targetCoords = default;

        if (!EnsureInterior(ent, out var interior))
            return false;

        targetCoords = interior.Entry;

        if (entryIndex < 0 || entryIndex >= ent.Comp.EntryPoints.Count)
            return true;

        var entryPoint = ent.Comp.EntryPoints[entryIndex];
        if (entryPoint.InteriorCoords is not { } interiorCoord)
            return true;

        var parent = interior.Grid.IsValid() ? interior.Grid : interior.EntryParent;
        targetCoords = new EntityCoordinates(parent, interiorCoord);
        return true;
    }

    private void OnVehicleEnterShutdown(Entity<VehicleEnterComponent> ent, ref ComponentShutdown args)
    {
        CleanupInterior(ent.Owner);
    }

    /// <summary>Puts whoever is still on a dead interior's map back beside the vehicle and tears the interior down.</summary>
    private void ReleaseStaleInterior(EntityUid vehicle, VehicleInteriorComponent interior)
    {
        var outside = _transform.GetMapCoordinates(vehicle);
        if (outside.MapId != MapId.Nullspace)
        {
            foreach (var passenger in new List<EntityUid>(interior.Passengers))
            {
                if (TerminatingOrDeleted(passenger) || _transform.GetMapId(passenger) != interior.MapId)
                    continue;

                _rmcTeleporter.HandlePulling(passenger, outside);
            }
        }

        CleanupInterior(vehicle);
    }

    private void CleanupInterior(EntityUid vehicle)
    {
        if (!TryComp(vehicle, out VehicleInteriorComponent? interior))
            return;

        foreach (var passenger in new List<EntityUid>(interior.Passengers))
        {
            if (TryComp(passenger, out VehicleInteriorOccupantComponent? occupant) &&
                occupant.Vehicle == vehicle)
            {
                RemComp<VehicleInteriorOccupantComponent>(passenger);
            }
        }

        if (interior.Map.IsValid() &&
            Exists(interior.Map) &&
            TryComp(interior.Map, out VehicleInteriorLinkComponent? link) &&
            link.Vehicle == vehicle)
        {
            RemComp<VehicleInteriorLinkComponent>(interior.Map);
        }

        RemComp<VehicleInteriorComponent>(vehicle);

        if (_net.IsClient)
            return;

        if (interior.MapId != MapId.Nullspace && _map.MapExists(interior.MapId))
        {
            _map.DeleteMap(interior.MapId);
        }
        else if (interior.Map.IsValid() && Exists(interior.Map))
        {
            Del(interior.Map);
        }
    }

    private void OnVehicleExitActivate(Entity<VehicleExitComponent> ent, ref ActivateInWorldEvent args)
    {
        if (_net.IsClient)
            return;

        if (args.Handled)
            return;

        if (ent.Comp.PendingExit)
        {
            if (IsHoldLive(new VehicleBusyHold(ent.Comp.PendingExitUser, ent.Comp.PendingExitUntil)))
            {
                _popup.PopupEntity(Loc.GetString("rmc-vehicle-exit-busy"), args.User, args.User);
                return;
            }

            // Whoever held the exit was deleted mid do-after, so nothing will ever release it.
            ent.Comp.PendingExit = false;
        }

        if (!TryComp(ent, out TransformComponent? exitXform) || exitXform.MapID == MapId.Nullspace)
            return;

        if (!TryGetVehicleFromInterior(ent.Owner, out var vehicle) || vehicle is not { } vehicleUid)
            return;

        if (!TryComp(vehicleUid, out VehicleEnterComponent? enter))
            return;

        if (IsEntryBlockedByLock(vehicleUid, args.User))
        {
            _popup.PopupEntity(Loc.GetString("rmc-vehicle-enter-locked"), args.User, args.User, PopupType.SmallCaution);
            args.Handled = true;
            return;
        }

        if (HasComp<GhostComponent>(args.User))
        {
            args.Handled = TryExit(ent, args.User);
            return;
        }

        ent.Comp.PendingExit = true;
        ent.Comp.PendingExitUser = args.User;
        ent.Comp.PendingExitUntil = _timing.CurTime + TimeSpan.FromSeconds(enter.ExitDoAfter) + HoldGrace;

        var doAfter = new DoAfterArgs(EntityManager, args.User, enter.ExitDoAfter, new VehicleExitDoAfterEvent(), ent.Owner)
        {
            BreakOnMove = true,
        };

        if (!_doAfter.TryStartDoAfter(doAfter))
        {
            ent.Comp.PendingExit = false;
            return;
        }

        args.Handled = true;
    }

    private bool TryFindEntry(Entity<VehicleEnterComponent> ent, EntityUid user, out int entryIndex)
    {
        entryIndex = -1;

        if (ent.Comp.EntryPoints.Count == 0)
            return true;

        var bypassEntry = HasComp<GhostComponent>(user) || IsFrameDestroyed(ent.Owner);

        var vehicleXform = Transform(ent.Owner);
        var userXform = Transform(user);

        if (vehicleXform.MapID != userXform.MapID || vehicleXform.MapID == MapId.Nullspace)
            return false;

        // The offsets are in the vehicle's own frame; the world matrix keeps that right on a turning ship deck.
        var userPos = _transform.GetWorldPosition(userXform);
        var localDelta = Vector2.Transform(userPos, _transform.GetInvWorldMatrix(vehicleXform));

        if (bypassEntry)
        {
            var closestDistance = float.MaxValue;
            var closestIndex = -1;

            for (var i = 0; i < ent.Comp.EntryPoints.Count; i++)
            {
                var entry = ent.Comp.EntryPoints[i];
                var distance = (localDelta - entry.Offset).LengthSquared();
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closestIndex = i;
                }
            }

            if (closestIndex >= 0)
            {
                entryIndex = closestIndex;
                return true;
            }

            return false;
        }

        for (var i = 0; i < ent.Comp.EntryPoints.Count; i++)
        {
            var entry = ent.Comp.EntryPoints[i];
            if ((localDelta - entry.Offset).Length() <= entry.Radius)
            {
                entryIndex = i;
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether the hold's user is still around and its time has not run out.</summary>
    private bool IsHoldLive(VehicleBusyHold hold)
    {
        return hold.User.IsValid() && !TerminatingOrDeleted(hold.User) && _timing.CurTime < hold.Until;
    }

    /// <summary>Whether <paramref name="user"/> still stands at doorway <paramref name="entryIndex"/>, in the vehicle's own frame.</summary>
    private bool IsAtEntry(Entity<VehicleEnterComponent> ent, EntityUid user, int entryIndex)
    {
        if (ent.Comp.EntryPoints.Count == 0)
            return true;

        if (entryIndex < 0 || entryIndex >= ent.Comp.EntryPoints.Count)
            return false;

        var vehicleXform = Transform(ent.Owner);
        var userXform = Transform(user);

        if (vehicleXform.MapID != userXform.MapID || vehicleXform.MapID == MapId.Nullspace)
            return false;

        if (HasComp<GhostComponent>(user) || IsFrameDestroyed(ent.Owner))
            return true;

        var userPos = _transform.GetWorldPosition(userXform);
        var localDelta = Vector2.Transform(userPos, _transform.GetInvWorldMatrix(vehicleXform));
        var entry = ent.Comp.EntryPoints[entryIndex];
        return (localDelta - entry.Offset).Length() <= entry.Radius;
    }

    private void OnVehicleEnterDoAfter(Entity<VehicleEnterComponent> ent, ref VehicleEnterDoAfterEvent args)
    {
        if (TryComp(ent.Owner, out VehicleInteriorComponent? interior) &&
            interior.EntryLocks.TryGetValue(args.EntryIndex, out var hold) &&
            hold.User == args.User)
        {
            interior.EntryLocks.Remove(args.EntryIndex);
        }

        if (args.Cancelled || args.Handled)
            return;

        // The do-after is not aimed at the vehicle, so it cannot tell that the vehicle drove off during the wait.
        if (!IsAtEntry(ent, args.User, args.EntryIndex))
            return;

        args.Handled = TryEnter(ent, args.User, args.EntryIndex);
    }

    private bool TryExit(Entity<VehicleExitComponent> ent, EntityUid user)
    {
        if (!TryGetVehicleFromInterior(ent.Owner, out var vehicle) || vehicle is not { } vehicleUid)
            return false;

        if (!TryComp(vehicleUid, out VehicleEnterComponent? enter))
            return false;

        if (IsEntryBlockedByLock(vehicleUid, user))
        {
            _popup.PopupEntity(Loc.GetString("rmc-vehicle-enter-locked"), user, user, PopupType.SmallCaution);
            return false;
        }

        if (!TryGetExitCoordinates(ent, enter, vehicleUid, out _, out var exitMapCoords))
            return false;

        // Ghosts go where the marker says; everyone else needs somewhere to stand.
        if (!HasComp<GhostComponent>(user) &&
            !TryFindExitSpot(ent, enter, vehicleUid, user, out exitMapCoords))
        {
            _popup.PopupEntity(Loc.GetString("rmc-vehicle-exit-blocked"), user, user, PopupType.SmallCaution);
            return false;
        }

        _rmcTeleporter.HandlePulling(user, exitMapCoords);
        UntrackOccupant(user, vehicleUid);
        return true;
    }

    /// <summary>Where someone leaving by <paramref name="exit"/> lands: the marker's doorway, another doorway, then free floor reachable from the hull; none when walled in.</summary>
    private bool TryFindExitSpot(
        Entity<VehicleExitComponent> exit,
        VehicleEnterComponent enter,
        EntityUid vehicle,
        EntityUid user,
        out MapCoordinates spot)
    {
        spot = default;

        var intended = exit.Comp.EntryIndex;
        var offsets = new List<Vector2>(enter.EntryPoints.Count + 1)
        {
            intended >= 0 && intended < enter.EntryPoints.Count ? enter.EntryPoints[intended].Offset : enter.ExitOffset,
        };

        for (var i = 0; i < enter.EntryPoints.Count; i++)
        {
            if (i != intended)
                offsets.Add(enter.EntryPoints[i].Offset);
        }

        var preferred = _transform.GetWorldPosition(vehicle);
        MapCoordinates? openSpace = null;

        for (var i = 0; i < offsets.Count; i++)
        {
            if (!TryGetExitCoordinatesForOffset(vehicle, offsets[i], out _, out var mapCoords))
                continue;

            if (i == 0)
                preferred = mapCoords.Position;

            switch (_exitSpots.Classify(mapCoords, vehicle, user))
            {
                case VehicleExitSpotKind.OwnFloor:
                    spot = mapCoords;
                    return true;
                case VehicleExitSpotKind.NoFloor:
                    openSpace ??= mapCoords;
                    break;
            }
        }

        if (_exitSpots.TryFindConnectedSpot(vehicle, user, preferred, FallbackExitReach, out spot))
            return true;

        if (openSpace is not { } space)
            return false;

        spot = space;
        return true;
    }

    private bool TryGetExitCoordinates(
        Entity<VehicleExitComponent> ent,
        VehicleEnterComponent enter,
        EntityUid vehicleUid,
        out EntityCoordinates exitCoords,
        out MapCoordinates exitMapCoords)
    {
        Vector2 offset;
        var entryIndex = ent.Comp.EntryIndex;
        if (entryIndex >= 0 && entryIndex < enter.EntryPoints.Count)
            offset = enter.EntryPoints[entryIndex].Offset;
        else
            offset = enter.ExitOffset;

        return TryGetExitCoordinatesForOffset(vehicleUid, offset, out exitCoords, out exitMapCoords);
    }

    private bool TryGetExitCoordinatesForOffset(
        EntityUid vehicleUid,
        Vector2 offset,
        out EntityCoordinates exitCoords,
        out MapCoordinates exitMapCoords)
    {
        exitCoords = default;
        exitMapCoords = default;

        var vehicleXform = Transform(vehicleUid);

        EntityUid? parent = vehicleXform.ParentUid;
        if (parent == null || !parent.Value.IsValid())
            parent = vehicleXform.MapUid;
        if (parent == null || !parent.Value.IsValid())
            return false;

        // In the parent's frame, so a vehicle riding a ship deck exits onto the deck wherever the ship now is.
        var rotated = vehicleXform.LocalRotation.RotateVec(offset);
        var position = vehicleXform.LocalPosition + rotated;

        exitCoords = new EntityCoordinates(parent.Value, position);
        exitMapCoords = _transform.ToMapCoordinates(exitCoords);
        return exitMapCoords.MapId != MapId.Nullspace;
    }

    private void OnVehicleExitDoAfter(Entity<VehicleExitComponent> ent, ref VehicleExitDoAfterEvent args)
    {
        // A hold that went stale and was taken over by someone else is theirs to release.
        if (ent.Comp.PendingExitUser == args.User)
            ent.Comp.PendingExit = false;

        if (args.Cancelled || args.Handled)
            return;

        args.Handled = TryExit(ent, args.User);
    }

    private void OnOccupantStartup(Entity<VehicleInteriorOccupantComponent> ent, ref ComponentStartup args)
    {
        _meta.AddFlag(ent, MetaDataFlags.ExtraTransformEvents);
    }

    private void OnOccupantRemove(Entity<VehicleInteriorOccupantComponent> ent, ref ComponentRemove args)
    {
        _meta.RemoveFlag(ent, MetaDataFlags.ExtraTransformEvents);

        if (ent.Comp.Vehicle.IsValid())
            UnregisterTrackedOccupant(ent.Comp.Vehicle, ent.Owner);
    }

    private void OnOccupantMapChanged(Entity<VehicleInteriorOccupantComponent> ent, ref MapUidChangedEvent args)
    {
        // The interior's state is server-only, so only the server can tell where the occupant belongs.
        if (_net.IsClient || ent.Comp.Vehicle == EntityUid.Invalid)
            return;

        if (TryComp(ent.Comp.Vehicle, out VehicleInteriorComponent? interior) &&
            args.NewMapId == interior.MapId)
        {
            RegisterTrackedOccupant(ent.Comp.Vehicle, ent.Owner, interior);
            return;
        }

        RemCompDeferred<VehicleInteriorOccupantComponent>(ent.Owner);
    }

    /// <summary>Turns the view of someone who has just come inside round to the interior's north, over the usual camera turn.</summary>
    private void FaceInteriorNorth(EntityUid user)
    {
        if (!TryComp(user, out InputMoverComponent? mover))
            return;

        var xform = Transform(user);
        var relative = xform.GridUid ?? xform.MapUid;
        var before = TryComp(mover.RelativeEntity, out TransformComponent? oldXform) ? _transform.GetWorldRotation(oldXform) : Angle.Zero;
        var after = relative is { } now ? _transform.GetWorldRotation(now) : Angle.Zero;

        // The view starts where it was outside and turns from there.
        mover.RelativeRotation -= after - before;
        mover.RelativeEntity = relative;
        mover.TargetRelativeRotation = Angle.Zero;
        mover.LerpTarget = TimeSpan.Zero;
        Dirty(user, mover);
    }

    private void OnOccupantMetaFlagRemoveAttempt(Entity<VehicleInteriorOccupantComponent> ent, ref MetaFlagRemoveAttemptEvent args)
    {
        if ((args.ToRemove & MetaDataFlags.ExtraTransformEvents) != 0 &&
            ent.Comp.LifeStage <= ComponentLifeStage.Running)
        {
            args.ToRemove &= ~MetaDataFlags.ExtraTransformEvents;
        }
    }

    private void TrackOccupant(EntityUid user, EntityUid vehicle)
    {
        var occupant = EnsureComp<VehicleInteriorOccupantComponent>(user);
        if (occupant.Vehicle.IsValid() &&
            occupant.Vehicle != vehicle)
        {
            UnregisterTrackedOccupant(occupant.Vehicle, user);
        }

        occupant.Vehicle = vehicle;
        Dirty(user, occupant);
        RegisterTrackedOccupant(vehicle, user);
    }

    private void UntrackOccupant(EntityUid user, EntityUid vehicle)
    {
        if (!TryComp(user, out VehicleInteriorOccupantComponent? occupant) ||
            occupant.Vehicle != vehicle)
        {
            UnregisterTrackedOccupant(vehicle, user);
            return;
        }

        RemComp<VehicleInteriorOccupantComponent>(user);
    }

    private void RegisterTrackedOccupant(
        EntityUid vehicle,
        EntityUid user,
        VehicleInteriorComponent? interior = null)
    {
        if (!Resolve(vehicle, ref interior, logMissing: false))
            return;

        interior.Passengers.Add(user);
    }

    private void UnregisterTrackedOccupant(EntityUid vehicle, EntityUid user)
    {
        if (!TryComp(vehicle, out VehicleInteriorComponent? interior))
            return;

        interior.Passengers.Remove(user);
    }

    private void PruneTrackedOccupants(EntityUid vehicle, VehicleInteriorComponent interior)
    {
        foreach (var passenger in new List<EntityUid>(interior.Passengers))
        {
            if (TryComp(passenger, out VehicleInteriorOccupantComponent? occupant) &&
                occupant.Vehicle == vehicle &&
                _transform.GetMapId(passenger) == interior.MapId)
            {
                continue;
            }

            interior.Passengers.Remove(passenger);
        }
    }

    private int CountLivingOccupants(HashSet<EntityUid> occupants)
    {
        var count = 0;
        foreach (var occupant in occupants)
        {
            // Crates and other pulled things come along but take no seat.
            if (HasComp<MobStateComponent>(occupant) && !_mobState.IsDead(occupant))
                count++;
        }

        return count;
    }

    private bool CanEnterAsPassenger(Entity<VehicleEnterComponent> ent, HashSet<EntityUid> passengers)
    {
        return CountLivingOccupants(passengers) < ent.Comp.MaxPassengers;
    }

    private void SpawnVehicleInteriorKey(EntityUid vehicle, MapId mapId)
    {
        var keyId = _vehicleLock.EnsureVehicleKeyId(vehicle);
        var seatQuery = EntityQueryEnumerator<VehicleDriverSeatComponent, TransformComponent>();
        while (seatQuery.MoveNext(out var seatUid, out _, out var seatXform))
        {
            if (seatXform.MapID != mapId)
                continue;

            var key = Spawn(VehicleKey, seatXform.Coordinates);
            if (TryComp(key, out VehicleKeyComponent? keyComp))
                _vehicleLock.BindKey((key, keyComp), keyId, vehicle);

            return;
        }
    }

    private void OnDriverSeatStrapAttempt(Entity<VehicleDriverSeatComponent> ent, ref StrapAttemptEvent args)
    {
        if (args.Cancelled)
            return;

        var attempt = new VehicleSeatAttemptEvent(args.Buckle.Owner, args.User, args.Popup);
        RaiseLocalEvent(ent.Owner, ref attempt);

        if (attempt.Cancelled)
            args.Cancelled = true;
    }

    private void OnDriverSeatStrapped(Entity<VehicleDriverSeatComponent> ent, ref StrappedEvent args)
    {
        if (_net.IsClient)
            return;

        if (!TryGetVehicleFromInterior(ent.Owner, out var vehicle) ||
            !TryComp(vehicle, out RMCVehicleComponent? vehicleComp))
        {
            return;
        }

        // Someone who cannot drive (out cold) is strapped in but neither operator nor keyholder, until they can act;
        // success adds the operator component.
        if (!_vehicles.TrySetOperator((vehicle.Value, vehicleComp), args.Buckle.Owner))
        {
            EnsureComp<VehicleDriverRecoveryComponent>(args.Buckle.Owner);
            return;
        }

        _vehicleLock.EnableLockAction(args.Buckle.Owner, vehicle.Value);
    }

    private void OnDriverRecoveryMobState(Entity<VehicleDriverRecoveryComponent> ent, ref MobStateChangedEvent args)
    {
        TryTakeTheWheel(ent);
    }

    private void OnDriverRecoverySleep(Entity<VehicleDriverRecoveryComponent> ent, ref SleepStateChangedEvent args)
    {
        if (!args.FellAsleep)
            TryTakeTheWheel(ent);
    }

    /// <summary>Makes someone strapped into a driver seat the operator, with the lock action, now that they may be able to act.</summary>
    private void TryTakeTheWheel(Entity<VehicleDriverRecoveryComponent> ent)
    {
        if (_net.IsClient)
            return;

        if (!TryComp(ent, out BuckleComponent? buckle) ||
            buckle.BuckledTo is not { } seat ||
            !HasComp<VehicleDriverSeatComponent>(seat) ||
            !TryGetVehicleFromInterior(seat, out var vehicle) ||
            !TryComp(vehicle, out RMCVehicleComponent? vehicleComp) ||
            vehicleComp.Operator != null && vehicleComp.Operator != ent.Owner)
        {
            RemCompDeferred<VehicleDriverRecoveryComponent>(ent);
            return;
        }

        // Still down or asleep: they stay on the list for the next time they get up.
        if (!_vehicles.TrySetOperator((vehicle.Value, vehicleComp), ent.Owner, removeExisting: false))
            return;

        _vehicleLock.EnableLockAction(ent.Owner, vehicle.Value);
        RemCompDeferred<VehicleDriverRecoveryComponent>(ent);
    }

    private void OnDriverSeatUnstrapped(Entity<VehicleDriverSeatComponent> ent, ref UnstrappedEvent args)
    {
        if (_net.IsClient)
            return;

        RemCompDeferred<VehicleDriverRecoveryComponent>(args.Buckle.Owner);

        if (!TryGetVehicleFromInterior(ent.Owner, out var vehicle) ||
            !TryComp(vehicle, out RMCVehicleComponent? vehicleComp))
        {
            return;
        }

        _vehicleLock.DisableLockAction(args.Buckle.Owner, vehicle.Value);

        if (vehicleComp.Operator != args.Buckle.Owner)
            return;

        _vehicles.TryRemoveOperator((vehicle.Value, vehicleComp));

        if (!IsOperatingOtherVehicle(args.Buckle.Owner))
        {
            RemCompDeferred<RMCVehicleOperatorComponent>(args.Buckle.Owner);
        }
    }

    private void OnVehicleOperatorEntered(Entity<RMCVehicleOperatorComponent> ent, ref OnVehicleEnteredEvent args)
    {
        if (_net.IsClient)
            return;

        if (!HasComp<VehicleEnterComponent>(args.Vehicle.Owner))
            return;

        _eye.SetTarget(ent.Owner, args.Vehicle.Owner);
        _viewToggle.EnableViewToggle(ent.Owner, args.Vehicle.Owner, args.Vehicle.Owner, insideTarget: null, isOutside: true);
    }

    private void OnVehicleOperatorExited(Entity<RMCVehicleOperatorComponent> ent, ref OnVehicleExitedEvent args)
    {
        if (_net.IsClient)
            return;

        if (!TryComp(ent, out EyeComponent? eye))
            return;

        _viewToggle.DisableViewToggle(ent.Owner, args.Vehicle.Owner);

        if (eye.Target != args.Vehicle.Owner)
            return;

        _eye.SetTarget(ent.Owner, null, eye);
    }

    private bool IsOperatingOtherVehicle(EntityUid entity)
    {
        if (!TryComp<BuckleComponent>(entity, out var buckle))
            return false;

        if (buckle.BuckledTo == null)
            return false;

        return HasComp<VehicleDriverSeatComponent>(buckle.BuckledTo);
    }

    private void OnFrameVehicleCanRun(Entity<HardpointIntegrityComponent> ent, ref VehicleCanRunEvent args)
    {
        if (!args.CanRun || ent.Comp.Integrity > 0f)
            return;

        args.CanRun = false;
    }

    private bool IsEntryBlockedByLock(EntityUid vehicle, EntityUid user)
    {
        if (HasComp<GhostComponent>(user))
            return false;

        return TryComp(vehicle, out VehicleLockComponent? vehicleLock) && vehicleLock.Locked;
    }

    private bool IsFrameDestroyed(EntityUid vehicle)
    {
        if (!TryComp(vehicle, out HardpointIntegrityComponent? frameIntegrity))
            return false;

        return frameIntegrity.BypassEntryOnZero && frameIntegrity.Integrity <= 0f;
    }

    /// <summary>Whether the vehicle's frame is at zero integrity.</summary>
    public bool IsVehicleFrameDestroyed(EntityUid vehicle)
    {
        return TryComp(vehicle, out HardpointIntegrityComponent? integrity) && integrity.Integrity <= 0f;
    }

    /// <summary>The vehicle whose interior map <paramref name="interiorEntity"/> is on.</summary>
    public bool TryGetVehicleFromInterior(EntityUid interiorEntity, out EntityUid? vehicle)
    {
        vehicle = null;
        var mapId = _transform.GetMapId(interiorEntity);
        if (mapId == MapId.Nullspace || !_map.MapExists(mapId))
            return false;

        var mapUid = _map.GetMapOrInvalid(mapId);
        if (!TryComp(mapUid, out VehicleInteriorLinkComponent? link) ||
            Deleted(link.Vehicle))
        {
            return false;
        }

        vehicle = link.Vehicle;
        return true;
    }

    /// <summary>The doorway of <paramref name="vehicle"/> that <paramref name="user"/> is standing at.</summary>
    public bool TryFindEntryPoint(EntityUid vehicle, EntityUid user, out int entryIndex)
    {
        entryIndex = -1;

        if (!TryComp(vehicle, out VehicleEnterComponent? enter))
            return false;

        return TryFindEntry((vehicle, enter), user, out entryIndex);
    }

    /// <summary>Where someone entering through doorway <paramref name="entryIndex"/> lands; loads the interior if needed.</summary>
    public bool TryGetInteriorEntryCoordinates(EntityUid vehicle, int entryIndex, out EntityCoordinates targetCoords)
    {
        targetCoords = default;

        if (!TryComp(vehicle, out VehicleEnterComponent? enter))
            return false;

        return TryGetInteriorEntryCoordinates((vehicle, enter), entryIndex, out targetCoords);
    }

    /// <summary>Loads the vehicle's interior if it is not loaded yet; server only.</summary>
    public bool TryLoadInterior(Entity<VehicleEnterComponent?> vehicle, [NotNullWhen(true)] out VehicleInteriorComponent? interior)
    {
        interior = null;
        if (!Resolve(vehicle, ref vehicle.Comp, false))
            return false;

        return EnsureInterior(new Entity<VehicleEnterComponent>(vehicle.Owner, vehicle.Comp), out interior);
    }

    /// <summary>Counts someone put straight onto the interior's map, such as a spawned NPC crewman, as inside the vehicle.</summary>
    public void TrackInteriorOccupant(EntityUid vehicle, EntityUid occupant)
    {
        TrackOccupant(occupant, vehicle);
    }

    /// <summary>
    /// Puts an occupant out through the first of the interior's exits that has room, at once and past the lock, as an
    /// NPC crew bailing out. The occupant must be unbuckled first.
    /// </summary>
    public bool TryExitVehicle(EntityUid vehicle, EntityUid occupant)
    {
        if (_net.IsClient ||
            !TryComp(vehicle, out VehicleEnterComponent? enter) ||
            !TryComp(vehicle, out VehicleInteriorComponent? interior) ||
            !IsInteriorLoaded(interior))
        {
            return false;
        }

        var exitQuery = EntityQueryEnumerator<VehicleExitComponent, TransformComponent>();
        while (exitQuery.MoveNext(out var exitUid, out var exit, out var exitXform))
        {
            if (exitXform.MapID != interior.MapId)
                continue;

            if (!TryFindExitSpot((exitUid, exit), enter, vehicle, occupant, out var spot))
                continue;

            _rmcTeleporter.HandlePulling(occupant, spot);
            UntrackOccupant(occupant, vehicle);
            return true;
        }

        return false;
    }
}
