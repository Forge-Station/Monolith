using Content.Server.Explosion.EntitySystems;
using Content.Shared._Forge.Features.Components;
using Content.Shared.DeviceLinking;

namespace Content.Server._Forge.Features;

public sealed class DeviceLinkOnTriggerSystem : EntitySystem
{
    [Dependency] private readonly SharedDeviceLinkSystem _deviceLink = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<DeviceLinkOnTriggerComponent, TriggerEvent>(OnTrigger);
    }

    private void OnTrigger(EntityUid uid, DeviceLinkOnTriggerComponent component, TriggerEvent args)
    {
        _deviceLink.InvokePort(uid, component.Port);
    }
}
