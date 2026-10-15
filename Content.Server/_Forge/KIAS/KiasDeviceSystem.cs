using System.Linq;
using System.Numerics;
using Content.Server.DeviceLinking.Systems;
using Content.Server.Shuttles.Events;
using Content.Shared._Forge.KIAS;
using Content.Shared._Forge.KIAS.Controllers;
using Content.Server._Forge.KIAS.Controllers;
using Content.Shared.DeviceLinking;
using Content.Shared.DeviceLinking.Events;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Power.EntitySystems;
using Robust.Shared.Map.Components;
using Robust.Shared.Containers;

namespace Content.Server._Forge.KIAS;

public sealed class KiasDeviceSystem : EntitySystem
{
    [Dependency] private KiasSystem _kias = default!;
    [Dependency] private KiasProtocolSystem _protocols = default!;
    [Dependency] private DeviceLinkSystem _links = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedPowerReceiverSystem _power = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private SharedContainerSystem _containers = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<KiasDeviceAdapterComponent, SignalReceivedEvent>(OnSignal);
        SubscribeLocalEvent<KiasDeviceComponent, ActivateInWorldEvent>(OnButton, before: new[] { typeof(SignalSwitchSystem) });
        SubscribeLocalEvent<KiasKeySwitchComponent, ActivateInWorldEvent>(OnKey, before: new[] { typeof(SignalSwitchSystem) });
        SubscribeLocalEvent<KiasWirelessComponent, MapInitEvent>(OnWirelessMapInit);
        SubscribeLocalEvent<KiasRotaryComponent, InteractHandEvent>(OnRotary);
        SubscribeLocalEvent<DockEvent>(OnDock);
        SubscribeLocalEvent<UndockEvent>(OnUndock);
    }

    private void OnWirelessMapInit(Entity<KiasWirelessComponent> ent, ref MapInitEvent args)
    {
        if (ent.Comp.TrustedTransmitters.Count > 16) ent.Comp.TrustedTransmitters.RemoveRange(16, ent.Comp.TrustedTransmitters.Count - 16);
    }

    private void OnButton(Entity<KiasDeviceComponent> ent, ref ActivateInWorldEvent args)
    {
        if (HasComp<Content.Server.DeviceLinking.Components.SignalSwitchComponent>(ent)
            && (!_kias.IsOnline(ent) || Transform(ent).GridUid is not { } grid || !_kias.CanConfigure(grid, args.User))) args.Handled = true;
    }

    private void OnDock(DockEvent args) { Docking(args.GridAUid, true); Docking(args.GridBUid, true); }
    private void OnUndock(UndockEvent args) { Docking(args.GridAUid, false); Docking(args.GridBUid, false); }

    private void Docking(EntityUid grid, bool docked)
    {
        if (!TryComp<KiasGridComponent>(grid, out var runtime) || !runtime.Active) return;
        var detected = false;
        foreach (var uid in runtime.Online)
        {
            if (!_kias.IsOnline(uid) || !HasComp<KiasDockingSensorComponent>(uid)) continue;
            detected = true;
            var controllers = EntityManager.System<KiasControllerIoSystem>();
            controllers.Emit(uid, "DockingSensor", "State", KiasGraphValue.Boolean(docked));
            controllers.Emit(uid, "DockingSensor", docked ? "Docked" : "Undocked", KiasGraphValue.Pulse);
            _links.SendSignal(uid, "DockStatus", docked);
            _links.InvokePort(uid, docked ? "KiasDocked" : "KiasUndocked");
        }
        if (detected) _protocols.Trigger(grid, docked ? KiasTrigger.Docked : KiasTrigger.Undocked);
    }

    public List<(string source, string sink)> DefaultLinks(EntityUid source)
    {
        return _links.GetDefaults(_links.GetSourcePorts(source).Where(port => port.DefaultLinks != null).ToList());
    }

    public void LinkDefaults(EntityUid user, EntityUid source, EntityUid sink)
    {
        if (!HasComp<KiasDeviceAdapterComponent>(source))
        {
            _links.LinkDefaults(user, source, sink);
            return;
        }
        _links.SaveLinks(user, source, sink, DefaultLinks(source));
        EntityManager.System<SharedPopupSystem>().PopupCursor(Loc.GetString("signal-linking-verb-success", ("machine", source)), user);
    }

    private void OnSignal(Entity<KiasDeviceAdapterComponent> ent, ref SignalReceivedEvent args)
    {
        if (!_kias.IsOnline(ent) || Transform(ent).GridUid is not { } grid || Comp<KiasGridComponent>(grid).Testing || args.Trigger == ent.Owner) return;
        if (args.Trigger is { } source)
        {
            if (TerminatingOrDeleted(source) || HasComp<KiasDeviceComponent>(source) && !_kias.IsOnline(source)) return;
            if (Transform(source).GridUid != grid)
            {
                if (!TryComp<KiasWirelessComponent>(ent, out var receiver) || !TryComp<KiasWirelessComponent>(source, out var transmitter)
                    || receiver.Channel != transmitter.Channel
                    || !EntityManager.System<KiasAccessSystem>().CanReceiveWireless(grid, source, receiver)) return;
                var a = _transform.GetMapCoordinates(ent);
                var b = _transform.GetMapCoordinates(source);
                var range = Math.Min(receiver.Range, transmitter.Range);
                if (a.MapId != b.MapId || Vector2.DistanceSquared(a.Position, b.Position) > range * range) return;
            }
        }
        if (args.Port == "KiasEmergency" && ent.Comp.Emergency)
        {
            _protocols.Trigger(grid, KiasTrigger.Manual);
            return;
        }
        if (args.Port is not ("On" or "Off" or "Toggle" or "Pressed")) return;
        ent.Comp.State = args.Port switch { "On" => true, "Off" => false, _ => !ent.Comp.State };
        EntityManager.System<KiasControllerIoSystem>().Emit(ent, "DeviceAdapter", "State", KiasGraphValue.Boolean(ent.Comp.State));
        _links.SendSignal(ent, "Status", ent.Comp.State);
        _links.InvokePort(ent, ent.Comp.State ? "On" : "Off");
    }

    private void OnKey(Entity<KiasKeySwitchComponent> ent, ref ActivateInWorldEvent args)
    {
        if (args.Handled || !args.Complex || !_containers.TryGetContainer(ent, "kias-key", out var slot)
            || !slot.ContainedEntities.Any(uid => HasComp<KiasMasterKeyComponent>(uid)) || !_power.IsPowered(ent.Owner)
            || !Transform(ent).Anchored || Transform(ent).GridUid is not { } grid || !_kias.CanConfigure(grid, args.User)
            || !TryComp<KiasGridComponent>(grid, out var runtime) || runtime.Core is not { } core || !TryComp<MapGridComponent>(grid, out var map)) return;
        var topology = new KiasTopology();
        var relays = EntityManager.System<KiasRelaySystem>();
        topology.Rebuild(runtime.Cables.Where(uid => !TerminatingOrDeleted(uid) && !relays.IsCableBlocked(uid))
            .Select(uid => _maps.TileIndicesFor(grid, map, Transform(uid).Coordinates)));
        if (!topology.Connected(_maps.TileIndicesFor(grid, map, Transform(core).Coordinates), _maps.TileIndicesFor(grid, map, Transform(ent).Coordinates))) return;
        args.Handled = true;
        _kias.SetEnabled(core, !Comp<KiasCoreComponent>(core).Enabled);
    }

    private void OnRotary(Entity<KiasRotaryComponent> ent, ref InteractHandEvent args)
    {
        if (args.Handled || !_kias.IsOnline(ent) || Transform(ent).GridUid is not { } grid || !_kias.CanConfigure(grid, args.User)) return;
        args.Handled = true;
        ent.Comp.Position = (ent.Comp.Position + 1) % Math.Clamp(ent.Comp.Positions, 2, 4);
        var signal = ent.Comp.Position < ent.Comp.Signals.Count ? ent.Comp.Signals[ent.Comp.Position] : ent.Comp.Position;
        _links.InvokePort(ent, $"KiasPosition{Math.Clamp(signal, 0, 3)}");
    }
}
