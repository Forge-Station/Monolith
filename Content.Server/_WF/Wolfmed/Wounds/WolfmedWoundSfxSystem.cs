using Content.Server._WF.Wolfmed.Gore;
using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared._WF.Wolfmed.Gore;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.FixedPoint;
using Robust.Server.Audio;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.Wolfmed.Wounds;

/// <summary>Plays a wound's sound when it lands or worsens, and throws blood or sparks off the hit body.</summary>
// The sound is keyed by what the wound is and what it is in. Both effects are read from WolfmedSfxProfilePrototype,
// throttled per body, and silent unless the wound moved because something hit the body.
/// <remarks>
/// Server-side because wound creation is. Sounds play at the BODY: a wound lives in the part's container
/// and a part lives in nullspace, so neither has a position to play from. The damage gate is what keeps
/// an infection, a necrosis clock or a healing tick from making a sound; it is stamped by
/// <see cref="WolfmedPartDamageEvent"/>, which is raised inside the same tick as the wound it causes.
/// FIX1: blood is thrown by the hit raising the body's bleeding, not by the wound's category or severity,
/// so a blunt hit that opens a bleed sprays and a cut that clots on contact does not. A chassis has no
/// bleeding to read, so its sparks still come straight off the wound.
/// </remarks>
public sealed class WolfmedWoundSfxSystem : EntitySystem
{
    /// <summary>The shipped profile. Retuning the feedback layer is an edit to that prototype.</summary>
    public const string DefaultProfile = "WFWolfmedSfxDefault";

    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WolfmedGoreSystem _gore = default!;
    [Dependency] private WolfmedWoundTraitSystem _traits = default!;
    [Dependency] private WoundSystem _wounds = default!;
    [Dependency] private Life.WolfmedSpawnInjurySystem _spawnInjury = default!;

    /// <summary>FIX1: bodies hit this tick, waiting for their bleeding to be compared with the snapshot.</summary>
    private readonly List<EntityUid> _pendingBleed = new();

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WolfmedPartDamageEvent>(OnPartDamage);
        SubscribeLocalEvent<WolfmedWoundLifecycleEvent>(OnWoundLifecycle);
    }

    /// <summary>Server time, so a system sharing the throttle stamps it the same way this one does.</summary>
    public TimeSpan Now => _timing.CurTime;

    /// <summary>The shipped profile, or null when a server has deleted it.</summary>
    public WolfmedSfxProfilePrototype? Profile =>
        _prototypes.TryIndex<WolfmedSfxProfilePrototype>(DefaultProfile, out var profile) ? profile : null;

    private void OnPartDamage(ref WolfmedPartDamageEvent args)
    {
        // A body taking its spawn injuries was not hit: no sound, no spray.
        if (TerminatingOrDeleted(args.Body) || _spawnInjury.IsApplying(args.Body))
            return;

        var state = EnsureComp<WolfmedWoundSfxComponent>(args.Body);
        // FIX1: raised before the wounds of this hit exist, so this is the bleeding the hit found. One
        // snapshot per pending body: a hit that carries two damage types must still only be one spray,
        // and a second hit arriving before the first was resolved must not overwrite the figure.
        if (!_pendingBleed.Contains(args.Body))
        {
            state.BleedBefore = TotalBleeding(args.Body);
            _pendingBleed.Add(args.Body);
        }

        state.LastHit = _timing.CurTime;
        // Kept as a vector, not an entity: a projectile is usually gone by the time the wound lands (G1).
        state.LastDirection = _gore.GetHitDirection(args.Body, args.Origin, args.Tool);
    }

    /// <summary>
    /// FIX1: resolves every body that was hit, once all of that hit's wounds have been created and the
    /// bleeding system has recomputed them. Running here rather than off the wound events is what keeps a
    /// burst of damage types to one decision, and keeps the decision off the order two handlers run in.
    /// </summary>
    public override void Update(float frameTime)
    {
        if (_pendingBleed.Count == 0)
            return;

        foreach (var body in _pendingBleed)
            ResolveBleedSpray(body);

        _pendingBleed.Clear();
    }

    private void ResolveBleedSpray(EntityUid body)
    {
        if (TerminatingOrDeleted(body) ||
            !TryComp(body, out WolfmedWoundSfxComponent? state) ||
            Profile is not { } profile)
            return;

        var after = TotalBleeding(body);
        var gain = after - state.BleedBefore;
        state.BleedBefore = after;

        // The whole trigger: the hit made this body bleed more. Infection creep, clotting, treatment and
        // healing never reach here, because only a hit puts a body on the pending list at all.
        if (gain < profile.HitSplatter.MinBleedIncrease)
            return;

        TrySpawnDebris(body, state, profile, true, gain);
    }

    /// <summary>Bleeding severity summed over every wound on every part, in wound-severity units.</summary>
    public FixedPoint2 TotalBleeding(EntityUid body)
    {
        var total = FixedPoint2.Zero;
        foreach (var (part, _) in _body.GetBodyChildren(body))
        {
            if (!TryComp(part, out WoundableComponent? woundable))
                continue;

            foreach (var wound in _wounds.GetWounds((part, woundable)))
            {
                if (TryComp(wound, out WoundBleedingComponent? bleeding))
                    total += bleeding.BleedingSeverity;
            }
        }

        return total;
    }

    private void OnWoundLifecycle(ref WolfmedWoundLifecycleEvent args)
    {
        if (args.Kind == WolfmedWoundLifecycle.Removed ||
            args.Severity <= args.OldSeverity ||
            CompOrNull<BodyPartComponent>(args.Part)?.Body is not { } body ||
            !TryComp(body, out WolfmedWoundSfxComponent? state) ||
            state.LastHit != _timing.CurTime)
            return;

        if (Profile is not { } profile)
            return;

        var organic = _traits.IsOrganic(args.Part);
        _prototypes.TryIndex(args.Prototype, out var prototype);

        // A hit that only deepens an existing wound is still a hit: it sounds once its growth is a wound's worth.
        if (args.Severity >= profile.MinSeverity &&
            (args.Kind == WolfmedWoundLifecycle.Created || JumpedStage(prototype, args.OldSeverity, args.Severity) ||
             args.Severity - args.OldSeverity >= profile.MinSeverity))
            TryPlayWound(body, state, profile, args.Prototype, prototype, organic, args.Severity);

        // FIX1: a chassis still sparks off the wound itself, because a chassis does not bleed and has no
        // other signal. Flesh is answered in Update, once the hit's effect on the bleeding is known.
        if (!organic)
            TrySpawnDebris(body, state, profile, false, args.Severity - args.OldSeverity);
    }

    /// <summary>
    /// Plays the sound this wound calls for. Public so the dismemberment system and a test can share the
    /// one throttle: two feedback sources on one body in one tick is one noise, not two.
    /// </summary>
    public bool TryPlayWound(
        EntityUid body,
        WolfmedWoundSfxComponent state,
        WolfmedSfxProfilePrototype profile,
        ProtoId<WoundPrototype> wound,
        WoundPrototype? prototype,
        bool organic,
        FixedPoint2 severity)
    {
        if (!_config.GetCVar(WolfmedCVars.WoundSfx) ||
            _timing.CurTime < state.NextSound ||
            profile.GetWoundSound(wound, prototype, organic, severity) is not { } sound)
            return false;

        state.NextSound = _timing.CurTime + profile.SoundInterval;
        _audio.PlayPvs(sound, body);
        return true;
    }

    /// <summary>
    /// Spawns the debris tier this severity earns, if the body is off its throttle. Flesh throws G1's
    /// spray; the V4 mist is what a body with no bloodstream, or a disabled splatter, falls back to, and a
    /// chassis still gets its sparks.
    /// </summary>
    /// <remarks>
    /// FIX1: the organic path also has to clear the burst budget, and the mist falls under it too, so a
    /// magazine emptied into a Vox is a few sprays rather than one effect per round.
    /// </remarks>
    public EntityUid? TrySpawnDebris(
        EntityUid body,
        WolfmedWoundSfxComponent state,
        WolfmedSfxProfilePrototype profile,
        bool organic,
        FixedPoint2 severity)
    {
        if (!_config.GetCVar(WolfmedCVars.HitDebris) ||
            _timing.CurTime < state.NextDebris ||
            profile.GetDebris(organic, severity) is not { } effect ||
            organic && !TryTakeBurstBudget(state, profile.HitSplatter))
            return null;

        state.NextDebris = _timing.CurTime + profile.DebrisInterval;

        if (organic &&
            _gore.TrySpawnSplatter(body, profile.HitSplatter, state.LastDirection, severity) is { } splatter)
            return splatter;

        return Spawn(effect, _transform.GetMapCoordinates(body));
    }

    /// <summary>
    /// FIX1: spends one of this body's sprays for the current window, or refuses when they are gone. The
    /// window is restarted lazily, so a body that has not been shot at in a while always has a full one.
    /// </summary>
    public bool TryTakeBurstBudget(WolfmedWoundSfxComponent state, WolfmedHitSplatterSpec spec)
    {
        if (spec.BurstBudget <= 0)
            return true;

        // Compared as a sum: BurstWindowStart defaults to TimeSpan.MinValue, and subtracting that overflows.
        if (state.BurstWindowStart == TimeSpan.MinValue || _timing.CurTime >= state.BurstWindowStart + spec.BurstWindow)
        {
            state.BurstWindowStart = _timing.CurTime;
            state.SpraysInWindow = 0;
        }

        if (state.SpraysInWindow >= spec.BurstBudget)
            return false;

        state.SpraysInWindow++;
        return true;
    }

    /// <summary>Whether the wound crossed into a new severity stage. A wound with no stages never does.</summary>
    public static bool JumpedStage(WoundPrototype? prototype, FixedPoint2 old, FixedPoint2 severity) =>
        prototype != null && prototype.GetStage(old) != prototype.GetStage(severity);
}
