using System;
using Content.Shared.DoAfter;
using Content.Shared.Explosion.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Whitelist;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Shared._WF.Vehicles.Core;

/// <summary>Demolition of a wrecked vehicle.</summary>
public sealed partial class VehicleInteriorSystem
{
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedExplosionSystem _explosion = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private EntityWhitelistSystem _whitelist = default!;

    private void InitializeDemolition()
    {
        SubscribeLocalEvent<VehicleDemolitionComponent, InteractUsingEvent>(OnDemolitionInteractUsing);
        SubscribeLocalEvent<VehicleDemolitionComponent, VehicleDemolitionDoAfterEvent>(OnDemolitionDoAfter);
    }

    private void OnDemolitionInteractUsing(Entity<VehicleDemolitionComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || _net.IsClient)
            return;

        if (!_whitelist.IsValid(ent.Comp.Whitelist, args.Used))
            return;

        if (!IsVehicleFrameDestroyed(ent.Owner))
        {
            _popup.PopupEntity(Loc.GetString("rmc-vehicle-demolition-frame-intact"), args.User, args.User, PopupType.SmallCaution);
            return;
        }

        args.Handled = true;

        if (ent.Comp.Rigging)
        {
            if (IsHoldLive(new VehicleBusyHold(ent.Comp.RiggingUser, ent.Comp.RiggingUntil)))
            {
                _popup.PopupEntity(Loc.GetString("rmc-vehicle-demolition-busy"), args.User, args.User);
                return;
            }

            // The planter was deleted mid do-after, so nothing will ever release the rig.
            ent.Comp.Rigging = false;
        }

        var doAfter = new DoAfterArgs(EntityManager, args.User, ent.Comp.DoAfter, new VehicleDemolitionDoAfterEvent(), ent.Owner, used: args.Used)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
        };

        if (!_doAfter.TryStartDoAfter(doAfter))
            return;

        ent.Comp.Rigging = true;
        ent.Comp.RiggingUser = args.User;
        ent.Comp.RiggingUntil = _timing.CurTime + TimeSpan.FromSeconds(ent.Comp.DoAfter) + HoldGrace;
        _audio.PlayPvs(ent.Comp.PlantSound, ent.Owner);
        _popup.PopupEntity(Loc.GetString("rmc-vehicle-demolition-start"), args.User, args.User);
    }

    private void OnDemolitionDoAfter(Entity<VehicleDemolitionComponent> ent, ref VehicleDemolitionDoAfterEvent args)
    {
        // A hold that went stale and was taken over by someone else is theirs to release.
        if (ent.Comp.RiggingUser == args.User)
            ent.Comp.Rigging = false;

        if (args.Cancelled || args.Handled || _net.IsClient)
            return;

        if (!IsVehicleFrameDestroyed(ent.Owner))
            return;

        args.Handled = true;

        if (ent.Comp.DeleteUsedItem && args.Used is { } used && !TerminatingOrDeleted(used))
            QueueDel(used);

        var now = _timing.CurTime;
        ent.Comp.Armed = true;
        ent.Comp.DetonateAt = now + TimeSpan.FromSeconds(ent.Comp.TimerDelay);
        ent.Comp.NextBeepAt = now;

        _popup.PopupEntity(Loc.GetString("rmc-vehicle-demolition-armed"), args.User, args.User, PopupType.MediumCaution);
        WarnOccupants(ent.Owner);
    }

    /// <summary>Tells everyone inside that a charge is ticking; they stand on the interior map, out of earshot of the vehicle.</summary>
    private void WarnOccupants(EntityUid vehicle)
    {
        if (!TryComp(vehicle, out VehicleInteriorComponent? interior) || interior.MapId == MapId.Nullspace)
            return;

        foreach (var passenger in interior.Passengers)
        {
            if (!TerminatingOrDeleted(passenger) && _transform.GetMapId(passenger) == interior.MapId)
                _popup.PopupEntity(Loc.GetString("wf-vehicle-demolition-warning"), passenger, passenger, PopupType.LargeCaution);
        }
    }

    /// <summary>The countdown beep, played on the interior as well so that those inside can hear it.</summary>
    private void PlayBeep(EntityUid vehicle, VehicleDemolitionComponent demolition)
    {
        _audio.PlayPvs(demolition.BeepSound, vehicle);

        if (TryComp(vehicle, out VehicleInteriorComponent? interior) &&
            interior.Grid.IsValid() &&
            !TerminatingOrDeleted(interior.Grid))
        {
            _audio.PlayPvs(demolition.BeepSound, interior.Grid);
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_net.IsClient)
            return;

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<VehicleDemolitionComponent>();
        while (query.MoveNext(out var uid, out var demolition))
        {
            if (!demolition.Armed)
                continue;

            if (now >= demolition.NextBeepAt)
            {
                PlayBeep(uid, demolition);
                demolition.NextBeepAt = now + TimeSpan.FromSeconds(demolition.BeepInterval);
            }

            if (now >= demolition.DetonateAt)
            {
                demolition.Armed = false;
                DetonateVehicle((uid, demolition));
            }
        }
    }

    private void DetonateVehicle(Entity<VehicleDemolitionComponent> ent)
    {
        var mapCoords = _transform.GetMapCoordinates(ent.Owner);

        if (mapCoords.MapId != MapId.Nullspace)
        {
            EjectAllOccupants(ent.Owner, Transform(ent.Owner).Coordinates);

            var explosion = Spawn(ent.Comp.Explosion, mapCoords);
            _explosion.TriggerExplosive(explosion);
        }

        QueueDel(ent.Owner);
    }
}
