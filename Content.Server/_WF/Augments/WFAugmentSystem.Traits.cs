using Content.Shared._WF.Augments;
using Content.Shared._WF.LegStyle;
using Content.Shared.Body.Components;
using Content.Shared.Humanoid;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Augments;

public sealed partial class WFAugmentSystem
{
    /// <summary>The style limbs fitted by a trait (Prybar Prosthetics, Bionic Legs) are drawn in: the bishop chassis they always had.</summary>
    public static readonly ProtoId<WFAugmentStylePrototype> TraitLimbStyle = "WFAugmentStyleEEBishop";

    /// <summary>
    /// Draws the limbs a trait fitted in <see cref="TraitLimbStyle"/>, fitted to the body's own limb shape, so an
    /// Avali's prosthetic arm keeps the Avali outline instead of the human one.
    /// </summary>
    public void StyleTraitLimbs(EntityUid body, params WFAugmentSlot[] slots)
    {
        if (!TryComp<BodyComponent>(body, out var bodyComp)
            || !TryComp<HumanoidAppearanceComponent>(body, out var humanoid)
            || !_proto.TryIndex(TraitLimbStyle, out var style))
            return;

        var digitigrade = humanoid.LegStyle is { } legStyle
                          && _proto.TryIndex(legStyle, out var legProto)
                          && legProto.Stance == LegStance.Digitigrade;

        foreach (var slot in slots)
        {
            if (FindLimb((body, bodyComp), slot) is not { } limb)
                continue;

            foreach (var layer in WFAugmentRules.Layers(slot))
            {
                var under = WFAugmentRules.Underlying(humanoid, layer, _proto);
                if (WFAugmentRules.Sprite(style, layer, digitigrade, under, _proto) is not { } sprite)
                    continue;

                // White, so a layer drawn before in skin tint doesn't keep it.
                var info = new CustomBaseLayerInfo(sprite, Color.White);
                humanoid.CustomBaseLayers[layer] = info;
                humanoid.AugmentLayers[layer] = info;
            }

            StyleLimb(body, limb.Id, style, digitigrade);
        }

        Dirty(body, humanoid);
    }
}
