using Content.Shared._Forge.Genetics.Components;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Player;
using System.Numerics;

namespace Content.Client._Forge.Genetics;

public sealed class GeneticVisualsSystem : EntitySystem
{
    [Dependency] private readonly IOverlayManager _overlays = default!;
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly SpriteSystem _sprites = default!;

    private GeneticVisualsOverlay _overlay = default!;

    public override void Initialize()
    {
        base.Initialize();
        _overlay = new GeneticVisualsOverlay();

        SubscribeLocalEvent<GeneticVisualsComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<GeneticVisualsComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<GeneticVisualsComponent, AfterAutoHandleStateEvent>(OnState);
        SubscribeLocalEvent<GeneticVisualsComponent, LocalPlayerAttachedEvent>(OnAttached);
        SubscribeLocalEvent<GeneticVisualsComponent, LocalPlayerDetachedEvent>(OnDetached);
    }

    private void OnStartup(Entity<GeneticVisualsComponent> ent, ref ComponentStartup args)
    {
        ApplyScale(ent);
        if (_players.LocalEntity == ent.Owner)
            _overlays.AddOverlay(_overlay);
    }

    private void OnShutdown(Entity<GeneticVisualsComponent> ent, ref ComponentShutdown args)
    {
        if (TryComp<SpriteComponent>(ent, out var sprite))
            _sprites.SetScale((ent, sprite), Vector2.One);

        if (_players.LocalEntity == ent.Owner)
            _overlays.RemoveOverlay(_overlay);
    }

    private void OnState(Entity<GeneticVisualsComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        ApplyScale(ent);
    }

    private void OnAttached(Entity<GeneticVisualsComponent> ent, ref LocalPlayerAttachedEvent args)
    {
        _overlays.AddOverlay(_overlay);
    }

    private void OnDetached(Entity<GeneticVisualsComponent> ent, ref LocalPlayerDetachedEvent args)
    {
        _overlays.RemoveOverlay(_overlay);
    }

    private void ApplyScale(Entity<GeneticVisualsComponent> ent)
    {
        if (!TryComp<SpriteComponent>(ent, out var sprite))
            return;

        var scale = ent.Comp.Scale <= 0 ? 1f : ent.Comp.Scale;
        _sprites.SetScale((ent, sprite), new Vector2(scale, scale));
    }
}
