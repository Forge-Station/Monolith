using Content.Server.Ame.Components;
using Content.Server.Ame.EntitySystems;
using Content.Server._Forge.KIAS.Controllers;
using Content.Shared._Forge.KIAS;
using Content.Shared._Forge.KIAS.Controllers;
using Content.Shared.DeviceLinking;
using Content.Shared.DeviceLinking.Events;

namespace Content.Server._Forge.KIAS;

public sealed class KiasAmeAdapterSystem : EntitySystem
{
    [Dependency] private KiasSystem _kias = default!;
    [Dependency] private AmeControllerSystem _ame = default!;
    [Dependency] private KiasControllerIoSystem _controllers = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<AmeControllerComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<AmeControllerComponent, SignalReceivedEvent>(OnSignal);
        _ame.InjectionChanged += OnChanged;
    }

    public override void Shutdown()
    {
        _ame.InjectionChanged -= OnChanged;
        base.Shutdown();
    }

    private void OnStartup(Entity<AmeControllerComponent> ent, ref ComponentStartup args)
    {
        EntityManager.System<SharedDeviceLinkSystem>().EnsureSinkPorts(ent, "On", "Off", "Toggle");
    }

    private void OnSignal(Entity<AmeControllerComponent> ent, ref SignalReceivedEvent args)
    {
        if (args.Trigger is not { } adapter || !HasComp<KiasDeviceAdapterComponent>(adapter)
            || !_kias.IsOnline(adapter) || !Transform(ent).Anchored
            || Transform(ent).GridUid != Transform(adapter).GridUid
            || args.Port is not ("On" or "Off" or "Toggle")) return;
        var previous = ent.Comp.Injecting;
        _ame.SetInjecting(ent, args.Port == "Toggle" ? !previous : args.Port == "On");
        if (previous == ent.Comp.Injecting) OnChanged(ent, ent.Comp.Injecting);
    }

    private void OnChanged(EntityUid target, bool injecting)
    {
        if (!TryComp<DeviceLinkSinkComponent>(target, out var sink)) return;
        foreach (var source in sink.LinkedSources)
        {
            if (!TryComp<KiasDeviceAdapterComponent>(source, out var adapter)) continue;
            adapter.State = injecting;
            if (_kias.IsOnline(source)) _controllers.Emit(source, "DeviceAdapter", "State", KiasGraphValue.Boolean(injecting));
        }
    }
}
