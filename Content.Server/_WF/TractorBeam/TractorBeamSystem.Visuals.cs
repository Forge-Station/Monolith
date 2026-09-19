using System.Numerics;
using Content.Shared._WF.TractorBeam;
using Robust.Server.GameStates;
using Robust.Shared.Map.Components;

namespace Content.Server._WF.TractorBeam;

public sealed partial class TractorBeamSystem
{
    [Dependency] private PvsOverrideSystem _beamPvs = default!;

    private float GetBeamHalfWidth(EntityUid target, Vector2 start, Vector2 end)
    {
        return TractorBeamGeometry.GetTargetHalfWidth(start, end, Comp<MapGridComponent>(target).LocalAABB,
            TransformSystem.GetWorldMatrix(target));
    }

    private void UpdateVisual(EntityUid uid, TractorBeamEmitterComponent beam)
    {
        if (!beam.Active || beam.Target is not { } target || beam.SourceGrid is not { } source ||
            TerminatingOrDeleted(target) || TerminatingOrDeleted(source) ||
            !TryComp(uid, out TransformComponent? emitterTransform))
        {
            DeleteVisual(beam);
            return;
        }

        // Parent the effect directly to the grid. Overriding a dish or one of its children would
        // unnecessarily send the machinery to distant clients along with the visible field.
        var coordinates = emitterTransform.ParentUid == source
            ? emitterTransform.Coordinates
            : TransformSystem.ToCoordinates(source, TransformSystem.GetMapCoordinates(emitterTransform));
        if (beam.Visual is not { } visual || TerminatingOrDeleted(visual))
        {
            visual = Spawn(null, coordinates);
            beam.Visual = visual;
            _beamPvs.AddGlobalOverride(visual);
        }
        else if (Transform(visual).Coordinates != coordinates)
        {
            TransformSystem.SetCoordinates(visual, coordinates);
        }

        var effect = EnsureComp<TractorBeamVisualComponent>(visual);
        var targetBounds = Comp<MapGridComponent>(target).LocalAABB;
        var widthScale = float.IsFinite(beam.VisualWidthScale) ? Math.Clamp(beam.VisualWidthScale, 0.05f, 1f) : 1f;
        if (effect.Target == target && effect.TargetOffset == beam.TargetOffset && effect.Strain == beam.Strain &&
            effect.TargetBounds == targetBounds && effect.WidthScale == widthScale)
            return;

        effect.Target = target;
        effect.TargetOffset = beam.TargetOffset;
        effect.TargetBounds = targetBounds;
        effect.Strain = beam.Strain;
        effect.WidthScale = widthScale;
        Dirty(visual, effect);
    }

    private void DeleteVisual(TractorBeamEmitterComponent beam)
    {
        if (beam.Visual is not { } visual)
            return;

        beam.Visual = null;
        // This entity and its override belong exclusively to this beam. Never remove an override
        // from either ship: grids may already be globally visible for unrelated systems.
        _beamPvs.RemoveGlobalOverride(visual);
        if (!TerminatingOrDeleted(visual))
            Del(visual);
    }
}
