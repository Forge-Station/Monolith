using Content.Server._WF.Wolfmed.Gore;
using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared._WF.Wolfmed.Damage;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Humanoid;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.Wolfmed.Damage;

/// <summary>
/// VISUALS: the open wound and the rot on each organic limb, for the humanoid sprite, and the blood colour the wound
/// is tinted with. Rides <see cref="PartDamageVisualsComponent"/> beside the degradation stage and the treatment.
/// </summary>
/// <remarks>
/// A new wound or a bleed change refreshes its body on the next tick. What raises nothing (an infection reaching
/// Septic, a wound closing, a drug stopping a bleed) is caught by a sweep every wolfmed.overlay_refresh_seconds.
/// Machines keep their chassis visuals: a mechanical part never shows either overlay.
/// </remarks>
public sealed class WolfmedWoundOverlaySystem : EntitySystem
{
    /// <summary>What a wound is tinted when its body has no blood reagent to read.</summary>
    private static readonly Color FallbackBlood = Color.FromHex("#800000");

    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private WolfmedGoreSystem _gore = default!;
    [Dependency] private WolfmedWoundTraitSystem _traits = default!;
    [Dependency] private WoundDamageProjectionSystem _projection = default!;
    [Dependency] private WoundSystem _wounds = default!;

    private readonly HashSet<EntityUid> _pending = new();
    private readonly Dictionary<HumanoidVisualLayers, WolfmedWoundOverlay> _woundScratch = new();
    private readonly HashSet<HumanoidVisualLayers> _rotScratch = new();
    private readonly Dictionary<WolfmedArterySite, WolfmedArteryOverlay> _arteryScratch = new();
    private readonly Dictionary<WolfmedArterySite, WolfmedWoundOverlay> _stumpScratch = new();
    private readonly HashSet<(BodyPartType, BodyPartSymmetry)> _presentScratch = new();
    private TimeSpan _nextSweep;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WolfmedWoundLifecycleEvent>(OnWoundLifecycle);
        // Free pair: the treatment overlay holds this event on WoundComponent, Onyx raises it and subscribes nothing.
        SubscribeLocalEvent<WoundBleedingComponent, WoundBleedingChangedEvent>(OnBleedingChanged);
        SubscribeLocalEvent<WolfmedBleedSpurtEvent>(OnBleedSpurt);
    }

    /// <summary>
    /// A blood spurt stamps the spray time when an artery on the sprite is spurting, so the client plays the spray. The
    /// looks are recomputed first: a part put back on or a bleed that ran out only reaches the sweep, and the stamp must
    /// go out with the looks it was decided on.
    /// </summary>
    private void OnBleedSpurt(ref WolfmedBleedSpurtEvent args)
    {
        Refresh(args.Body);
        if (!TryComp(args.Body, out PartDamageVisualsComponent? visual))
            return;

        foreach (var look in visual.Arteries.Values)
        {
            if (look != WolfmedArteryOverlay.Bleeding)
                continue;

            visual.ArterySprayAt = _timing.CurTime;
            Dirty(args.Body, visual);
            return;
        }
    }

    private void OnWoundLifecycle(ref WolfmedWoundLifecycleEvent args) => Queue(args.Part);

    private void OnBleedingChanged(Entity<WoundBleedingComponent> wound, ref WoundBleedingChangedEvent args) =>
        Queue(args.Part);

    private void Queue(EntityUid part)
    {
        if (!TerminatingOrDeleted(part) && CompOrNull<BodyPartComponent>(part)?.Body is { } body)
            _pending.Add(body);
    }

    /// <inheritdoc/>
    public override void Update(float frameTime)
    {
        var now = _timing.CurTime;
        if (now >= _nextSweep)
        {
            _nextSweep = now + TimeSpan.FromSeconds(MathF.Max(0.1f, _cfg.GetCVar(WolfmedCVars.OverlayRefreshSeconds)));
            var hosts = EntityQueryEnumerator<WoundHostComponent, PartDamageVisualsComponent>();
            while (hosts.MoveNext(out var uid, out _, out _))
                _pending.Add(uid);
        }

        if (_pending.Count == 0)
            return;

        foreach (var body in _pending)
            Refresh(body);

        _pending.Clear();
    }

    /// <summary>Recomputes a body's wound and rot overlays and networks them when they changed. Public for tests.</summary>
    public void Refresh(EntityUid body)
    {
        if (TerminatingOrDeleted(body) || !TryComp(body, out WoundHostComponent? host) ||
            !TryComp(body, out PartDamageVisualsComponent? visual))
            return;

        var streamRate = _cfg.GetCVar(WolfmedCVars.WoundOverlayStreamRate);
        var wounds = _woundScratch;
        var rot = _rotScratch;
        var arteries = _arteryScratch;
        var stumps = _stumpScratch;
        var present = _presentScratch;
        wounds.Clear();
        rot.Clear();
        arteries.Clear();
        stumps.Clear();
        present.Clear();
        foreach (var (_, bodyPart) in _body.GetBodyChildren(body))
            present.Add((bodyPart.PartType, bodyPart.Symmetry));

        foreach (var (part, bodyPart) in _body.GetBodyChildren(body))
        {
            if (!_projection.TryGetVisualLayer(part, out var layer) || !TryComp(part, out WoundableComponent? woundable) ||
                !_traits.IsOrganic((part, woundable)))
                continue;

            var look = GetWoundOverlay((part, woundable), host.DismembermentWound, streamRate);
            if (look != WolfmedWoundOverlay.None &&
                (!wounds.TryGetValue(layer, out var current) || WolfmedWoundOverlays.Rank(look) > WolfmedWoundOverlays.Rank(current)))
                wounds[layer] = look;

            if (IsRotting((part, woundable)))
                rot.Add(layer);

            CollectSites((part, bodyPart, woundable), host.DismembermentWound, streamRate, present, arteries, stumps);
        }

        var colour = _gore.GetBloodColor(body) ?? FallbackBlood;
        if (Same(visual.Wounds, wounds) && visual.Rot.SetEquals(rot) && visual.WoundColor == colour &&
            Same(visual.Arteries, arteries) && Same(visual.Stumps, stumps))
            return;

        visual.Wounds = new Dictionary<HumanoidVisualLayers, WolfmedWoundOverlay>(wounds);
        visual.Rot = new HashSet<HumanoidVisualLayers>(rot);
        visual.WoundColor = colour;
        visual.Arteries = new Dictionary<WolfmedArterySite, WolfmedArteryOverlay>(arteries);
        visual.Stumps = new Dictionary<WolfmedArterySite, WolfmedWoundOverlay>(stumps);
        Dirty(body, visual);
    }

    /// <summary>
    /// Playtest 4: the arteries and stumps this part puts on the sprite. A cut artery on the part itself is the part's
    /// own site; a stump wound on it is the site of the part that was torn off, unless that part is back on, and that
    /// site also gets the stump: the art whenever the stump is open, a drip or trickle by the part's stream rate while
    /// it bleeds. The artery look follows the blood spurts' rule (<c>WolfmedBleedSpurtSystem.HasSpurtSource</c>): a
    /// stump spurts while it bleeds untreated, a cut artery while it bleeds at all; anything else open is the still
    /// artery. The worse look wins a shared site.
    /// </summary>
    public void CollectSites(Entity<BodyPartComponent, WoundableComponent> part, ProtoId<WoundPrototype> stump,
        float streamRate, HashSet<(BodyPartType, BodyPartSymmetry)> present,
        Dictionary<WolfmedArterySite, WolfmedArteryOverlay> arteries, Dictionary<WolfmedArterySite, WolfmedWoundOverlay> stumps)
    {
        foreach (var wound in _wounds.GetWounds((part.Owner, part.Comp2)))
        {
            if (wound.Comp.State != WoundState.Open)
                continue;

            WolfmedArterySite? site;
            var rate = TryComp(wound, out WoundBleedingComponent? bleeding) ? bleeding.CurrentRate : 0f;
            var spurting = rate > 0f;
            if (wound.Comp.Prototype == stump)
            {
                if (!TryComp(wound, out WolfmedStumpComponent? tag) || present.Contains((tag.PartType, tag.Symmetry)))
                    continue;

                site = WolfmedArterySites.ForPart(tag.PartType, tag.Symmetry, true);
                // A stump whose bleed has run out has no bleeding component at all; && so it is never read then.
                spurting = spurting && bleeding!.Treatment == BleedingTreatment.None;
                if (site is { } where)
                {
                    var stumpLook = rate <= 0f ? WolfmedWoundOverlay.Old
                        : rate >= streamRate ? WolfmedWoundOverlay.Stream : WolfmedWoundOverlay.Drip;
                    if (!stumps.TryGetValue(where, out var currentStump) ||
                        WolfmedWoundOverlays.Rank(stumpLook) > WolfmedWoundOverlays.Rank(currentStump))
                        stumps[where] = stumpLook;
                }
            }
            else if (_traits.TryGetBehavior(wound.Owner, out WolfmedArterialBleedBehavior _))
            {
                site = WolfmedArterySites.ForPart(part.Comp1.PartType, part.Comp1.Symmetry, false);
            }
            else
            {
                continue;
            }

            if (site is not { } at)
                continue;

            var look = spurting ? WolfmedArteryOverlay.Bleeding : WolfmedArteryOverlay.Still;
            if (!arteries.TryGetValue(at, out var current) || look > current)
                arteries[at] = look;
        }
    }

    /// <summary>
    /// A drip while any open wound on the part bleeds, a trickle once the part bleeds at the stream rate, and the still
    /// wound once every bleed on it has clotted or been dressed. Stump wounds are left out: the stump draws itself.
    /// </summary>
    public WolfmedWoundOverlay GetWoundOverlay(Entity<WoundableComponent> part, ProtoId<WoundPrototype> stump,
        float streamRate)
    {
        var rate = 0f;
        var open = false;
        foreach (var wound in _wounds.GetWounds(part.AsNullable()))
        {
            if (wound.Comp.State != WoundState.Open)
                continue;

            // Playtest 4: a stump draws at the joint it was torn from (the Stumps table), not as a glyph on the part.
            if (wound.Comp.Prototype == stump)
                continue;

            // The bleeding component comes with a bleed that rolled and goes when the wound drops under its bleeding
            // stage; a dressing or clotting leaves it at zero, which is the still wound.
            if (TryComp(wound, out WoundBleedingComponent? bleeding))
            {
                open = true;
                rate += bleeding.CurrentRate;
            }
        }

        if (rate > 0f)
            return rate >= streamRate ? WolfmedWoundOverlay.Stream : WolfmedWoundOverlay.Drip;

        return open ? WolfmedWoundOverlay.Old : WolfmedWoundOverlay.None;
    }

    /// <summary>Dead tissue, or an infection on the part at its top stage.</summary>
    public bool IsRotting(Entity<WoundableComponent> part)
    {
        if (CompOrNull<WolfmedNecrosisComponent>(part)?.Necrotic == true)
            return true;

        foreach (var wound in _wounds.GetWounds(part.AsNullable()))
        {
            if (CompOrNull<WolfmedInfectionComponent>(wound)?.Stage == WolfmedInfectionStage.Septic)
                return true;
        }

        return false;
    }

    private static bool Same<TKey, TLook>(Dictionary<TKey, TLook> current, Dictionary<TKey, TLook> next)
        where TKey : notnull
        where TLook : struct, Enum
    {
        if (current.Count != next.Count)
            return false;

        foreach (var (layer, look) in next)
        {
            if (!current.TryGetValue(layer, out var existing) || !EqualityComparer<TLook>.Default.Equals(existing, look))
                return false;
        }

        return true;
    }
}
