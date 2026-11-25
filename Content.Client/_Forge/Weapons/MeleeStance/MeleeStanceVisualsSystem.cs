using System.Linq;
using Content.Shared._Forge.Weapons.MeleeStance;
using Robust.Client.Animations;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;

namespace Content.Client._Forge.Weapons.MeleeStance;

/// <summary>
/// Applies the world sprite of the current <see cref="MeleeStanceMode"/> and plays the transition animation.
/// </summary>
/// <remarks>
/// Visuals are applied once per frame rather than directly in the event handler: during prediction the stance can
/// be reset to the server state and re-predicted several times per tick, which would otherwise restart the
/// transition animation over and over.
/// </remarks>
public sealed partial class MeleeStanceVisualsSystem : EntitySystem
{
    [Dependency] private SpriteSystem _sprite = default!;
    [Dependency] private AnimationPlayerSystem _animation = default!;

    private const string AnimationKey = "melee-stance-transition";

    /// <summary>
    /// Stance currently shown on the sprite.
    /// </summary>
    private readonly Dictionary<EntityUid, int> _visualMode = new();

    /// <summary>
    /// Original look of every layer touched by any stance, captured before the first change.
    /// Sprites aren't networked, so at the first apply the sprite is still exactly the prototype one.
    /// </summary>
    private readonly Dictionary<EntityUid, Dictionary<string, LayerSnapshot>> _baseLayers = new();

    private readonly HashSet<EntityUid> _pending = new();

    private readonly record struct LayerSnapshot(RSI? Rsi, RSI.StateId State, bool Visible, Color Color);

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MeleeStanceComponent, MeleeStanceChangedEvent>(OnStanceChanged);
        SubscribeLocalEvent<MeleeStanceComponent, AnimationCompletedEvent>(OnAnimationCompleted);
        SubscribeLocalEvent<MeleeStanceComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnStanceChanged(Entity<MeleeStanceComponent> ent, ref MeleeStanceChangedEvent args)
    {
        _pending.Add(ent);
    }

    private void OnShutdown(Entity<MeleeStanceComponent> ent, ref ComponentShutdown args)
    {
        _pending.Remove(ent);
        _visualMode.Remove(ent);
        _baseLayers.Remove(ent);
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        if (_pending.Count == 0)
            return;

        foreach (var uid in _pending)
        {
            if (!TryComp<MeleeStanceComponent>(uid, out var comp) || !TryComp<SpriteComponent>(uid, out var sprite))
                continue;

            UpdateVisuals((uid, comp), (uid, sprite));
        }

        _pending.Clear();
    }

    private void UpdateVisuals(Entity<MeleeStanceComponent> ent, Entity<SpriteComponent> sprite)
    {
        var comp = ent.Comp;
        if (comp.CurrentMode < 0 || comp.CurrentMode >= comp.Modes.Count)
            return;

        var hadPrevious = _visualMode.TryGetValue(ent, out var previous);
        if (hadPrevious && previous == comp.CurrentMode)
            return;

        var mode = comp.Modes[comp.CurrentMode];
        var stanceIndex = EnsureStanceLayer(sprite, comp);
        EnsureSnapshot(sprite, ent, comp);

        _visualMode[ent] = comp.CurrentMode;

        // A new switch interrupts the running transition.
        if (_animation.HasRunningAnimation(ent, AnimationKey))
            _animation.Stop(ent.Owner, null, AnimationKey);

        ApplyLayers(sprite, ent, comp, mode, stanceIndex);

        // No transition when the entity is first seen (spawn / PVS entry), only on an actual switch.
        if (!hadPrevious || mode.TransitionState == null || mode.TransitionTime <= 0f || stanceIndex == null)
            return;

        var anim = new Animation
        {
            Length = TimeSpan.FromSeconds(mode.TransitionTime),
            AnimationTracks =
            {
                new AnimationTrackSpriteFlick
                {
                    LayerKey = comp.SpriteLayer,
                    KeyFrames = { new AnimationTrackSpriteFlick.KeyFrame(new RSI.StateId(mode.TransitionState), 0f) },
                },
            },
        };

        _animation.Play(ent.Owner, anim, AnimationKey);
    }

    private void OnAnimationCompleted(Entity<MeleeStanceComponent> ent, ref AnimationCompletedEvent args)
    {
        if (args.Key != AnimationKey || !args.Finished)
            return;

        if (!TryComp<SpriteComponent>(ent, out var sprite))
            return;

        var comp = ent.Comp;
        if (comp.CurrentMode < 0 || comp.CurrentMode >= comp.Modes.Count)
            return;

        // The flick track disables auto-animation and leaves the transition state on; restore the stance look.
        Entity<SpriteComponent> spriteEnt = (ent.Owner, sprite);
        var stanceIndex = EnsureStanceLayer(spriteEnt, comp);
        ApplyLayers(spriteEnt, ent, comp, comp.Modes[comp.CurrentMode], stanceIndex);
    }

    private void ApplyLayers(
        Entity<SpriteComponent> sprite,
        EntityUid uid,
        MeleeStanceComponent comp,
        MeleeStanceMode mode,
        int? stanceIndex)
    {
        var nullable = sprite.AsNullable();

        // 1. Restore every touched layer this stance doesn't override.
        if (_baseLayers.TryGetValue(uid, out var snapshots))
        {
            foreach (var (key, snapshot) in snapshots)
            {
                if (mode.Layers.ContainsKey(key))
                    continue;

                if (key == comp.SpriteLayer && mode.State != null)
                    continue;

                if (!_sprite.LayerMapTryGet(nullable, key, out var index, false))
                    continue;

                _sprite.LayerSetRsi(nullable, index, snapshot.Rsi, snapshot.State);
                _sprite.LayerSetVisible(nullable, index, snapshot.Visible);
                _sprite.LayerSetColor(nullable, index, snapshot.Color);
                _sprite.LayerSetAutoAnimated(nullable, index, true);
            }
        }

        // 2. Main stance layer state.
        if (mode.State != null && stanceIndex != null)
        {
            _sprite.LayerSetRsiState(nullable, stanceIndex.Value, new RSI.StateId(mode.State));
            _sprite.LayerSetAutoAnimated(nullable, stanceIndex.Value, true);
        }

        // 3. Arbitrary layer overrides.
        foreach (var (key, data) in mode.Layers)
        {
            if (!_sprite.LayerMapTryGet(nullable, key, out var index, false))
            {
                Log.Warning($"{ToPrettyString(uid)}: melee stance layer '{key}' not found in the sprite.");
                continue;
            }

            _sprite.LayerSetData(nullable, index, data);
            _sprite.LayerSetAutoAnimated(nullable, index, true);
        }
    }

    /// <summary>
    /// Makes sure <see cref="MeleeStanceComponent.SpriteLayer"/> is mapped, falling back to layer 0.
    /// </summary>
    private int? EnsureStanceLayer(Entity<SpriteComponent> sprite, MeleeStanceComponent comp)
    {
        if (_sprite.LayerMapTryGet(sprite.AsNullable(), comp.SpriteLayer, out var index, false))
            return index;

        if (!sprite.Comp.AllLayers.Any())
            return null;

        _sprite.LayerMapSet(sprite.AsNullable(), comp.SpriteLayer, 0);
        return 0;
    }

    private void EnsureSnapshot(Entity<SpriteComponent> sprite, EntityUid uid, MeleeStanceComponent comp)
    {
        if (_baseLayers.ContainsKey(uid))
            return;

        var snapshots = new Dictionary<string, LayerSnapshot>();
        var keys = new HashSet<string> { comp.SpriteLayer };
        foreach (var mode in comp.Modes)
        {
            keys.UnionWith(mode.Layers.Keys);
        }

        foreach (var key in keys)
        {
            if (!_sprite.LayerMapTryGet(sprite.AsNullable(), key, out var index, false))
                continue;

            if (!_sprite.TryGetLayer(sprite.AsNullable(), index, out var layer, false))
                continue;

            snapshots[key] = new LayerSnapshot(layer.RSI, layer.State, layer.Visible, layer.Color);
        }

        _baseLayers[uid] = snapshots;
    }
}
