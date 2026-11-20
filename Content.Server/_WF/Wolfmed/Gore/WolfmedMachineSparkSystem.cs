using Content.Server._WF.Wolfmed.Wounds;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Cybernetics;
using Content.Shared._WF.Wolfmed.Gore;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Robust.Server.Audio;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.Wolfmed.Gore;

/// <summary>Makes a badly damaged or EMP-struck machine part throw sparks every few seconds until repaired.</summary>
// Built like WolfmedBleedSpurtSystem: WolfmedMachineSparkComponent sits on a body only while it has a sparking part,
// so the tick walks a handful of entities, and the condition is re-asked on every wound change and every spark.
public sealed class WolfmedMachineSparkSystem : EntitySystem
{
    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private WolfmedWoundSfxSystem _sfx = default!;
    [Dependency] private WolfmedWoundTraitSystem _traits = default!;

    private readonly HashSet<EntityUid> _pending = new();
    private readonly List<EntityUid> _finished = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WolfmedWoundLifecycleEvent>(OnWoundLifecycle);
    }

    private void OnWoundLifecycle(ref WolfmedWoundLifecycleEvent args)
    {
        if (CompOrNull<BodyPartComponent>(args.Part)?.Body is { } body)
            _pending.Add(body);
    }

    /// <summary>Asks again whether this body should be sparking. For callers that change a part without a wound event.</summary>
    public void QueueRefresh(EntityUid body) => _pending.Add(body);

    public override void Update(float frameTime)
    {
        if (_sfx.Profile is not { } profile)
            return;

        var spec = profile.MachineSparks;
        if (_pending.Count > 0)
        {
            foreach (var body in _pending)
                Refresh(body, spec);

            _pending.Clear();
        }

        if (!spec.Enabled)
            return;

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WolfmedMachineSparkComponent>();
        while (query.MoveNext(out var uid, out var sparks))
        {
            if (now < sparks.NextSpark)
                continue;

            if (!HasSparkingPart(uid, spec))
            {
                _finished.Add(uid);
                continue;
            }

            Spawn(spec.Effect, Transform(uid).Coordinates);
            if (spec.Sound != null)
                _audio.PlayPvs(spec.Sound, uid);

            sparks.NextSpark = now + Next(spec);
        }

        foreach (var uid in _finished)
            RemComp<WolfmedMachineSparkComponent>(uid);

        _finished.Clear();
    }

    private void Refresh(EntityUid body, WolfmedMachineSparkSpec spec)
    {
        if (TerminatingOrDeleted(body))
            return;

        if (!spec.Enabled || !HasSparkingPart(body, spec))
        {
            RemComp<WolfmedMachineSparkComponent>(body);
            return;
        }

        if (!HasComp<WolfmedMachineSparkComponent>(body))
            EnsureComp<WolfmedMachineSparkComponent>(body).NextSpark = _timing.CurTime + Next(spec);
    }

    /// <summary>Whether any machine part on this body is damaged past the floor or switched off by an EMP.</summary>
    public bool HasSparkingPart(EntityUid body, WolfmedMachineSparkSpec spec)
    {
        if (TerminatingOrDeleted(body) || !HasComp<WoundHostComponent>(body))
            return false;

        foreach (var (part, _) in _body.GetBodyChildren(body))
        {
            if (!TryComp(part, out WoundableComponent? woundable) || _traits.IsOrganic((part, woundable)))
                continue;

            if (TryComp(part, out CyberneticsComponent? cybernetics) && cybernetics.Disabled)
                return true;

            // Damage, not wound severity: a surge that is too light to open a wound still leaves a scorched part.
            if (TryComp(part, out DamageableComponent? damage) && damage.TotalDamage >= spec.MinDamage)
                return true;
        }

        return false;
    }

    private TimeSpan Next(WolfmedMachineSparkSpec spec)
    {
        var jitter = spec.Jitter.TotalSeconds * (_random.NextDouble() * 2 - 1);
        return TimeSpan.FromSeconds(Math.Max(1, spec.Interval.TotalSeconds + jitter));
    }
}

/// <summary>Marks a body with a sparking machine part, and when it next sparks.</summary>
[RegisterComponent]
public sealed partial class WolfmedMachineSparkComponent : Component
{
    [ViewVariables]
    public TimeSpan NextSpark;
}
