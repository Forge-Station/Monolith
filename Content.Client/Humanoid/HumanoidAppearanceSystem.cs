using System.Numerics;
using Content.Client._WF.Genitals; // WOLFGATE
using Content.Shared._WF.Genitals; // WOLFGATE
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Inventory;
using Content.Shared.Preferences;
using Robust.Client.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client.Humanoid;

public sealed partial class HumanoidAppearanceSystem : SharedHumanoidAppearanceSystem
{
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private MarkingManager _markingManager = default!;
    [Dependency] private GenitalsVisualizerSystem _genitalsVisuals = default!; // WOLFGATE - anatomy undergarment hiding

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<HumanoidAppearanceComponent, AfterAutoHandleStateEvent>(OnHandleState);
    }

    private void OnHandleState(EntityUid uid, HumanoidAppearanceComponent component, ref AfterAutoHandleStateEvent args)
    {
        UpdateSprite(uid, component, Comp<SpriteComponent>(uid)); // WOLFGATE - entity id threaded through (Component.Owner is obsolete)
    }

    private void UpdateSprite(EntityUid uid, HumanoidAppearanceComponent component, SpriteComponent sprite) // WOLFGATE - entity id for the anatomy hooks
    {
        UpdateLayers(component, sprite);
        ApplyMarkingSet(uid, component, sprite); // WOLFGATE

        sprite[sprite.LayerMapReserveBlank(HumanoidVisualLayers.Eyes)].Color = component.EyeColor;

        // WOLFGATE - anatomy visuals follow marking rebuilds (GenitalsVisualizerSystem).
        RaiseLocalEvent(uid, new HumanoidMarkingsAppliedEvent());
    }

    /// <summary>
    /// WOLFGATE - re-runs the sprite and marking rebuild, e.g. when the undergarments a viewer sees as removed change.
    /// </summary>
    public void RefreshMarkings(EntityUid uid)
    {
        if (TryComp<HumanoidAppearanceComponent>(uid, out var humanoid) && TryComp<SpriteComponent>(uid, out var sprite))
            UpdateSprite(uid, humanoid, sprite);
    }

    private static bool IsHidden(HumanoidAppearanceComponent humanoid, HumanoidVisualLayers layer)
        => humanoid.HiddenLayers.ContainsKey(layer) || humanoid.PermanentlyHidden.Contains(layer);

    private void UpdateLayers(HumanoidAppearanceComponent component, SpriteComponent sprite)
    {
        var oldLayers = new HashSet<HumanoidVisualLayers>(component.BaseLayers.Keys);
        component.BaseLayers.Clear();

        // add default species layers
        var speciesProto = _prototypeManager.Index(component.Species);
        var baseSprites = _prototypeManager.Index<HumanoidSpeciesBaseSpritesPrototype>(speciesProto.SpriteSet);
        foreach (var (key, id) in baseSprites.Sprites)
        {
            oldLayers.Remove(key);
            if (!component.CustomBaseLayers.ContainsKey(key))
                SetLayerData(component, sprite, key, id, sexMorph: true);
        }

        // add custom layers
        foreach (var (key, info) in component.CustomBaseLayers)
        {
            oldLayers.Remove(key);
            // Shitmed Change: For whatever reason these weren't actually ignoring the skin color as advertised.
            SetLayerData(component, sprite, key, info.Id, sexMorph: false, color: info.Color, overrideSkin: true);
        }

        // hide old layers
        // TODO maybe just remove them altogether?
        foreach (var key in oldLayers)
        {
            if (sprite.LayerMapTryGet(key, out var index))
                sprite[index].Visible = false;
        }
    }

    private void SetLayerData(
        HumanoidAppearanceComponent component,
        SpriteComponent sprite,
        HumanoidVisualLayers key,
        string? protoId,
        bool sexMorph = false,
        Color? color = null,
        bool overrideSkin = false) // Shitmed Change
    {
        var layerIndex = sprite.LayerMapReserveBlank(key);
        var layer = sprite[layerIndex];
        layer.Visible = !IsHidden(component, key);

        if (color != null)
            layer.Color = color.Value;

        if (protoId == null)
            return;

        if (sexMorph)
            protoId = HumanoidVisualLayersExtension.GetSexMorph(key, component.Sex, protoId);

        var proto = _prototypeManager.Index<HumanoidSpeciesSpriteLayer>(protoId);
        component.BaseLayers[key] = proto;

        if (proto.MatchSkin && !overrideSkin) // Shitmed Change
            layer.Color = component.SkinColor.WithAlpha(proto.LayerAlpha);

        if (proto.BaseSprite != null)
            sprite.LayerSetSprite(layerIndex, proto.BaseSprite);
    }

    /// <summary>
    ///     Loads a profile directly into a humanoid.
    /// </summary>
    /// <param name="uid">The humanoid entity's UID</param>
    /// <param name="profile">The profile to load.</param>
    /// <param name="humanoid">The humanoid entity's humanoid component.</param>
    /// <remarks>
    ///     This should not be used if the entity is owned by the server. The server will otherwise
    ///     override this with the appearance data it sends over.
    /// </remarks>
    public override void LoadProfile(EntityUid uid, HumanoidCharacterProfile? profile, HumanoidAppearanceComponent? humanoid = null)
    {
        if (profile == null)
            return;

        if (!Resolve(uid, ref humanoid))
        {
            return;
        }

        var customBaseLayers = new Dictionary<HumanoidVisualLayers, CustomBaseLayerInfo>();

        var speciesPrototype = _prototypeManager.Index<SpeciesPrototype>(profile.Species);
        var markings = new MarkingSet(speciesPrototype.MarkingPoints, _markingManager, _prototypeManager);

        // Add markings that doesn't need coloring. We store them until we add all other markings that doesn't need it.
        var markingFColored = new Dictionary<Marking, MarkingPrototype>();
        foreach (var marking in profile.Appearance.Markings)
        {
            if (_markingManager.TryGetMarking(marking, out var prototype))
            {
                if (!prototype.ForcedColoring)
                {
                    markings.AddBack(prototype.MarkingCategory, marking);
                }
                else
                {
                    markingFColored.Add(marking, prototype);
                }
            }
        }

        // legacy: remove in the future?
        //markings.RemoveCategory(MarkingCategories.Hair);
        //markings.RemoveCategory(MarkingCategories.FacialHair);

        // We need to ensure hair before applying it or coloring can try depend on markings that can be invalid
        var hairColor = _markingManager.MustMatchSkin(profile.Species, HumanoidVisualLayers.Hair, out var hairAlpha, _prototypeManager)
            ? profile.Appearance.SkinColor.WithAlpha(hairAlpha)
            : profile.Appearance.HairColor;
        var hair = new Marking(profile.Appearance.HairStyleId,
            new[] { hairColor });

        var facialHairColor = _markingManager.MustMatchSkin(profile.Species, HumanoidVisualLayers.FacialHair, out var facialHairAlpha, _prototypeManager)
            ? profile.Appearance.SkinColor.WithAlpha(facialHairAlpha)
            : profile.Appearance.FacialHairColor;
        var facialHair = new Marking(profile.Appearance.FacialHairStyleId,
            new[] { facialHairColor });

        if (_markingManager.CanBeApplied(profile.Species, profile.Sex, hair, _prototypeManager))
        {
            markings.AddBack(MarkingCategories.Hair, hair);
        }
        if (_markingManager.CanBeApplied(profile.Species, profile.Sex, facialHair, _prototypeManager))
        {
            markings.AddBack(MarkingCategories.FacialHair, facialHair);
        }

        // Finally adding marking with forced colors
        foreach (var (marking, prototype) in markingFColored)
        {
            var markingColors = MarkingColoring.GetMarkingLayerColors(
                prototype,
                profile.Appearance.SkinColor,
                profile.Appearance.EyeColor,
                markings
            );
            markings.AddBack(prototype.MarkingCategory, new Marking(marking.MarkingId, markingColors));
        }

        markings.EnsureSpecies(profile.Species, profile.Appearance.SkinColor, _markingManager, _prototypeManager);
        markings.EnsureSexes(profile.Sex, _markingManager);
        markings.EnsureDefault(
            profile.Appearance.SkinColor,
            profile.Appearance.EyeColor,
            _markingManager);

        DebugTools.Assert(IsClientSide(uid));

        humanoid.MarkingSet = markings;
        humanoid.PermanentlyHidden = new HashSet<HumanoidVisualLayers>();
        humanoid.HiddenLayers = new Dictionary<HumanoidVisualLayers, SlotFlags>();
        humanoid.CustomBaseLayers = customBaseLayers;
        humanoid.Sex = profile.Sex;
        humanoid.Gender = profile.Gender;
        humanoid.Age = profile.Age;
        humanoid.Species = profile.Species;
        humanoid.SkinColor = profile.Appearance.SkinColor;
        humanoid.EyeColor = profile.Appearance.EyeColor;
        humanoid.Height = profile.Appearance.Height;
        humanoid.Width = profile.Appearance.Width;

        // Apply scaling for client-side preview (width, height)
        var sprite = Comp<SpriteComponent>(uid);
        // Check to prevent sprite scale errors for old profiles
        var width = profile.Appearance.Width <= 0.005f ? 1.0f : profile.Appearance.Width;
        var height = profile.Appearance.Height <= 0.005f ? 1.0f : profile.Appearance.Height;
        sprite.Scale = new Vector2(width, height);

        UpdateSprite(uid, humanoid, Comp<SpriteComponent>(uid)); // WOLFGATE

        // WOLFGATE - lets GenitalPreviewSystem fill the doll's anatomy from the profile.
        RaiseLocalEvent(uid, new GenitalPreviewProfileLoadedEvent(profile));
    }

    private void ApplyMarkingSet(EntityUid uid, HumanoidAppearanceComponent humanoid, SpriteComponent sprite) // WOLFGATE - entity id for the anatomy hooks
    {
        // I am lazy and I CBF resolving the previous mess, so I'm just going to nuke the markings.
        // Really, markings should probably be a separate component altogether.
        ClearAllMarkings(humanoid, sprite);

        foreach (var markingList in humanoid.MarkingSet.Markings.Values)
        {
            foreach (var marking in markingList)
            {
                if (_markingManager.TryGetMarking(marking, out var markingPrototype))
                    ApplyMarking(uid, markingPrototype, marking.MarkingColors, marking.Visible, humanoid, sprite); // WOLFGATE
            }
        }

        humanoid.ClientOldMarkings = new MarkingSet(humanoid.MarkingSet);
    }

    private void ClearAllMarkings(HumanoidAppearanceComponent humanoid, SpriteComponent sprite)
    {
        foreach (var markingList in humanoid.ClientOldMarkings.Markings.Values)
        {
            foreach (var marking in markingList)
            {
                RemoveMarking(marking, sprite);
            }
        }

        humanoid.ClientOldMarkings.Clear();

        foreach (var markingList in humanoid.MarkingSet.Markings.Values)
        {
            foreach (var marking in markingList)
            {
                RemoveMarking(marking, sprite);
            }
        }
    }

    private void RemoveMarking(Marking marking, SpriteComponent spriteComp)
    {
        if (!_markingManager.TryGetMarking(marking, out var prototype))
        {
            return;
        }

        foreach (var sprite in prototype.Sprites)
        {
            if (sprite is not SpriteSpecifier.Rsi rsi)
            {
                continue;
            }

            var layerId = $"{marking.MarkingId}-{rsi.RsiState}";
            if (!spriteComp.LayerMapTryGet(layerId, out var index))
            {
                continue;
            }

            spriteComp.LayerMapRemove(layerId);
            spriteComp.RemoveLayer(index);
        }
    }
    private void ApplyMarking(EntityUid uid, // WOLFGATE - entity id for the anatomy undergarment check
        MarkingPrototype markingPrototype,
        IReadOnlyList<Color>? colors,
        bool visible,
        HumanoidAppearanceComponent humanoid,
        SpriteComponent sprite)
    {
        if (!sprite.LayerMapTryGet(markingPrototype.BodyPart, out int _))
        {
            return;
        }

        visible &= !IsHidden(humanoid, markingPrototype.BodyPart);
        visible &= humanoid.BaseLayers.TryGetValue(markingPrototype.BodyPart, out var setting)
           && setting.AllowsMarkings;

        // WOLFGATE - anatomy: a removed undergarment is not drawn for viewers who pass the anatomy gate.
        if (_genitalsVisuals.IsUndergarmentHidden(uid, markingPrototype.MarkingCategory))
            visible = false;

        // WOLFGATE - ported from HardLight/Floof: resolve per-sprite colours through colorLinks so a
        // multi-sprite marking (e.g. a tail split across two layers) is coloured as one unit.
        var linkedColors = ResolveMarkingColors(markingPrototype, colors);
        // Sprites of one marking may land in different layers; count within each layer separately so
        // ordering is unchanged for ordinary single-layer markings.
        var slotOffsets = new Dictionary<HumanoidVisualLayers, int>();
        // End WOLFGATE

        for (var j = 0; j < markingPrototype.Sprites.Count; j++)
        {
            var markingSprite = markingPrototype.Sprites[j];

            if (markingSprite is not SpriteSpecifier.Rsi rsi)
            {
                continue;
            }

            var layerId = $"{markingPrototype.ID}-{rsi.RsiState}";

            // WOLFGATE - a marking may redirect individual sprites into other layers.
            var layerSlot = markingPrototype.BodyPart;
            if (markingPrototype.Layering != null
                && markingPrototype.Layering.TryGetValue(rsi.RsiState, out var layerName)
                && Enum.TryParse<HumanoidVisualLayers>(layerName, out var parsedSlot))
            {
                layerSlot = parsedSlot;
            }

            if (!sprite.LayerMapTryGet(layerSlot, out var targetLayer))
            {
                continue;
            }

            var slotOffset = slotOffsets.TryGetValue(layerSlot, out var o) ? o : 0;
            slotOffsets[layerSlot] = slotOffset + 1;
            // End WOLFGATE

            if (!sprite.LayerMapTryGet(layerId, out _))
            {
                var layer = sprite.AddLayer(markingSprite, targetLayer + slotOffset + 1);
                sprite.LayerMapSet(layerId, layer);
                sprite.LayerSetSprite(layerId, rsi);
            }
		    // impstation edit begin - check if there's a shader defined in the markingPrototype's shader datafield, and if there is...
			if (markingPrototype.Shader != null)
			{
			// use spriteComponent's layersetshader function to set the layer's shader to that which is specified.
				sprite.LayerSetShader(layerId, markingPrototype.Shader);
			}
			// impstation edit end
            sprite.LayerSetVisible(layerId, visible);

            if (!visible || setting == null) // this is kinda implied
            {
                continue;
            }

            // Okay so if the marking prototype is modified but we load old marking data this may no longer be valid
            // and we need to check the index is correct.
            // So if that happens just default to white?
            // WOLFGATE - colours come from the colorLinks-resolved list rather than straight from colors.
            if (linkedColors != null && j < linkedColors.Count)
            {
                sprite.LayerSetColor(layerId, linkedColors[j]);
            }
            else
            {
                sprite.LayerSetColor(layerId, Color.White);
            }
            // End WOLFGATE
        }
    }

    /// <summary>
    /// WOLFGATE - ported from HardLight/Floof. Returns the per-sprite colours for a marking with
    /// <see cref="MarkingPrototype.ColorLinks"/> applied, so linked sprites take their parent's
    /// colour. Returns <paramref name="colors"/> unchanged when the marking has no links.
    /// </summary>
    private static IReadOnlyList<Color>? ResolveMarkingColors(MarkingPrototype prototype, IReadOnlyList<Color>? colors)
    {
        if (prototype.ColorLinks is not { Count: > 0 } || colors == null)
            return colors;

        return prototype.ResolveLinkedColors(colors);
    }

    public override void SetSkinColor(EntityUid uid, Color skinColor, bool sync = true, bool verify = true, HumanoidAppearanceComponent? humanoid = null)
    {
        if (!Resolve(uid, ref humanoid) || humanoid.SkinColor == skinColor)
            return;

        base.SetSkinColor(uid, skinColor, false, verify, humanoid);

        if (!TryComp(uid, out SpriteComponent? sprite))
            return;

        foreach (var (layer, spriteInfo) in humanoid.BaseLayers)
        {
            if (!spriteInfo.MatchSkin)
                continue;

            var index = sprite.LayerMapReserveBlank(layer);
            sprite[index].Color = skinColor.WithAlpha(spriteInfo.LayerAlpha);
        }
    }

    public override void SetLayerVisibility(
        Entity<HumanoidAppearanceComponent> ent,
        HumanoidVisualLayers layer,
        bool visible,
        SlotFlags? slot,
        ref bool dirty)
    {
        base.SetLayerVisibility(ent, layer, visible, slot, ref dirty);

        var sprite = Comp<SpriteComponent>(ent);
        if (!sprite.LayerMapTryGet(layer, out var index))
        {
            if (!visible)
                return;
            index = sprite.LayerMapReserveBlank(layer);
        }

        var spriteLayer = sprite[index];
        if (spriteLayer.Visible == visible)
            return;

        spriteLayer.Visible = visible;

        // I fucking hate this. I'll get around to refactoring sprite layers eventually I swear
        // Just a week away...

        foreach (var markingList in ent.Comp.MarkingSet.Markings.Values)
        {
            foreach (var marking in markingList)
            {
                if (_markingManager.TryGetMarking(marking, out var markingPrototype) && markingPrototype.BodyPart == layer)
                    ApplyMarking(ent.Owner, markingPrototype, marking.MarkingColors, marking.Visible, ent, sprite); // WOLFGATE
            }
        }
    }
}
