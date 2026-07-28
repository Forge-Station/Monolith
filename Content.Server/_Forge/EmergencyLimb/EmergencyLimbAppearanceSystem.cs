// Author: @lenta313. Все права не защищены / No rights reserved.
using Content.Shared._Forge.EmergencyLimb;
using Content.Shared._Shitmed.Body.Events;
using Content.Shared.Body.Part;
using Content.Shared.Humanoid;

namespace Content.Server._Forge.EmergencyLimb;

/// <summary>
/// Works around a Shitmed limitation: when a limb with a custom <c>baseLayerId</c> (our metal
/// emergency limb, or cybernetics) leaves the body, the body's custom base layer isn't reset, so a
/// replacement default limb keeps showing the old custom texture. We drop that custom base layer
/// whenever an emergency limb is detached (surgery — BodyPartComponentsModifyEvent with Add=false)
/// or destroyed/crumbled (EntityTerminatingEvent).
///
/// We subscribe on the part (not the body) to avoid clashing with the engine's own
/// BodyComponent/BodyPartRemovedEvent subscription.
/// </summary>
public sealed class EmergencyLimbAppearanceSystem : EntitySystem
{
    [Dependency] private readonly SharedHumanoidAppearanceSystem _humanoid = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<EmergencyLimbPartComponent, BodyPartComponentsModifyEvent>(OnPartModified);
        SubscribeLocalEvent<EmergencyLimbPartComponent, EntityTerminatingEvent>(OnPartTerminating);
    }

    // Detached from a body (e.g. removed by surgery). Add == false means removal.
    private void OnPartModified(EntityUid uid, EmergencyLimbPartComponent comp, BodyPartComponentsModifyEvent args)
    {
        if (args.Add)
            return;

        if (TryComp<BodyPartComponent>(uid, out var part))
            ResetLayer(args.Body, part);
    }

    // Deleted/crumbled while still attached.
    private void OnPartTerminating(Entity<EmergencyLimbPartComponent> ent, ref EntityTerminatingEvent args)
    {
        if (TryComp<BodyPartComponent>(ent, out var part) && part.Body is { } body)
            ResetLayer(body, part);
    }

    /// <summary>
    /// Drops the stale custom (metal) base layer from the body so the default / next limb renders.
    /// </summary>
    private void ResetLayer(EntityUid body, BodyPartComponent part)
    {
        if (!TryComp<HumanoidAppearanceComponent>(body, out var humanoid))
            return;

        if (part.ToHumanoidLayers() is not { } layer)
            return;

        if (humanoid.CustomBaseLayers.Remove(layer))
        {
            _humanoid.SetLayerVisibility((body, humanoid), layer, false, null);
            Dirty(body, humanoid);
        }
    }
}
