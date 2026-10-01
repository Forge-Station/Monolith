using System.Numerics;
using Content.Shared._WF.Wolfmed.Gore;
using Robust.Client.GameObjects;
using Robust.Shared.Graphics.RSI;
using Robust.Shared.Timing;

namespace Content.Client._WF.Wolfmed.Effects;

/// <summary>
/// Draws G1's blood: the spray that travels away from a hit and the mark it leaves on a wall. The server
/// decides what and where; this only paints it.
/// </summary>
/// <remarks>
/// The art is greyscale, so every one of these is tinted with the victim's own blood reagent colour. That
/// colour, the RSI state and the facing all ride networked fields rather than a one-shot message, so a
/// client that walks into PVS an hour after a firefight sees the same wall the shooter did. The spray is
/// slid by hand each frame instead of being given physics: it is a picture, not an object, and there are
/// only ever a handful in the air.
/// </remarks>
public sealed class WolfmedGoreEffectSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SpriteSystem _sprite = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WolfmedHitSplatterComponent, ComponentStartup>(OnSplatterStartup);
        SubscribeLocalEvent<WolfmedHitSplatterComponent, AfterAutoHandleStateEvent>(OnSplatterState);
        SubscribeLocalEvent<WolfmedBloodSplatComponent, ComponentStartup>(OnSplatStartup);
        SubscribeLocalEvent<WolfmedBloodSplatComponent, AfterAutoHandleStateEvent>(OnSplatState);
    }

    private void OnSplatterStartup(Entity<WolfmedHitSplatterComponent> splatter, ref ComponentStartup args)
    {
        splatter.Comp.StartedAt = _timing.CurTime;
        Apply(splatter);
    }

    private void OnSplatterState(Entity<WolfmedHitSplatterComponent> splatter, ref AfterAutoHandleStateEvent args) =>
        Apply(splatter);

    private void OnSplatStartup(Entity<WolfmedBloodSplatComponent> splat, ref ComponentStartup args) =>
        Apply(splat);

    private void OnSplatState(Entity<WolfmedBloodSplatComponent> splat, ref AfterAutoHandleStateEvent args) =>
        Apply(splat);

    /// <inheritdoc/>
    public override void FrameUpdate(float frameTime)
    {
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WolfmedHitSplatterComponent, SpriteComponent>();
        while (query.MoveNext(out var uid, out var splatter, out var sprite))
        {
            if (splatter.Travel <= 0f)
                continue;

            var progress = (float) (now - splatter.StartedAt).TotalSeconds / splatter.Travel;
            Aim((uid, sprite), splatter, Math.Clamp(progress, 0f, 1f));
        }
    }

    private void Apply(Entity<WolfmedHitSplatterComponent> splatter)
    {
        if (!TryComp(splatter, out SpriteComponent? sprite))
            return;

        _sprite.SetColor((splatter.Owner, sprite), splatter.Comp.Color);
        _sprite.LayerSetRsiState((splatter.Owner, sprite), 0, splatter.Comp.State);
        Aim((splatter.Owner, sprite), splatter.Comp, 0f);
    }

    /// <summary>
    /// Points and places the spray in WORLD space. A sprite's rotation and offset are local to its entity, and
    /// the entity rides a grid that may be turned (and turning), so the entity's world rotation comes off the
    /// networked world angle every frame. The art points along +X at zero.
    /// </summary>
    private void Aim(Entity<SpriteComponent> sprite, WolfmedHitSplatterComponent splatter, float progress)
    {
        var local = new Angle(splatter.Angle) - _transform.GetWorldRotation(sprite.Owner);
        _sprite.SetRotation((sprite.Owner, sprite.Comp), local);
        _sprite.SetOffset((sprite.Owner, sprite.Comp), local.ToVec() * splatter.Distance * progress);
    }

    private void Apply(Entity<WolfmedBloodSplatComponent> splat)
    {
        if (!TryComp(splat, out SpriteComponent? sprite))
            return;

        _sprite.SetColor((splat.Owner, sprite), splat.Comp.Color);
        _sprite.LayerSetRsiState((splat.Owner, sprite), 0, splat.Comp.State);
    }
}
