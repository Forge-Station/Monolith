using System;
using Content.Shared._WF.Vehicles.Base;
using Content.Shared.Input;
using Robust.Shared.Audio.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Input.Binding;
using Robust.Shared.Network;
using Robust.Shared.Serialization;
using Robust.Shared.Timing;

namespace Content.Shared._WF.Vehicles.Core;

/// <summary>Lets the operator of a vehicle sound its horn from the use-item key.</summary>
public sealed partial class VehicleHornSystem : EntitySystem
{
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private IGameTiming _timing = default!;

    public override void Initialize()
    {
        if (_net.IsClient)
        {
            // The key still reaches the hands system: honking never swallows the use-in-hand bind.
            CommandBinds.Builder
                .Bind(ContentKeyFunctions.UseItemInHand, InputCmdHandler.FromDelegate(session =>
                {
                    // The same press is run again on every prediction replay; one honk is one request.
                    if (!_timing.IsFirstTimePredicted)
                        return;

                    if (session?.AttachedEntity is not { } user)
                        return;

                    if (!TryGetOperatedVehicle(user, out var vehicleUid))
                        return;

                    RaiseNetworkEvent(new VehicleHornRequestEvent(GetNetEntity(vehicleUid)));
                }, handle: false))
                .Register<VehicleHornSystem>();
        }

        SubscribeNetworkEvent<VehicleHornRequestEvent>(OnHornRequest);
    }

    public override void Shutdown()
    {
        if (_net.IsClient)
            CommandBinds.Unregister<VehicleHornSystem>();
    }

    private bool TryGetOperatedVehicle(EntityUid user, out EntityUid vehicle)
    {
        vehicle = EntityUid.Invalid;

        if (!TryComp(user, out RMCVehicleOperatorComponent? op) ||
            op.Vehicle is not { } operated ||
            !Exists(operated))
        {
            return false;
        }

        vehicle = operated;
        return true;
    }

    /// <summary>Sounds the vehicle's horn on its cooldown without a player at the wheel, as an NPC driver; server only.</summary>
    public bool TrySoundHorn(EntityUid vehicle)
    {
        if (!_net.IsServer)
            return false;

        if (TryComp(vehicle, out WFVehicleEngineComponent? engine) && !engine.Powered)
            return false;

        if (!TryComp(vehicle, out VehicleSoundComponent? sound) || sound.HornSound == null)
            return false;

        var now = _timing.CurTime;
        if (sound.NextHornSound > now)
            return false;

        sound.NextHornSound = now + TimeSpan.FromSeconds(sound.HornCooldown);
        _audio.PlayPvs(sound.HornSound, vehicle);
        Dirty(vehicle, sound);
        return true;
    }

    private void OnHornRequest(VehicleHornRequestEvent ev, EntitySessionEventArgs args)
    {
        if (!_net.IsServer)
            return;

        if (args.SenderSession.AttachedEntity is not { } user)
            return;

        var vehicle = GetEntity(ev.Vehicle);

        if (!TryComp(vehicle, out RMCVehicleComponent? vehicleComp) || vehicleComp.Operator != user)
            return;

        if (TryComp(vehicle, out WFVehicleEngineComponent? engine) && !engine.Powered)
            return;

        if (!TryComp(vehicle, out VehicleSoundComponent? sound) || sound.HornSound == null)
            return;

        var now = _timing.CurTime;
        if (sound.NextHornSound > now)
            return;

        sound.NextHornSound = now + TimeSpan.FromSeconds(sound.HornCooldown);
        _audio.PlayPvs(sound.HornSound, vehicle);
        Dirty(vehicle, sound);
    }
}

/// <summary>A client's request to sound the horn of the vehicle it drives.</summary>
[Serializable, NetSerializable]
public sealed partial class VehicleHornRequestEvent : EntityEventArgs
{
    public NetEntity Vehicle;

    public VehicleHornRequestEvent()
    {
    }

    public VehicleHornRequestEvent(NetEntity vehicle)
    {
        Vehicle = vehicle;
    }
}
