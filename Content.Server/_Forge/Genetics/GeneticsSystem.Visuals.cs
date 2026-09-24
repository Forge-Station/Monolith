using Content.Shared._Forge.Genetics;
using Content.Shared._Forge.Genetics.Components;
using Robust.Shared.GameObjects;

namespace Content.Server._Forge.Genetics;

public sealed partial class GeneticsSystem
{
    [Dependency] private readonly SharedPointLightSystem _lights = default!;

    public void RefreshVisuals(EntityUid uid, GenomeComponent? genome = null)
    {
        if (!Resolve(uid, ref genome, false))
        {
            RemComp<GeneticVisualsComponent>(uid);
            return;
        }

        var overlay = Color.Transparent;
        var glow = Color.Transparent;
        var energy = 0f;
        var radius = 1.6f;
        var scale = 1f;
        var flicker = false;
        var any = false;

        foreach (var (id, state) in genome.Genes)
        {
            if (!state.Active || !Prototypes.TryIndex<GenePrototype>(id, out var proto))
                continue;

            any = true;
            if (proto.OverlayColor.A > 0)
                overlay = Blend(overlay, proto.OverlayColor);

            if (proto.GlowColor.A > 0 || proto.GlowEnergy > 0)
            {
                glow = proto.GlowColor.A > 0 ? proto.GlowColor : proto.OverlayColor;
                energy = MathF.Max(energy, proto.GlowEnergy);
                radius = MathF.Max(radius, 1.4f + proto.GlowEnergy * 0.15f);
            }

            if (!MathHelper.CloseTo(proto.VisualScale, 1f))
                scale *= proto.VisualScale;

            flicker |= proto.VisualFlicker;
        }

        if (!any)
        {
            RemComp<GeneticVisualsComponent>(uid);
            return;
        }

        var visuals = EnsureComp<GeneticVisualsComponent>(uid);
        visuals.OverlayColor = overlay;
        visuals.GlowColor = glow;
        visuals.GlowEnergy = energy;
        visuals.GlowRadius = radius;
        visuals.Scale = scale;
        visuals.Flicker = flicker;
        Dirty(uid, visuals);

        if (energy > 0 && glow.A > 0)
        {
            var light = _lights.EnsureLight(uid);
            _lights.SetColor(uid, glow, light);
            _lights.SetRadius(uid, radius, light);
            _lights.SetEnergy(uid, energy, light);
            _lights.SetEnabled(uid, true, light);
        }
    }

    private static Color Blend(Color a, Color b)
    {
        if (a.A <= 0)
            return b;
        if (b.A <= 0)
            return a;

        return new Color(
            (a.R + b.R) / 2f,
            (a.G + b.G) / 2f,
            (a.B + b.B) / 2f,
            MathF.Max(a.A, b.A));
    }
}
