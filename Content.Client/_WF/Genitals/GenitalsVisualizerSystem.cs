using System.Numerics;
using Content.Client._Common.Consent;
using Content.Client.Humanoid;
using Content.Shared._Common.Consent;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.Genitals;
using Content.Shared._WF.Genitals.Components;
using Content.Shared._WF.Genitals.Prototypes;
using Content.Shared._WF.Genitals.Systems;
using Content.Shared.Clothing;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Inventory.Events;
using Content.Shared.SSDIndicator;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Client.ResourceManagement;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client._WF.Genitals;

/// <summary>Draws genital organs from GenitalsComponent into fixed keyed layers above three anchor layers.</summary>
/// <remarks>
/// The keyed layers are created once per entity; updates only change RSI, state, colour, offset and visibility. Events
/// queue a body, and FrameUpdate redraws each queued body once.
/// </remarks>
public sealed partial class GenitalsVisualizerSystem : EntitySystem
{
    [Dependency] private IClientConsentManager _consentManager = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IResourceCache _resourceCache = default!;
    [Dependency] private ClientGenitalConsentSystem _consent = default!;
    [Dependency] private GenitalCoverageSystem _coverage = default!;
    [Dependency] private HumanoidAppearanceSystem _humanoid = default!;
    [Dependency] private SharedArousalSystem _arousal = default!;
    [Dependency] private SharedGenitalsSystem _genitals = default!;
    [Dependency] private SpriteSystem _sprite = default!;

    /// <summary>Keyed layers directly above the Behind anchor, bottom to top: north-facing BEHIND art.</summary>
    private static readonly string[] BehindKeys =
    {
        "wf-gen-behind-breasts",
        "wf-gen-behind-testicles",
        "wf-gen-behind-penis",
    };

    /// <summary>Keyed layers directly above the Under anchor, bottom to top, indexed by <see cref="Part"/>.</summary>
    private static readonly string[] UnderKeys =
    {
        "wf-gen-under-vagina",
        "wf-gen-under-testicles",
        "wf-gen-under-breasts",
        "wf-gen-under-sheath-outer",
        "wf-gen-under-sheath-inner",
        "wf-gen-under-penis",
    };

    /// <summary>Keyed layers directly above the Over anchor (show through clothing), indexed by <see cref="Part"/>.</summary>
    private static readonly string[] OverKeys =
    {
        "wf-gen-over-vagina",
        "wf-gen-over-testicles",
        "wf-gen-over-breasts",
        "wf-gen-over-sheath-outer",
        "wf-gen-over-sheath-inner",
        "wf-gen-over-penis",
    };

    private const int BehindBreasts = 0;
    private const int BehindTesticles = 1;
    private const int BehindPenis = 2;

    /// <summary>The parts drawn for the penis slot: the shaft and both sheath layers.</summary>
    private static readonly Part[] PenisParts = { Part.Penis, Part.SheathOuter, Part.SheathInner };

    /// <summary>Prototype and anchor pairs already reported as missing, so each is logged once.</summary>
    private readonly HashSet<string> _warnedAnchors = new();

    /// <summary>
    /// Bodies to redraw at the next frame. State handling, prediction replays and equipment events can each fire several
    /// times a tick, so they only queue the body, and FrameUpdate redraws it once after prediction settles.
    /// </summary>
    private readonly HashSet<EntityUid> _pendingVisuals = new();

    private readonly List<EntityUid> _visualsBatch = new();

    /// <summary>RSI state per shape, logical step, arousal and layer; null where the shape has no art. Cleared on reload.</summary>
    private readonly Dictionary<(string Shape, int Step, bool Aroused, bool Behind), string?> _states = new();

    /// <summary>Sheath art per sheath type; null where none is defined. Cleared on reload.</summary>
    private readonly Dictionary<SheathType, GenitalSheathPrototype?> _sheaths = new();

    /// <summary>Set while this system rebuilds markings, so the event raised by that rebuild does not start another one.</summary>
    private bool _refreshing;

    /// <summary>The kill switch, the viewer or the anatomy prototypes changed; redraw every body next frame.</summary>
    private bool _refreshPending;

    /// <summary>The local entity and its adult gate at the last frame; a change redraws every body (CanViewerSee).</summary>
    private (EntityUid? Entity, bool Adult) _viewer = (null, true);

    /// <summary>Fired after any visual refresh of an entity; the Anatomy panel listens for the local entity.</summary>
    public event Action<EntityUid>? AnatomyChanged;

    /// <summary>Parts on the Under and Over sets; the values index <see cref="UnderKeys"/> and <see cref="OverKeys"/>.</summary>
    private enum Part : byte
    {
        Vagina,
        Testicles,
        Breasts,
        SheathOuter,
        SheathInner,
        Penis,
    }

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GenitalsComponent, AfterAutoHandleStateEvent>(OnAfterState);
        SubscribeLocalEvent<GenitalsComponent, GenitalsVisualsChangedEvent>(OnVisualsChanged);
        SubscribeLocalEvent<GenitalsComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<GenitalsComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<GenitalsComponent, DidEquipEvent>(OnDidEquip);
        SubscribeLocalEvent<GenitalsComponent, DidUnequipEvent>(OnDidUnequip);
        SubscribeLocalEvent<GenitalsComponent, HumanoidMarkingsAppliedEvent>(OnMarkingsApplied);
        SubscribeLocalEvent<EquipmentVisualsUpdatedEvent>(OnEquipmentVisualsUpdated);
        SubscribeLocalEvent<ConsentComponent, AfterAutoHandleStateEvent>(OnConsentState);
        SubscribeLocalEvent<SSDIndicatorComponent, AfterAutoHandleStateEvent>(OnSsdState);
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);

        _consentManager.OnServerDataLoaded += RefreshAll;
        Subs.CVar(_cfg, WolfgateCVars.AnatomyEnabled, _ => _refreshPending = true);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _consentManager.OnServerDataLoaded -= RefreshAll;
        _pendingVisuals.Clear();
    }

    public override void FrameUpdate(float frameTime)
    {
        // Attaching to another body, or the body's age changing, can change the viewer gate for every body.
        var viewer = (_player.LocalEntity, _consent.ViewerIsAdult());
        if (viewer != _viewer)
        {
            _viewer = viewer;
            _refreshPending = true;
        }

        if (_refreshPending)
        {
            _refreshPending = false;
            _pendingVisuals.Clear();
            RefreshAll();
            return;
        }

        if (_pendingVisuals.Count == 0)
            return;

        // Copied first: a redraw that rebuilds markings may queue the body again, for the next frame.
        _visualsBatch.Clear();
        _visualsBatch.AddRange(_pendingVisuals);
        _pendingVisuals.Clear();

        foreach (var uid in _visualsBatch)
        {
            if (!TerminatingOrDeleted(uid) && TryComp<GenitalsComponent>(uid, out var genitals))
                UpdateVisuals(uid, genitals, null);
        }
    }

    /// <summary>Redraws the anatomy of one body for the local viewer now.</summary>
    public void UpdateVisuals(Entity<GenitalsComponent, SpriteComponent?> ent)
    {
        UpdateVisuals(ent.Owner, ent.Comp1, ent.Comp2);
    }

    /// <summary>Redraws every body with anatomy, e.g. after the viewer's consent or the kill switch changed.</summary>
    public void RefreshAll()
    {
        var query = EntityQueryEnumerator<GenitalsComponent, SpriteComponent>();
        while (query.MoveNext(out var uid, out var genitals, out var sprite))
        {
            UpdateVisuals(uid, genitals, sprite);
        }
    }

    /// <summary>
    /// Whether an undergarment marking is drawn as removed for the local viewer: its removal flag is set, or the entity
    /// is a creator doll in Nude mode, and the viewer passes the anatomy gate. Used by the humanoid marking hook.
    /// </summary>
    public bool IsUndergarmentHidden(EntityUid uid, MarkingCategories category)
    {
        if (UndergarmentSlots.SlotOf(category) is not { } slot
            || !TryComp<GenitalsComponent>(uid, out var genitals)
            || genitals.LifeStage > ComponentLifeStage.Running)
            return false;

        return (GetHiddenUndergarments(genitals) & UndergarmentSlots.RemovedFlag(slot)) != 0 && _consent.CanViewerSee(uid);
    }

    private void UpdateVisuals(EntityUid uid, GenitalsComponent genitals, SpriteComponent? sprite)
    {
        if (!Resolve(uid, ref sprite, false))
            return;

        EnsureLayers(uid, sprite);

        // Kill switch and the viewer's consent, then the target's consent and presence.
        var gate = genitals.LifeStage <= ComponentLifeStage.Running && _consent.CanViewerSee(uid);
        if (gate)
            DrawOrgans(uid, genitals, sprite);
        else
            HideAll(uid, sprite);

        UpdateUndergarments(uid, genitals, gate ? GetHiddenUndergarments(genitals) : UndergarmentFlags.None);
        AnatomyChanged?.Invoke(uid);
    }

    private void DrawOrgans(EntityUid uid, GenitalsComponent genitals, SpriteComponent sprite)
    {
        var settings = _genitals.Settings;
        var ent = new Entity<GenitalsComponent>(uid, genitals);
        TryComp<HumanoidAppearanceComponent>(uid, out var humanoid);

        // Coverage once for all slots.
        var coverage = _coverage.GetCoverage(new Entity<GenitalsComponent, HumanoidAppearanceComponent?>(uid, genitals, humanoid));
        var arousal = genitals.PreviewArousal ?? _arousal.GetState((uid, genitals));

        Dictionary<GenitalRegion, Vector2i>? offsets = null;
        if (humanoid != null)
            settings.SpeciesOffsets.TryGetValue(humanoid.Species, out offsets);

        DrawOrgan(uid, sprite, ent, GenitalSlot.Breasts, genitals.Breasts, Part.Breasts, BehindKeys[BehindBreasts],
            coverage, false, GetOffset(ent, GenitalSlot.Breasts, offsets));
        DrawOrgan(uid, sprite, ent, GenitalSlot.Testicles, genitals.Testicles, Part.Testicles, BehindKeys[BehindTesticles],
            coverage, false, GetOffset(ent, GenitalSlot.Testicles, offsets));
        DrawOrgan(uid, sprite, ent, GenitalSlot.Vagina, genitals.Vagina, Part.Vagina, null,
            coverage, GenitalSpriteResolver.UsesArousedArt(GenitalSlot.Vagina, arousal, settings), GetOffset(ent, GenitalSlot.Vagina, offsets));
        DrawPenis(uid, sprite, ent, coverage, arousal, settings, GetOffset(ent, GenitalSlot.Penis, offsets));
    }

    /// <summary>One organ on its FRONT layer (Under or Over) plus, where the shape has the art, its BEHIND layer.</summary>
    private void DrawOrgan(EntityUid uid, SpriteComponent sprite, Entity<GenitalsComponent> ent, GenitalSlot slot,
        GenitalOrganState? state, Part part, string? behindKey, in GenitalCoverage coverage, bool aroused, Vector2 offset)
    {
        var exposure = _coverage.GetExposure(ent, slot, coverage);
        if (!exposure.Exposed
            || state is not { } organ
            || organ.Shape is not { } shapeId
            || !_proto.TryIndex(shapeId, out var shape))
        {
            HideLayer(uid, sprite, UnderKeys[(int) part]);
            HideLayer(uid, sprite, OverKeys[(int) part]);
            if (behindKey != null)
                HideLayer(uid, sprite, behindKey);
            return;
        }

        HideLayer(uid, sprite, Key(Other(exposure.Layer), part));
        ShowShape(uid, sprite, Key(exposure.Layer, part), shape, organ.Step, aroused, false, organ.Color, offset);
        if (behindKey != null)
            ShowShape(uid, sprite, behindKey, shape, organ.Step, aroused, true, organ.Color, offset);
    }

    /// <summary>The penis and its sheath or slit. The sheath outer takes the sheath colour, every other layer the penis colour.</summary>
    private void DrawPenis(EntityUid uid, SpriteComponent sprite, Entity<GenitalsComponent> ent, in GenitalCoverage coverage,
        ArousalState arousal, GenitalSettingsPrototype settings, Vector2 offset)
    {
        var exposure = _coverage.GetExposure(ent, GenitalSlot.Penis, coverage);
        if (!exposure.Exposed
            || ent.Comp.Penis is not { } organ
            || organ.Shape is not { } shapeId
            || !_proto.TryIndex(shapeId, out var shape))
        {
            foreach (var part in PenisParts)
            {
                HideLayer(uid, sprite, UnderKeys[(int) part]);
                HideLayer(uid, sprite, OverKeys[(int) part]);
            }

            HideLayer(uid, sprite, BehindKeys[BehindPenis]);
            return;
        }

        var set = exposure.Layer;
        foreach (var part in PenisParts)
        {
            HideLayer(uid, sprite, Key(Other(set), part));
        }

        var erect = GenitalSpriteResolver.UsesArousedArt(GenitalSlot.Penis, arousal, settings);
        var sheath = organ.Sheath == SheathType.None ? null : FindSheath(organ.Sheath);

        if (sheath != null && !erect)
        {
            // Retracted or emerging: the sheath or slit shows and the shaft stays hidden.
            HideLayer(uid, sprite, Key(set, Part.Penis));
            HideLayer(uid, sprite, BehindKeys[BehindPenis]);
            var (outer, inner) = GenitalSpriteResolver.GetSheathStates(sheath, arousal);
            SetLayer(uid, sprite, Key(set, Part.SheathOuter), sheath.Sprite, outer, organ.SheathColor, offset);
            SetLayer(uid, sprite, Key(set, Part.SheathInner), sheath.Sprite, inner, organ.Color, offset);
            return;
        }

        ShowShape(uid, sprite, Key(set, Part.Penis), shape, organ.Step, erect, false, organ.Color, offset);
        ShowShape(uid, sprite, BehindKeys[BehindPenis], shape, organ.Step, erect, true, organ.Color, offset);

        // Erect: a sheath stays visible at the base (ErectOuter); a slit is not drawn.
        if (sheath != null && sheath.ErectOuter is { } erectOuter)
            SetLayer(uid, sprite, Key(set, Part.SheathOuter), sheath.Sprite, erectOuter, organ.SheathColor, offset);
        else
            HideLayer(uid, sprite, Key(set, Part.SheathOuter));

        HideLayer(uid, sprite, Key(set, Part.SheathInner));
    }

    /// <summary>The sheath art for a sheath type, or null if none is defined.</summary>
    private GenitalSheathPrototype? FindSheath(SheathType type)
    {
        if (_sheaths.TryGetValue(type, out var cached))
            return cached;

        GenitalSheathPrototype? found = null;
        foreach (var sheath in _proto.EnumeratePrototypes<GenitalSheathPrototype>())
        {
            if (sheath.Type != type)
                continue;

            found = sheath;
            break;
        }

        _sheaths[type] = found;
        return found;
    }

    /// <summary>Species pixel offset for the region of the slot, in world units (+y is up).</summary>
    private Vector2 GetOffset(Entity<GenitalsComponent> ent, GenitalSlot slot, Dictionary<GenitalRegion, Vector2i>? offsets)
    {
        if (offsets == null || !offsets.TryGetValue(_coverage.GetRegion(ent, slot), out var pixels))
            return Vector2.Zero;

        return new Vector2(pixels.X, pixels.Y) / EyeManager.PixelsPerMeter;
    }

    private void ShowShape(EntityUid uid, SpriteComponent sprite, string key, GenitalShapePrototype shape, int step,
        bool aroused, bool behind, Color color, Vector2 offset)
    {
        if (GetState(shape, step, aroused, behind) is { } state)
            SetLayer(uid, sprite, key, shape.Sprite, state, color, offset);
        else
            HideLayer(uid, sprite, key);
    }

    /// <summary>The RSI state of a shape at a step, arousal and layer; null when the shape has no art for it. Cached.</summary>
    private string? GetState(GenitalShapePrototype shape, int step, bool aroused, bool behind)
    {
        var key = (shape.ID, step, aroused, behind);
        if (!_states.TryGetValue(key, out var state))
        {
            GenitalSpriteResolver.TryGetState(shape, step, aroused, behind, out state);
            _states[key] = state;
        }

        return state;
    }

    /// <summary>Shows a keyed layer with the given art; hides it when the state is null or missing from the RSI.</summary>
    private void SetLayer(EntityUid uid, SpriteComponent sprite, string key, ResPath rsiPath, string? state, Color color, Vector2 offset)
    {
        if (!_sprite.TryGetLayer((uid, sprite), key, out var layer, false))
            return;

        if (state == null
            || !_resourceCache.TryGetResource<RSIResource>(GenitalSpriteResolver.RsiPath(rsiPath), out var rsi)
            || !rsi.RSI.TryGetState(state, out _))
        {
            _sprite.LayerSetVisible(layer, false);
            return;
        }

        if (layer.RSI != rsi.RSI || layer.State.Name != state)
            _sprite.LayerSetRsi(layer, rsi.RSI, new RSI.StateId(state));

        _sprite.LayerSetColor(layer, color);
        _sprite.LayerSetOffset(layer, offset);
        _sprite.LayerSetVisible(layer, true);
    }

    private void HideLayer(EntityUid uid, SpriteComponent sprite, string key)
    {
        if (_sprite.TryGetLayer((uid, sprite), key, out var layer, false))
            _sprite.LayerSetVisible(layer, false);
    }

    private void HideAll(EntityUid uid, SpriteComponent sprite)
    {
        foreach (var key in BehindKeys)
        {
            HideLayer(uid, sprite, key);
        }

        foreach (var key in UnderKeys)
        {
            HideLayer(uid, sprite, key);
        }

        foreach (var key in OverKeys)
        {
            HideLayer(uid, sprite, key);
        }
    }

    private static string Key(GenitalLayerSet set, Part part)
    {
        return set == GenitalLayerSet.Over ? OverKeys[(int) part] : UnderKeys[(int) part];
    }

    private static GenitalLayerSet Other(GenitalLayerSet set)
    {
        return set == GenitalLayerSet.Over ? GenitalLayerSet.Under : GenitalLayerSet.Over;
    }

    /// <summary>Creates the keyed layers directly above each anchor, once. A missing anchor draws nothing for its set.</summary>
    private void EnsureLayers(EntityUid uid, SpriteComponent sprite)
    {
        EnsureSet(uid, sprite, GenitalVisualLayers.Behind, BehindKeys);
        EnsureSet(uid, sprite, GenitalVisualLayers.Under, UnderKeys);
        EnsureSet(uid, sprite, GenitalVisualLayers.Over, OverKeys);
    }

    private void EnsureSet(EntityUid uid, SpriteComponent sprite, GenitalVisualLayers anchor, string[] keys)
    {
        var complete = true;
        foreach (var key in keys)
        {
            if (_sprite.LayerMapTryGet((uid, sprite), key, out _, false))
                continue;

            complete = false;
            break;
        }

        if (complete)
            return;

        if (!_sprite.LayerMapTryGet((uid, sprite), anchor, out var below, false))
        {
            WarnMissingAnchor(uid, anchor);
            return;
        }

        // Each key goes directly above the previous one, so the table order is kept.
        foreach (var key in keys)
        {
            if (_sprite.LayerMapTryGet((uid, sprite), key, out var existing, false))
            {
                below = existing;
                continue;
            }

            below++;
            var layer = _sprite.AddBlankLayer((uid, sprite), below);
            _sprite.LayerMapSet((uid, sprite), key, below);
            _sprite.LayerSetVisible(layer, false);
        }
    }

    private void WarnMissingAnchor(EntityUid uid, GenitalVisualLayers anchor)
    {
        var prototype = MetaData(uid).EntityPrototype?.ID ?? "(no prototype)";
        if (_warnedAnchors.Add($"{prototype}/{anchor}"))
            Log.Warning($"Entity prototype {prototype} has no {anchor} anchor layer; anatomy on that layer set is not drawn.");
    }

    /// <summary>Rebuilds the markings when the undergarments this viewer sees as removed change.</summary>
    private void UpdateUndergarments(EntityUid uid, GenitalsComponent genitals, UndergarmentFlags hidden)
    {
        if (hidden == genitals.LastHiddenUndergarments)
            return;

        genitals.LastHiddenUndergarments = hidden;

        // A rebuild in progress asks IsUndergarmentHidden itself, so it draws this value already.
        if (!_refreshing)
            RebuildMarkings(uid);
    }

    private void RebuildMarkings(EntityUid uid)
    {
        _refreshing = true;
        try
        {
            _humanoid.RefreshMarkings(uid);
        }
        finally
        {
            _refreshing = false;
        }
    }

    /// <summary>Removal flags as drawn: the real flags, or both for a creator doll in Nude mode. The caller applies the gate.</summary>
    private static UndergarmentFlags GetHiddenUndergarments(GenitalsComponent genitals)
    {
        var nude = genitals.IsPreview && genitals.PreviewMode == GenitalPreviewMode.Nude;
        var hidden = UndergarmentFlags.None;

        if (nude || (genitals.Undergarments & UndergarmentFlags.TopRemoved) != 0)
            hidden |= UndergarmentFlags.TopRemoved;

        if (nude || (genitals.Undergarments & UndergarmentFlags.BottomRemoved) != 0)
            hidden |= UndergarmentFlags.BottomRemoved;

        return hidden;
    }

    /// <summary>Redraws the body at the next frame.</summary>
    private void QueueVisuals(EntityUid uid)
    {
        _pendingVisuals.Add(uid);
    }

    /// <summary>Server state, also re-applied at every prediction reset: records the confirmed arousal and queues a redraw.</summary>
    private void OnAfterState(Entity<GenitalsComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        ent.Comp.ConfirmedArousal = ent.Comp.Arousal;
        QueueVisuals(ent.Owner);
    }

    private void OnVisualsChanged(Entity<GenitalsComponent> ent, ref GenitalsVisualsChangedEvent args)
    {
        QueueVisuals(ent.Owner);
    }

    private void OnStartup(Entity<GenitalsComponent> ent, ref ComponentStartup args)
    {
        QueueVisuals(ent.Owner);
    }

    /// <summary>Hides every anatomy layer and gives the undergarments back.</summary>
    private void OnShutdown(Entity<GenitalsComponent> ent, ref ComponentShutdown args)
    {
        _pendingVisuals.Remove(ent.Owner);
        if (TerminatingOrDeleted(ent.Owner))
            return;

        if (TryComp<SpriteComponent>(ent.Owner, out var sprite))
            HideAll(ent.Owner, sprite);

        if (ent.Comp.LastHiddenUndergarments != UndergarmentFlags.None)
        {
            ent.Comp.LastHiddenUndergarments = UndergarmentFlags.None;
            if (!_refreshing)
                RebuildMarkings(ent.Owner);
        }

        AnatomyChanged?.Invoke(ent.Owner);
    }

    private void OnDidEquip(Entity<GenitalsComponent> ent, ref DidEquipEvent args)
    {
        QueueVisuals(ent.Owner);
    }

    private void OnDidUnequip(Entity<GenitalsComponent> ent, ref DidUnequipEvent args)
    {
        QueueVisuals(ent.Owner);
    }

    /// <summary>A marking rebuild asked IsUndergarmentHidden, so it drew the current removals; the anatomy is redrawn next frame.</summary>
    private void OnMarkingsApplied(Entity<GenitalsComponent> ent, ref HumanoidMarkingsAppliedEvent args)
    {
        if (_refreshing)
            return;

        ent.Comp.LastHiddenUndergarments = ent.Comp.LifeStage <= ComponentLifeStage.Running && _consent.CanViewerSee(ent.Owner)
            ? GetHiddenUndergarments(ent.Comp)
            : UndergarmentFlags.None;
        QueueVisuals(ent.Owner);
    }

    /// <summary>Raised on the item after every equipment render, including a coat folded open or closed.</summary>
    private void OnEquipmentVisualsUpdated(EquipmentVisualsUpdatedEvent args)
    {
        if (HasComp<GenitalsComponent>(args.Equipee))
            QueueVisuals(args.Equipee);
    }

    /// <summary>Another player's consent changed (ConsentComponent raises AfterAutoHandleState).</summary>
    private void OnConsentState(Entity<ConsentComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        if (HasComp<GenitalsComponent>(ent.Owner))
            QueueVisuals(ent.Owner);
    }

    /// <summary>Another player disconnected or reconnected (SSDIndicatorComponent raises AfterAutoHandleState).</summary>
    private void OnSsdState(Entity<SSDIndicatorComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        if (HasComp<GenitalsComponent>(ent.Owner))
            QueueVisuals(ent.Owner);
    }

    /// <summary>Shape or sheath data changed: drops the caches and redraws every body.</summary>
    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (!args.WasModified<GenitalShapePrototype>() && !args.WasModified<GenitalSheathPrototype>())
            return;

        _states.Clear();
        _sheaths.Clear();
        _refreshPending = true;
    }
}
