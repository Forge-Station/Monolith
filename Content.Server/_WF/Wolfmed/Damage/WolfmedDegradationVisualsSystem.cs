using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Wolfmed.Damage;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Humanoid;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Wolfmed.Damage;

/// <summary>
/// V3: turns each part's own wound severity into a degradation stage for the humanoid sprite. The stages
/// ride <see cref="PartDamageVisualsComponent.Degradation"/>, which is already networked and already has a
/// client-side hook, so nothing new crosses the wire and no new subscription is taken.
/// </summary>
/// <remarks>
/// Driven off the wound lifecycle broadcast rather than off damage: a wound healed by sutures or surgery
/// changes no damage number, and dead tissue is not damage at all. Server only, like every other wound
/// write; the client reads the networked result.
/// </remarks>
public sealed class WolfmedDegradationVisualsSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private WolfmedWoundTraitSystem _traits = default!;
    [Dependency] private WoundDamageProjectionSystem _projection = default!;
    [Dependency] private WoundSystem _wounds = default!;

    private readonly HashSet<EntityUid> _pending = new();
    private readonly Dictionary<HumanoidVisualLayers, WolfmedPartDegradation> _scratch = new();

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WolfmedWoundLifecycleEvent>(OnWoundLifecycle);
    }

    private void OnWoundLifecycle(ref WolfmedWoundLifecycleEvent args)
    {
        if (TerminatingOrDeleted(args.Part))
            return;

        // Coalesced: one explosion creates wounds on dozens of parts across several bodies in a single
        // tick, and each event would otherwise walk a whole body. One walk per body per tick instead.
        _pending.Add(CompOrNull<BodyPartComponent>(args.Part)?.Body ?? GetDetachedRoot(args.Part));
    }

    /// <inheritdoc/>
    public override void Update(float frameTime)
    {
        if (_pending.Count == 0)
            return;

        foreach (var uid in _pending)
            Refresh(uid);

        _pending.Clear();
    }

    /// <summary>
    /// Recomputes every layer's stage on a body, or on the root of a limb that is off the body, and
    /// networks the result. Public so the part lifecycle, necrosis and a test can force it.
    /// </summary>
    public void Refresh(EntityUid uid)
    {
        if (TerminatingOrDeleted(uid) || !TryComp(uid, out PartDamageVisualsComponent? visual))
            return;

        var settings = CompOrNull<WolfmedDegradationVisualsComponent>(uid);
        if (settings is { Enabled: false } ||
            !_prototypes.TryIndex(settings?.Profile ?? WolfmedDegradationVisualsComponent.DefaultProfile,
                out WolfmedDegradationProfilePrototype? profile))
        {
            Clear(uid, visual);
            return;
        }

        // Built into a scratch map and only copied out when it differs, so an unchanged body allocates
        // nothing and sends nothing.
        var stages = _scratch;
        stages.Clear();
        foreach (var (part, _) in GetParts(uid))
        {
            if (!_projection.TryGetVisualLayer(part, out var layer))
                continue;

            var stage = GetStage(part, profile);
            if (stage == WolfmedPartDegradation.None)
                continue;

            // A body with more limbs than layers (a second left arm) shows the worse of the two.
            if (!stages.TryGetValue(layer, out var current) || Rank(stage) > Rank(current))
                stages[layer] = stage;
        }

        if (Same(visual.Degradation, stages))
            return;

        visual.Degradation = new Dictionary<HumanoidVisualLayers, WolfmedPartDegradation>(stages);
        Dirty(uid, visual);
    }

    /// <summary>The stage this part's wounds have reached, and the material they are showing.</summary>
    public WolfmedPartDegradation GetStage(EntityUid part, WolfmedDegradationProfilePrototype profile)
    {
        if (!TryComp(part, out WoundableComponent? woundable))
            return WolfmedPartDegradation.None;

        // Read off the component rather than through WolfmedNecrosisSystem so the dependency runs one way:
        // necrosis asks for a refresh, this never asks necrosis anything.
        if (CompOrNull<WolfmedNecrosisComponent>(part)?.Necrotic == true)
            return WolfmedPartDegradation.Necrotic;

        var severity = FixedPoint2.Zero;
        foreach (var wound in _wounds.GetWounds((part, woundable)))
            severity += wound.Comp.Severity;

        return profile.GetStage(severity, _traits.IsMechanical((part, woundable)));
    }

    private IEnumerable<(EntityUid Id, BodyPartComponent Component)> GetParts(EntityUid uid)
    {
        // A wound host enumerates its whole body; a severed limb enumerates itself and whatever came off
        // attached to it, which is what RefreshDetachedDamage projects onto the same component.
        return HasComp<WoundHostComponent>(uid)
            ? _body.GetBodyChildren(uid)
            : TryComp(uid, out BodyPartComponent? part)
                ? _body.GetBodyPartChildren(uid, part)
                : [];
    }

    private EntityUid GetDetachedRoot(EntityUid part)
    {
        var root = part;
        while (_body.GetParentPartOrNull(root) is { } parent)
            root = parent;

        return root;
    }

    private void Clear(EntityUid uid, PartDamageVisualsComponent visual)
    {
        if (visual.Degradation.Count == 0)
            return;

        visual.Degradation = new Dictionary<HumanoidVisualLayers, WolfmedPartDegradation>();
        Dirty(uid, visual);
    }

    /// <summary>How bad a stage is, across materials, for the duplicate-layer tie-break.</summary>
    private static int Rank(WolfmedPartDegradation stage) => stage switch
    {
        WolfmedPartDegradation.Necrotic => 3,
        WolfmedPartDegradation.Bone or WolfmedPartDegradation.Wiring => 2,
        WolfmedPartDegradation.Muscle or WolfmedPartDegradation.Struts => 1,
        _ => 0,
    };

    private static bool Same(Dictionary<HumanoidVisualLayers, WolfmedPartDegradation> current,
        Dictionary<HumanoidVisualLayers, WolfmedPartDegradation> next)
    {
        if (current.Count != next.Count)
            return false;

        foreach (var (layer, stage) in next)
        {
            if (!current.TryGetValue(layer, out var existing) || existing != stage)
                return false;
        }

        return true;
    }
}
