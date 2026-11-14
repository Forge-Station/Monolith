using System.Linq;
using Content.Shared._WF.Augments;
using Content.Shared.Preferences;

namespace Content.Shared.Humanoid;

public abstract partial class SharedHumanoidAppearanceSystem
{
    /// <summary>
    /// Applies the profile's augments to a body: the server swaps the parts, then the styles are drawn. A slot the
    /// profile leaves natural keeps an augment the body already has, as its part does.
    /// </summary>
    protected void ApplyAugments(EntityUid uid, HumanoidCharacterProfile profile, HumanoidAppearanceComponent humanoid)
    {
        var ev = new WFApplyAugmentsEvent(profile);
        RaiseLocalEvent(uid, ref ev);
        ApplyAugmentVisuals(uid, profile, humanoid, keepUnpicked: true);
    }

    /// <summary>
    /// Draws each augment's style over its layers and hides missing limbs. What an earlier load drew is taken back
    /// first, unless something (the leg style) has replaced it since; with <paramref name="keepUnpicked"/>, only on
    /// the slots this profile picks. The editor doll passes false, so an unpicked augment comes off it.
    /// </summary>
    public void ApplyAugmentVisuals(EntityUid uid,
        HumanoidCharacterProfile profile,
        HumanoidAppearanceComponent humanoid,
        bool keepUnpicked = false)
    {
        var picked = profile.Augments.Keys.SelectMany(WFAugmentRules.Layers).ToHashSet();

        foreach (var (layer, info) in humanoid.AugmentLayers.ToList())
        {
            if (keepUnpicked && !picked.Contains(layer))
            {
                // The leg style reloads the species' legs under a kept augment.
                humanoid.CustomBaseLayers[layer] = info;
                continue;
            }

            if (humanoid.CustomBaseLayers.TryGetValue(layer, out var current) && current.Equals(info))
                humanoid.CustomBaseLayers.Remove(layer);
            humanoid.AugmentLayers.Remove(layer);
        }

        foreach (var layer in humanoid.AugmentHidden.ToList())
        {
            if (keepUnpicked && !picked.Contains(layer))
                continue;

            SetLayerVisibility((uid, humanoid), layer, true);
            humanoid.AugmentHidden.Remove(layer);
        }

        var digitigrade = WFAugmentRules.Digitigrade(profile, _proto);
        foreach (var (slot, choice) in profile.Augments)
        {
            if (!_proto.TryIndex(choice.Augment, out var augment))
                continue;

            if (augment.Kind == WFAugmentKind.Missing)
            {
                foreach (var layer in WFAugmentRules.Layers(slot))
                {
                    SetLayerVisibility((uid, humanoid), layer, false);
                    humanoid.AugmentHidden.Add(layer);
                }
                continue;
            }

            if (!augment.Styled || choice.Style is not { } styleId || !_proto.TryIndex(styleId, out var style))
                continue;

            foreach (var layer in WFAugmentRules.Layers(slot))
            {
                var under = WFAugmentRules.Underlying(humanoid, layer, _proto);
                if (WFAugmentRules.Sprite(style, layer, digitigrade, under, _proto) is not { } sprite)
                    continue;

                var info = new CustomBaseLayerInfo(sprite);
                humanoid.CustomBaseLayers[layer] = info;
                humanoid.AugmentLayers[layer] = info;
            }
        }

        Dirty(uid, humanoid);
    }
}
