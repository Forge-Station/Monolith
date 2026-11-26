using Content.Client.IconSmoothing;
using Content.Shared.Doors.Components;
using Robust.Client.GameObjects;

namespace Content.Client.Doors;

/// <summary>
/// Secret doors draw wall corners from the real wall RSI while closed, and the door
/// animation while moving or open. Both sets of layers stay on the sprite, so the
/// unused set has to be hidden or the old wall sprite sits on top of the doorway.
/// </summary>
public sealed partial class SecretDoorSmoothingSystem : EntitySystem
{
    [Dependency] private AppearanceSystem _appearance = default!;
    [Dependency] private IconSmoothSystem _smooth = default!;
    [Dependency] private SpriteSystem _sprite = default!;

    public override void Initialize()
    {
        base.Initialize();
        UpdatesAfter.Add(typeof(IconSmoothSystem));
        SubscribeLocalEvent<DoorComponent, ComponentStartup>(OnStartup);
    }

    public override void FrameUpdate(float frameTime)
    {
        var query = EntityQueryEnumerator<DoorComponent, IconSmoothComponent, SpriteComponent>();
        while (query.MoveNext(out var uid, out var door, out var smooth, out var sprite))
        {
            if (smooth.States.Length == 0)
                continue;

            Apply((uid, door), door.State, smooth, sprite);
        }
    }

    private void OnStartup(Entity<DoorComponent> ent, ref ComponentStartup args)
    {
        if (!TryComp<IconSmoothComponent>(ent, out var smooth) || !TryComp<SpriteComponent>(ent, out var sprite))
            return;

        Apply(ent, ent.Comp.State, smooth, sprite);
    }

    /// <summary>
    /// Called from <see cref="DoorSystem"/> after it applies the door sprite.
    /// DoorComponent can only have one AppearanceChangeEvent subscription.
    /// </summary>
    public void OnDoorAppearance(Entity<DoorComponent> ent, AppearanceComponent? appearance)
    {
        if (!TryComp<IconSmoothComponent>(ent, out var smooth) || !TryComp<SpriteComponent>(ent, out var sprite))
            return;

        if (!_appearance.TryGetData(ent, DoorVisuals.State, out DoorState state, appearance))
            state = ent.Comp.State;

        Apply(ent, state, smooth, sprite);
    }

    private void Apply(Entity<DoorComponent> ent, DoorState state, IconSmoothComponent smooth, SpriteComponent sprite)
    {
        if (smooth.States.Length == 0)
            return;

        var closed = state is DoorState.Closed;
        if (smooth.Enabled != closed)
            _smooth.SetEnabled(ent.Owner, closed);

        if (!_sprite.LayerMapTryGet((ent.Owner, sprite), DoorVisualLayers.Base, out var baseIndex, false))
            return;

        var i = 0;
        foreach (var layer in sprite.AllLayers)
        {
            var visible = closed ? i != baseIndex : i == baseIndex;
            if (layer.Visible != visible)
                _sprite.LayerSetVisible((ent.Owner, sprite), i, visible);
            i++;
        }
    }
}
