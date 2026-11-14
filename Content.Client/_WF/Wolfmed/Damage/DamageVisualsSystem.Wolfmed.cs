using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Wolfmed.Damage;
using Content.Shared.Body.Part;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using System.Numerics;
using Robust.Client.Animations;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Animations;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client.Damage;

/// <summary>Per-limb damage sprites for wound hosts (P3-4). Body of HOOK 20's insertions in DamageVisualsSystem.cs.</summary>
public sealed partial class DamageVisualsSystem
{
    [Dependency] private AnimationPlayerSystem _wfAnimation = default!;
    [Dependency] private IGameTiming _wfTiming = default!;

    /// <summary>A spurt older than this when its state lands is not played: the body was out of view, or just seen.</summary>
    private static readonly TimeSpan SprayWindow = TimeSpan.FromSeconds(1);

    /// <summary>Re-runs the damage visuals when a wound host's per-part damage projection changes.</summary>
    private void OnPartDamageVisualsState(Entity<PartDamageVisualsComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        if (TryComp(ent, out DamageVisualsComponent? visuals) && TryComp(ent, out AppearanceComponent? appearance))
            HandleDamage(ent, appearance, visuals);

        // Option B: severed-limb wound rendering, independent of whether this entity has a DamageVisuals block.
        UpdateDetachedPartDamage(ent.Owner, ent.Comp);
        UpdateDegradation(ent.Owner, ent.Comp); // V3
        UpdateTreatments(ent.Owner, ent.Comp); // G3
        UpdateWoundOverlays(ent.Owner, ent.Comp); // VISUALS
    }

    /// <summary>Option B: a detached part's own BodyPartComponent state changed (e.g. it was just severed).</summary>
    private void OnBodyPartState(Entity<BodyPartComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        if (!TryComp(ent, out PartDamageVisualsComponent? damage))
            return;

        UpdateDetachedPartDamage(ent.Owner, damage);
        UpdateDegradation(ent.Owner, damage); // V3
        UpdateTreatments(ent.Owner, damage); // G3
    }

    /// <summary>Drives each targeted sprite layer from that limb's own damage instead of the mob's aggregate (D30).</summary>
    private void UpdatePartDamageVisuals(EntityUid uid, SpriteComponent sprite,
        DamageVisualsComponent damageVisComp, PartDamageVisualsComponent partDamage)
    {
        foreach (var layerMapKey in damageVisComp.TargetLayerMapKeys)
        {
            var damage = layerMapKey is HumanoidVisualLayers humanoidLayer
                ? GetLayerDamage(partDamage, humanoidLayer)
                : null;
            var groups = damage?.GetDamagePerGroup(_prototypeManager);

            // The per-group threshold cache is deliberately not consulted here (WG's LastThresholdPerGroup is
            // keyed by group, not by (layer, group)); recompute per layer every time instead, as Onyx does.
            foreach (var group in damageVisComp.DamageOverlayGroups!.Keys)
            {
                var amount = groups?.GetValueOrDefault(group) ?? FixedPoint2.Zero;
                CheckThresholdBoundary(amount, FixedPoint2.Zero, damageVisComp, out var threshold);
                UpdateTargetLayer(sprite, damageVisComp, layerMapKey, group, threshold);
            }
        }
    }

    /// <summary>Damage shown on one layer: the limb itself plus the extremity Wolfgate has no layer for (P3-D25).</summary>
    private static DamageSpecifier? GetLayerDamage(PartDamageVisualsComponent partDamage, HumanoidVisualLayers layer)
    {
        partDamage.Damage.TryGetValue(layer, out var damage);

        // WOLFGATE: P3-D25 - fold LHand->LArm, RHand->RArm, LFoot->LLeg, RFoot->RLeg. WG's stock six-layer
        // overlay has no hand/foot targetLayers and no hand/foot art, so a faithful per-layer port would make
        // hand and foot wounds invisible where today (pre-phase-3) they light every limb layer.
        var extra = layer switch
        {
            HumanoidVisualLayers.LArm => HumanoidVisualLayers.LHand,
            HumanoidVisualLayers.RArm => HumanoidVisualLayers.RHand,
            HumanoidVisualLayers.LLeg => HumanoidVisualLayers.LFoot,
            HumanoidVisualLayers.RLeg => HumanoidVisualLayers.RFoot,
            _ => (HumanoidVisualLayers?) null,
        };

        if (extra is { } extraLayer && partDamage.Damage.TryGetValue(extraLayer, out var extraDamage))
            damage = damage is null ? extraDamage : damage + extraDamage;

        return damage;
    }

    // --- V3: per-part degradation overlays ---

    /// <summary>
    /// Shows each limb's degradation stage. One overlay layer per visual layer, added once and then only
    /// toggled or re-stated, so this never fights the damage overlays or the Z-level draw depth.
    /// </summary>
    private void UpdateDegradation(EntityUid uid, PartDamageVisualsComponent damage)
    {
        if (!TryComp(uid, out SpriteComponent? sprite))
            return;

        var id = CompOrNull<WolfmedDegradationVisualsComponent>(uid)?.Profile ??
                 WolfmedDegradationVisualsComponent.DefaultProfile;
        if (!_prototypeManager.TryIndex(id, out WolfmedDegradationProfilePrototype? profile))
            return;

        // A severed limb draws its overlay on its own sprite, which has no humanoid layer map; back on a
        // body it is the body's layers that draw it, so its own copy goes quiet.
        var severed = TryComp(uid, out BodyPartComponent? part);
        if (severed && part!.Body != null)
        {
            foreach (var layer in WolfmedDegradationLayers.All)
                UpdateDegradationLayer(uid, sprite, profile, layer, WolfmedPartDegradation.None, true);

            return;
        }

        foreach (var layer in WolfmedDegradationLayers.All)
            UpdateDegradationLayer(uid, sprite, profile, layer,
                damage.Degradation.GetValueOrDefault(layer), severed);
    }

    /// <summary>Creates (once) and updates one degradation overlay layer.</summary>
    private void UpdateDegradationLayer(EntityUid uid, SpriteComponent sprite,
        WolfmedDegradationProfilePrototype profile, HumanoidVisualLayers layer, WolfmedPartDegradation stage,
        bool severed)
    {
        var state = stage == WolfmedPartDegradation.None ? null : profile.GetState(layer, stage);
        var key = $"WolfmedDegradation{layer}";
        if (!SpriteSystem.LayerMapTryGet((uid, sprite), key, out var index, false))
        {
            if (state == null)
                return;

            // Immediately above the limb it belongs to: over the skin, under the jumpsuit and everything
            // else the inventory adds further up the stack. A body sprite with no layer for this limb gets
            // no overlay at all, which keeps human-shaped art off a non-humanoid wound host; a severed limb
            // has no humanoid layer map to begin with, so its one overlay goes on top.
            int? insert = null;
            if (SpriteSystem.LayerMapTryGet((uid, sprite), layer, out var limb, false))
                insert = Math.Max(limb, MarkingTop(uid, sprite, layer)) + 1; // playtest 5: over the fur
            else if (!severed)
                return;

            index = SpriteSystem.AddLayer((uid, sprite), new SpriteSpecifier.Rsi(profile.Rsi, state), insert);
            SpriteSystem.LayerMapSet((uid, sprite), key, index);
        }

        SpriteSystem.LayerSetVisible((uid, sprite), index, state != null);
        if (state != null)
            SpriteSystem.LayerSetRsiState((uid, sprite), index, state);
    }

    // --- G3: treatment overlays ---

    /// <summary>
    /// Shows the dressing or splint each limb is wearing. Same shape as the degradation overlay: one
    /// layer per visual layer, added once and afterwards only toggled or re-stated.
    /// </summary>
    private void UpdateTreatments(EntityUid uid, PartDamageVisualsComponent damage)
    {
        if (!TryComp(uid, out SpriteComponent? sprite))
            return;

        var id = CompOrNull<WolfmedTreatmentVisualsComponent>(uid)?.Profile ??
                 WolfmedTreatmentVisualsComponent.DefaultProfile;
        if (!_prototypeManager.TryIndex(id, out WolfmedTreatmentOverlayProfilePrototype? profile))
            return;

        foreach (var layer in WolfmedTreatmentLayers.All)
            UpdateTreatmentLayer(uid, sprite, profile, layer, damage.Treatments.GetValueOrDefault(layer));
    }

    /// <summary>Creates (once) and updates one treatment overlay layer.</summary>
    private void UpdateTreatmentLayer(EntityUid uid, SpriteComponent sprite,
        WolfmedTreatmentOverlayProfilePrototype profile, HumanoidVisualLayers layer,
        WolfmedPartTreatment treatment)
    {
        var state = treatment == WolfmedPartTreatment.None ? null : profile.GetState(layer, treatment);
        var key = $"WolfmedTreatment{layer}";
        if (!SpriteSystem.LayerMapTryGet((uid, sprite), key, out var index, false))
        {
            if (state == null)
                return;

            // Above the limb and above its degradation overlay, below the clothing the inventory adds
            // further up: a bandage covers a wound and a sleeve covers the bandage. Whichever of the two
            // overlays is created second, inserting just above the one below it keeps that order, because
            // an insert at the lower index pushes this layer up with it.
            if (!SpriteSystem.LayerMapTryGet((uid, sprite), layer, out var below, false))
                return;

            if (SpriteSystem.LayerMapTryGet((uid, sprite), $"WolfmedDegradation{layer}", out var wound, false))
                below = Math.Max(below, wound);

            // Over the jumpsuit, gloves and shoes, under a coat or a hardsuit: a dressing has to be seen to be any
            // use as feedback, and a bandaged chest under a jumpsuit showed nothing at all. The anchor is the "id"
            // slot, which sits between shoes/ears and outer clothing and carries no sprite of its own. Worn
            // clothing is inserted just before its own slot layer, so anchoring on outerClothing itself would
            // land this above a coat that is already on.
            var insert = SpriteSystem.LayerMapTryGet((uid, sprite), "id", out var anchor, false)
                ? Math.Max(anchor, below + 1)
                : below + 1;
            index = SpriteSystem.AddLayer((uid, sprite), new SpriteSpecifier.Rsi(profile.Rsi, state), insert);
            SpriteSystem.LayerMapSet((uid, sprite), key, index);
        }

        SpriteSystem.LayerSetVisible((uid, sprite), index, state != null);
        if (state != null)
            SpriteSystem.LayerSetRsiState((uid, sprite), index, state);
    }

    // --- VISUALS: open wounds and rot ---

    /// <summary>
    /// Draws each organic limb's open wound (tinted the body's blood colour) and its rot. Same shape as the
    /// degradation overlay: one layer of each per visual layer, added once, then only toggled or re-stated, and
    /// shifted by the species' row in <see cref="WolfmedOverlayOffsetsPrototype"/>.
    /// </summary>
    // Stacking on each limb: the limb, its degradation, its rot, its wound, then the clothing. A layer made later is
    // inserted above the ones below it in that order, and an insert under it pushes it up, so the order holds
    // whichever is created first. The torso, arms and legs sit under the jumpsuit; hands and feet, which draw over
    // it, sit under the gloves and shoes. A severed limb draws neither: it has no humanoid layer map.
    private void UpdateWoundOverlays(EntityUid uid, PartDamageVisualsComponent damage)
    {
        if (HasComp<BodyPartComponent>(uid) || !TryComp(uid, out SpriteComponent? sprite))
            return;

        var species = CompOrNull<HumanoidAppearanceComponent>(uid)?.Species;
        _prototypeManager.TryIndex<WolfmedOverlayOffsetsPrototype>(WolfmedOverlayOffsetsPrototype.Default,
            out var offsets);

        foreach (var layer in WolfmedDegradationLayers.All)
        {
            var shift = species is { } id && offsets != null ? offsets.Get(id, layer) : Vector2i.Zero;
            var offset = new Vector2(shift.X, shift.Y) / EyeManager.PixelsPerMeter;

            UpdateOverlayLayer(uid, sprite, layer, $"WolfmedRot{layer}", WolfmedWoundOverlays.RotRsi,
                damage.Rot.Contains(layer) ? WolfmedWoundOverlays.GetRotState(layer) : null, offset, null,
                $"WolfmedDegradation{layer}");
            UpdateOverlayLayer(uid, sprite, layer, $"WolfmedWound{layer}", WolfmedWoundOverlays.WoundRsi,
                WolfmedWoundOverlays.GetWoundState(layer, damage.Wounds.GetValueOrDefault(layer)), offset,
                damage.WoundColor, $"WolfmedDegradation{layer}", $"WolfmedRot{layer}");
        }

        // Playtest 4: the stumps and arteries go on top of everything. A stump is a missing limb, and a spray leaves
        // the body, so hair, a helmet or a collar must not hide them. Per site, bottom to top: the stump's flesh in
        // the blood colour, its bone, its drip while it bleeds, then the artery, which rests on the still frame and
        // plays its spray once on every spurting one per blood spurt.
        var spray = damage.ArterySprayAt != damage.LastArterySprayAt &&
                    _wfTiming.CurTime - damage.ArterySprayAt <= SprayWindow;
        damage.LastArterySprayAt = damage.ArterySprayAt;
        foreach (var site in Enum.GetValues<WolfmedArterySite>())
        {
            var shift = species is { } at && offsets != null ? offsets.Get(at, WolfmedArterySites.Layer(site)) : Vector2i.Zero;
            var offset = new Vector2(shift.X, shift.Y) / EyeManager.PixelsPerMeter;

            var stump = damage.Stumps.GetValueOrDefault(site);
            var open = stump != WolfmedWoundOverlay.None;
            UpdateSiteLayer(uid, sprite, site, SiteKind.Stump, WolfmedWoundOverlays.StumpRsi,
                open ? WolfmedWoundOverlays.GetStumpState(site) : null, offset, damage.WoundColor);
            UpdateSiteLayer(uid, sprite, site, SiteKind.StumpBone, WolfmedWoundOverlays.StumpRsi,
                open ? WolfmedWoundOverlays.GetStumpBoneState(site) : null, offset, Color.White);
            UpdateSiteLayer(uid, sprite, site, SiteKind.StumpBleed, WolfmedWoundOverlays.StumpRsi,
                WolfmedWoundOverlays.GetStumpBleedState(site, stump), offset, damage.WoundColor);

            var look = damage.Arteries.GetValueOrDefault(site);
            var key = SiteKey(site, SiteKind.Artery);
            var still = look == WolfmedArteryOverlay.None
                ? null
                : WolfmedWoundOverlays.GetArteryState(site, WolfmedArteryOverlay.Still);
            UpdateSiteLayer(uid, sprite, site, SiteKind.Artery, WolfmedWoundOverlays.ArteryRsi, still, offset, damage.WoundColor);

            if (look != WolfmedArteryOverlay.Bleeding)
            {
                if (_wfAnimation.HasRunningAnimation(uid, key))
                    _wfAnimation.Stop(uid, key);
            }
            else if (spray)
            {
                Spray(uid, sprite, key, site);
            }
        }
    }

    /// <summary>The layers one site stacks, bottom to top.</summary>
    private enum SiteKind : byte
    {
        Stump,
        StumpBone,
        StumpBleed,
        Artery,
    }

    private static string SiteKey(WolfmedArterySite site, SiteKind kind) => $"Wolfmed{kind}{site}";

    /// <summary>
    /// Creates (once) and updates one of a site's layers. A new layer goes above the sprite's own layers: under the
    /// site's layers of a later kind if any exist, else just above those of an earlier kind, else on top of everything.
    /// </summary>
    private void UpdateSiteLayer(EntityUid uid, SpriteComponent sprite, WolfmedArterySite site, SiteKind kind, ResPath rsi,
        string? state, Vector2 offset, Color colour)
    {
        var key = SiteKey(site, kind);
        if (!SpriteSystem.LayerMapTryGet((uid, sprite), key, out var index, false))
        {
            if (state == null)
                return;

            int? above = null;
            int? below = null;
            foreach (var other in Enum.GetValues<SiteKind>())
            {
                if (other == kind || !SpriteSystem.LayerMapTryGet((uid, sprite), SiteKey(site, other), out var existing, false))
                    continue;

                if (other < kind)
                    above = above is { } top ? Math.Max(top, existing) : existing;
                else
                    below = below is { } bottom ? Math.Min(bottom, existing) : existing;
            }

            // An insert at an index goes under the layer that held it and pushes it up.
            var insert = below ?? (above is { } highest ? highest + 1 : (int?) null);
            index = SpriteSystem.AddLayer((uid, sprite), new SpriteSpecifier.Rsi(rsi, state), insert);
            SpriteSystem.LayerMapSet((uid, sprite), key, index);
        }

        SpriteSystem.LayerSetVisible((uid, sprite), index, state != null);
        if (state == null)
            return;

        SpriteSystem.LayerSetRsiState((uid, sprite), index, state);
        SpriteSystem.LayerSetOffset((uid, sprite), index, offset);
        SpriteSystem.LayerSetColor((uid, sprite), index, colour);
    }
    /// <summary>Plays the site's spray once, then returns the layer to the still artery.</summary>
    private void Spray(EntityUid uid, SpriteComponent sprite, string key, WolfmedArterySite site)
    {
        var spraying = WolfmedWoundOverlays.GetArteryState(site, WolfmedArteryOverlay.Bleeding)!;
        var still = WolfmedWoundOverlays.GetArteryState(site, WolfmedArteryOverlay.Still)!;
        if (SpriteSystem.LayerGetEffectiveRsi((uid, sprite), key, spraying) is not { } rsi ||
            !rsi.TryGetState(spraying, out var state))
            return;

        var length = MathF.Max(0.1f, state.AnimationLength);
        if (_wfAnimation.HasRunningAnimation(uid, key))
            _wfAnimation.Stop(uid, key);

        _wfAnimation.Play(uid, new Animation
        {
            Length = TimeSpan.FromSeconds(length + 0.05f),
            AnimationTracks =
            {
                new AnimationTrackSpriteFlick
                {
                    LayerKey = key,
                    KeyFrames =
                    {
                        new AnimationTrackSpriteFlick.KeyFrame(spraying, 0f),
                        new AnimationTrackSpriteFlick.KeyFrame(still, length),
                    },
                },
            },
        }, key);
    }

    /// <summary>Creates (once, appended above every layer the sprite has) and updates one artery layer.</summary>
    private void UpdateTopLayer(EntityUid uid, SpriteComponent sprite, string key, ResPath rsi, string? state,
        Vector2 offset, Color colour)
    {
        if (!SpriteSystem.LayerMapTryGet((uid, sprite), key, out var index, false))
        {
            if (state == null)
                return;

            index = SpriteSystem.AddLayer((uid, sprite), new SpriteSpecifier.Rsi(rsi, state));
            SpriteSystem.LayerMapSet((uid, sprite), key, index);
        }

        SpriteSystem.LayerSetVisible((uid, sprite), index, state != null);
        if (state == null)
            return;

        SpriteSystem.LayerSetRsiState((uid, sprite), index, state);
        SpriteSystem.LayerSetOffset((uid, sprite), index, offset);
        SpriteSystem.LayerSetColor((uid, sprite), index, colour);
    }

    /// <summary>Creates (once) and updates one wound or rot layer, above the limb and the overlays named below it.</summary>
    private void UpdateOverlayLayer(EntityUid uid, SpriteComponent sprite, HumanoidVisualLayers layer, string key,
        ResPath rsi, string? state, Vector2 offset, Color? colour, params string[] below)
    {
        if (!SpriteSystem.LayerMapTryGet((uid, sprite), key, out var index, false))
        {
            if (state == null || !SpriteSystem.LayerMapTryGet((uid, sprite), layer, out var insert, false))
                return;

            foreach (var other in below)
            {
                if (SpriteSystem.LayerMapTryGet((uid, sprite), other, out var under, false))
                    insert = Math.Max(insert, under);
            }

            // Playtest 5: a fur, scale or feather marking is inserted right above its limb and covered the wound on
            // every species that wears one; the wound goes above the limb's markings, still under the clothing.
            insert = Math.Max(insert, MarkingTop(uid, sprite, layer));
            index = SpriteSystem.AddLayer((uid, sprite), new SpriteSpecifier.Rsi(rsi, state), insert + 1);
            SpriteSystem.LayerMapSet((uid, sprite), key, index);
        }
        else if (state != null && MarkingTop(uid, sprite, layer) is var top && top >= index)
        {
            // A marking applied after the wound was drawn landed above it: put the wound back over the fur.
            SpriteSystem.RemoveLayer((uid, sprite), index);
            index = SpriteSystem.AddLayer((uid, sprite), new SpriteSpecifier.Rsi(rsi, state), top);
            SpriteSystem.LayerMapSet((uid, sprite), key, index);
        }

        SpriteSystem.LayerSetVisible((uid, sprite), index, state != null);
        if (state == null)
            return;

        // Re-stating an unchanged state is a no-op, so a drip does not restart on every damage update.
        SpriteSystem.LayerSetRsiState((uid, sprite), index, state);
        SpriteSystem.LayerSetOffset((uid, sprite), index, offset);
        if (colour is { } tint)
            SpriteSystem.LayerSetColor((uid, sprite), index, tint);
    }

    /// <summary>
    /// The highest sprite layer a marking on this limb draws at, or -1 with none. The humanoid appearance inserts a
    /// marking straight above its body part, so a full-body fur or scale marking covers anything put there after it.
    /// </summary>
    public int MarkingTop(EntityUid uid, SpriteComponent sprite, HumanoidVisualLayers layer)
    {
        var top = -1;
        if (!TryComp(uid, out HumanoidAppearanceComponent? humanoid))
            return top;

        foreach (var markings in humanoid.MarkingSet.Markings.Values)
        {
            foreach (var marking in markings)
            {
                if (!_prototypeManager.TryIndex(marking.MarkingId, out MarkingPrototype? prototype) ||
                    prototype.BodyPart != layer)
                    continue;

                foreach (var specifier in prototype.Sprites)
                {
                    if (specifier is SpriteSpecifier.Rsi rsi &&
                        SpriteSystem.LayerMapTryGet((uid, sprite), $"{prototype.ID}-{rsi.RsiState}", out var index, false))
                        top = Math.Max(top, index);
                }
            }
        }

        return top;
    }

    // --- Option B: severed-limb wound rendering (P3-D18) ---

    /// <summary>Adds/updates the detached-part wound layers for one part and hides them once it is reattached.</summary>
    private void UpdateDetachedPartDamage(EntityUid uid, PartDamageVisualsComponent damage)
    {
        if (!TryComp(uid, out BodyPartComponent? part) || !TryComp(uid, out SpriteComponent? sprite))
            return;

        var detached = part.Body == null;
        foreach (var (layer, layerDamage) in damage.Damage)
        {
            if (!TryGetDetachedDamagePrefix(layer, out var prefix))
                continue;

            var groups = layerDamage.GetDamagePerGroup(_prototypeManager);
            UpdateDetachedDamageLayer(uid, sprite, layer, prefix, "Brute", groups.GetValueOrDefault("Brute"), detached);
            UpdateDetachedDamageLayer(uid, sprite, layer, prefix, "Burn", groups.GetValueOrDefault("Burn"), detached);
        }

        if (detached)
            return;

        // Reattached: hide every layer this part could have grown while it was off the body.
        foreach (var layer in Enum.GetValues<HumanoidVisualLayers>())
        {
            SetDetachedDamageLayerVisible(uid, sprite, layer, "Brute", false);
            SetDetachedDamageLayerVisible(uid, sprite, layer, "Burn", false);
        }
    }

    /// <summary>Creates (once) and updates one detached-part damage layer.</summary>
    private void UpdateDetachedDamageLayer(EntityUid uid, SpriteComponent sprite, HumanoidVisualLayers layer,
        string prefix, string group, FixedPoint2 amount, bool detached)
    {
        var threshold = GetDetachedDamageThreshold(amount);
        var key = $"WolfmedDetached{layer}{group}"; // WOLFGATE: layer-map key, no Onyx equivalent to preserve
        if (!SpriteSystem.LayerMapTryGet((uid, sprite), key, out var index, false))
        {
            index = SpriteSystem.AddLayer((uid, sprite), new SpriteSpecifier.Rsi(
                new ResPath($"_Onyx/Wounds/{group.ToLowerInvariant()}_damage.rsi"),
                $"{prefix}_{group}_10"));
            SpriteSystem.LayerMapSet((uid, sprite), key, index);
            if (group == "Brute")
                SpriteSystem.LayerSetColor((uid, sprite), index, Color.FromHex("#FF0000"));
        }

        var visible = detached && threshold > 0;
        SpriteSystem.LayerSetVisible((uid, sprite), index, visible);
        if (visible)
            SpriteSystem.LayerSetRsiState((uid, sprite), index, $"{prefix}_{group}_{threshold}");
    }

    private void SetDetachedDamageLayerVisible(EntityUid uid, SpriteComponent sprite, HumanoidVisualLayers layer, string group, bool visible)
    {
        if (SpriteSystem.LayerMapTryGet((uid, sprite), $"WolfmedDetached{layer}{group}", out var index, false))
            SpriteSystem.LayerSetVisible((uid, sprite), index, visible);
    }

    private static int GetDetachedDamageThreshold(FixedPoint2 amount)
    {
        var value = amount.Float();
        if (value >= 100) return 100;
        if (value >= 70) return 70;
        if (value >= 50) return 50;
        if (value >= 30) return 30;
        if (value >= 20) return 20;
        if (value >= 10) return 10;
        return 0;
    }

    /// <summary>Maps a visual layer to its detached-art state prefix. WOLFGATE: D9 - no HumanoidVisualLayers.Groin in WG; Onyx's Groin arm is dropped.</summary>
    private static bool TryGetDetachedDamagePrefix(HumanoidVisualLayers layer, out string prefix)
    {
        prefix = layer switch
        {
            HumanoidVisualLayers.Chest => "Chest",
            HumanoidVisualLayers.Head => "Head",
            HumanoidVisualLayers.LArm => "LArm",
            HumanoidVisualLayers.RArm => "RArm",
            HumanoidVisualLayers.LHand => "LHand",
            HumanoidVisualLayers.RHand => "RHand",
            HumanoidVisualLayers.LLeg => "LLeg",
            HumanoidVisualLayers.RLeg => "RLeg",
            HumanoidVisualLayers.LFoot => "LFoot",
            HumanoidVisualLayers.RFoot => "RFoot",
            _ => string.Empty,
        };
        return prefix.Length != 0;
    }
}
