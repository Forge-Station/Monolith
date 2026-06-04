using Content.Client.Weapons.Ranged.Components;
using Content.Client.Weapons.Ranged.ItemStatus;
using Content.Client.Weapons.Ranged.Systems;
using Content.Shared._Forge.Weapons.ThrowingAmmoProvider;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;

namespace Content.Client._Forge.Weapons.ThrowingAmmoProvider;

public sealed partial class ThrowingAmmoCounterSystem : EntitySystem
{
    [Dependency] private SpriteSystem _sprite = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ThrowingAmmoProviderComponent, GunSystem.AmmoCounterControlEvent>(OnThrowingAmmoControl);
        SubscribeLocalEvent<ThrowingAmmoProviderComponent, GunSystem.UpdateAmmoCounterEvent>(OnThrowingAmmoUpdate);
        SubscribeLocalEvent<ThrowingAmmoProviderComponent, AfterAutoHandleStateEvent>(OnThrowingAmmoState);
        SubscribeLocalEvent<HiddenUntilThrownComponent, ComponentStartup>(OnHiddenStartup);
        SubscribeLocalEvent<HiddenUntilThrownComponent, ComponentShutdown>(OnHiddenShutdown);
    }

    private void OnThrowingAmmoControl(EntityUid uid,
        ThrowingAmmoProviderComponent component,
        GunSystem.AmmoCounterControlEvent args)
    {
        args.Control = new ThrowingStatusControl();
    }

    private void OnThrowingAmmoUpdate(EntityUid uid,
        ThrowingAmmoProviderComponent component,
        GunSystem.UpdateAmmoCounterEvent args)
    {
        if (args.Control is not ThrowingStatusControl control)
            return;

        control.Update(component.AmmoCount, component.Capacity);
        args.Handled = true;
    }

    private void OnThrowingAmmoState(EntityUid uid,
        ThrowingAmmoProviderComponent component,
        ref AfterAutoHandleStateEvent args)
    {
        if (TryComp<AmmoCounterComponent>(uid, out var counter) &&
            counter.Control is ThrowingStatusControl control)
            control.Update(component.AmmoCount, component.Capacity);
    }

    private void OnHiddenStartup(EntityUid uid, HiddenUntilThrownComponent component, ComponentStartup args)
    {
        if (TryComp<SpriteComponent>(uid, out var sprite))
            _sprite.SetVisible((uid, sprite), false);
    }

    private void OnHiddenShutdown(EntityUid uid, HiddenUntilThrownComponent component, ComponentShutdown args)
    {
        if (TryComp<SpriteComponent>(uid, out var sprite))
            _sprite.SetVisible((uid, sprite), true);
    }

    private sealed class ThrowingStatusControl : Control
    {
        private readonly BulletRender _bulletRender;

        public ThrowingStatusControl()
        {
            MinHeight = 15;
            HorizontalExpand = true;
            VerticalAlignment = VAlignment.Center;
            AddChild(_bulletRender = new BulletRender
            {
                HorizontalAlignment = HAlignment.Right,
                VerticalAlignment = VAlignment.Bottom,
            });
        }

        public void Update(int count, int capacity)
        {
            _bulletRender.Count = count;
            _bulletRender.Capacity = capacity;
            _bulletRender.Type = capacity > 50 ? BulletRender.BulletType.Tiny : BulletRender.BulletType.Normal;
        }
    }
}
