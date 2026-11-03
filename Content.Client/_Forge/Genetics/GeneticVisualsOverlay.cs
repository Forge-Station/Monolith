using Content.Shared._Forge.Genetics.Components;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.Timing;

namespace Content.Client._Forge.Genetics;

public sealed class GeneticVisualsOverlay : Robust.Client.Graphics.Overlay
{
    [Dependency] private readonly IEntityManager _entities = default!;
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public override OverlaySpace Space => OverlaySpace.ScreenSpace;

    public GeneticVisualsOverlay()
    {
        IoCManager.InjectDependencies(this);
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (_players.LocalEntity is not { } player)
            return;

        if (!_entities.TryGetComponent(player, out GeneticVisualsComponent? visuals))
            return;

        if (visuals.OverlayColor.A <= 0)
            return;

        var alpha = visuals.OverlayColor.A;
        if (visuals.Flicker)
        {
            var pulse = 0.65f + 0.35f * MathF.Sin((float) _timing.CurTime.TotalSeconds * 6f);
            alpha *= pulse;
        }

        var color = visuals.OverlayColor.WithAlpha(alpha);
        args.ScreenHandle.DrawRect(args.ViewportBounds, color);
    }
}
