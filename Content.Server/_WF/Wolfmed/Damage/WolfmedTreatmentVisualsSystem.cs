using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Wolfmed.Damage;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Humanoid;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.Wolfmed.Damage;

/// <summary>
/// G3: turns what is tied around each limb into an overlay state for the humanoid sprite. A dressing over
/// a bleed shows gauze; a fracture held by a splint shows that splint.
/// </summary>
/// <remarks>
/// Rides <see cref="PartDamageVisualsComponent.Treatments"/> for the same reason V3's degradation stage
/// does: the client already owns that component's state hook. Driven off the three events that can change
/// a treatment without changing a damage number - the wound lifecycle broadcast, a bleed refresh and a
/// fracture treatment change - and coalesced to one walk per body per tick.
/// </remarks>
public sealed class WolfmedTreatmentVisualsSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private WoundDamageProjectionSystem _projection = default!;
    [Dependency] private WoundSystem _wounds = default!;

    private readonly HashSet<EntityUid> _pending = new();
    private readonly List<EntityUid> _expired = new();
    private readonly Dictionary<HumanoidVisualLayers, WolfmedPartTreatment> _scratch = new();

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WolfmedWoundLifecycleEvent>(OnWoundLifecycle);
        // Both are free pairs: FractureEffectsSystem holds the fracture event on the WOUND, not the part,
        // and nothing subscribes the bleeding one at all.
        SubscribeLocalEvent<WoundComponent, WoundBleedingChangedEvent>(OnBleedingChanged);
        SubscribeLocalEvent<WoundableComponent, FractureTreatmentChangedEvent>(OnFractureChanged);
    }

    private void OnWoundLifecycle(ref WolfmedWoundLifecycleEvent args) => Queue(args.Part);

    private void OnBleedingChanged(Entity<WoundComponent> wound, ref WoundBleedingChangedEvent args) =>
        Queue(args.Part);

    private void OnFractureChanged(Entity<WoundableComponent> part, ref FractureTreatmentChangedEvent args) =>
        Queue(part.Owner);

    private void Queue(EntityUid part)
    {
        if (!TerminatingOrDeleted(part) && CompOrNull<BodyPartComponent>(part)?.Body is { } body)
            _pending.Add(body);
    }

    /// <inheritdoc/>
    public override void Update(float frameTime)
    {
        // Dressings that have outlasted their wound come off on their own. A handful of parts at most.
        var now = _timing.CurTime;
        var marks = EntityQueryEnumerator<WolfmedDressingMarkComponent>();
        while (marks.MoveNext(out var marked, out var mark))
        {
            if (now >= mark.Until)
                _expired.Add(marked);
        }

        foreach (var marked in _expired)
        {
            RemComp<WolfmedDressingMarkComponent>(marked);
            Queue(marked);
        }

        _expired.Clear();

        if (_pending.Count == 0)
            return;

        foreach (var body in _pending)
            Refresh(body);

        _pending.Clear();
    }

    /// <summary>
    /// Recomputes every layer's treatment on a body and networks the result. Public so the part
    /// lifecycle and a test can force it.
    /// </summary>
    public void Refresh(EntityUid body)
    {
        if (TerminatingOrDeleted(body) || !TryComp(body, out PartDamageVisualsComponent? visual))
            return;

        var settings = CompOrNull<WolfmedTreatmentVisualsComponent>(body);
        if (settings is { Enabled: false } ||
            !_prototypes.TryIndex(settings?.Profile ?? WolfmedTreatmentVisualsComponent.DefaultProfile,
                out WolfmedTreatmentOverlayProfilePrototype? profile))
        {
            Clear(body, visual);
            return;
        }

        var treatments = _scratch;
        treatments.Clear();
        foreach (var (part, _) in _body.GetBodyChildren(body))
        {
            if (!_projection.TryGetVisualLayer(part, out var raw))
                continue;

            var treatment = GetTreatment(part, profile);
            if (treatment == WolfmedPartTreatment.None)
                continue;

            // A hand folds into its arm and a foot into its leg: the art has one band per limb.
            var layer = WolfmedTreatmentLayers.Fold(raw);
            if (!treatments.TryGetValue(layer, out var current) ||
                WolfmedTreatmentLayers.Rank(treatment) > WolfmedTreatmentLayers.Rank(current))
                treatments[layer] = treatment;
        }

        if (Same(visual.Treatments, treatments))
            return;

        visual.Treatments = new Dictionary<HumanoidVisualLayers, WolfmedPartTreatment>(treatments);
        Dirty(body, visual);
    }

    /// <summary>
    /// What this part is wearing. A splint outranks a dressing, and a splint mark that no longer has a
    /// Reduced fracture behind it is thrown away here rather than in every system that could end one.
    /// </summary>
    public WolfmedPartTreatment GetTreatment(EntityUid part, WolfmedTreatmentOverlayProfilePrototype profile)
    {
        if (!TryComp(part, out WoundableComponent? woundable))
            return WolfmedPartTreatment.None;

        var dressed = false;
        var splinted = false;
        foreach (var wound in _wounds.GetWounds((part, woundable)))
        {
            if (TryComp(wound, out WoundFractureComponent? fracture) &&
                fracture.Treatment == FractureTreatment.Reduced)
                splinted = true;

            if (wound.Comp.State == WoundState.Open &&
                TryComp(wound, out WoundBleedingComponent? bleeding) &&
                profile.Dressings.Contains(bleeding.Treatment))
                dressed = true;
        }

        if (!splinted && HasComp<WolfmedSplintMarkComponent>(part))
            RemComp<WolfmedSplintMarkComponent>(part);

        // A dressing outlives the wound it closed: each time the part reads as dressed the clock is pushed back, and
        // the bandage stays on the sprite until it runs out.
        if (dressed)
            EnsureComp<WolfmedDressingMarkComponent>(part).Until = _timing.CurTime + profile.DressingLinger;
        else if (TryComp(part, out WolfmedDressingMarkComponent? mark) && _timing.CurTime < mark.Until)
            dressed = true;

        if (splinted)
            return CompOrNull<WolfmedSplintMarkComponent>(part)?.Overlay ?? WolfmedPartTreatment.Splint;

        return dressed ? WolfmedPartTreatment.Gauze : WolfmedPartTreatment.None;
    }

    private void Clear(EntityUid body, PartDamageVisualsComponent visual)
    {
        if (visual.Treatments.Count == 0)
            return;

        visual.Treatments = new Dictionary<HumanoidVisualLayers, WolfmedPartTreatment>();
        Dirty(body, visual);
    }

    private static bool Same(
        Dictionary<HumanoidVisualLayers, WolfmedPartTreatment> current,
        Dictionary<HumanoidVisualLayers, WolfmedPartTreatment> next)
    {
        if (current.Count != next.Count)
            return false;

        foreach (var (layer, treatment) in next)
        {
            if (!current.TryGetValue(layer, out var existing) || existing != treatment)
                return false;
        }

        return true;
    }
}

/// <summary>A dressing still on a part after its wound closed, and when it comes off.</summary>
[RegisterComponent]
public sealed partial class WolfmedDressingMarkComponent : Component
{
    [ViewVariables]
    public TimeSpan Until;
}

