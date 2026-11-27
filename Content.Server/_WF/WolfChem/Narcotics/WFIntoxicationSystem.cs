using System.Numerics;
using Content.Server._WF.Lattice;
using Content.Shared._WF.WolfChem.Narcotics;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Drowsiness;
using Content.Shared.EntityEffects;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.StatusEffect;
using Content.Shared.Stunnable;
using Robust.Server.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.WolfChem.Narcotics;

/// <summary>
/// Ticks drug levels once a second: reads the blood, decays fed level, applies the tier each reaches, and starts and
/// ends crashes.
/// </summary>
public sealed partial class WFIntoxicationSystem : SharedWFIntoxicationSystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private StatusEffectsSystem _status = default!;
    [Dependency] private WFDriftEffectsSystem _drift = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private StaminaSystem _stamina = default!;
    [Dependency] private SharedStunSystem _stun = default!;

    /// <summary>Seconds between ticks.</summary>
    public const float TickSeconds = 1f;

    /// <summary>How long a tier's status effects outlast the tick that set them.</summary>
    private static readonly TimeSpan StatusTime = TimeSpan.FromSeconds(TickSeconds * 2.5f);

    /// <summary>Seconds between the nods of a crash or withdrawal Drowsiness, (min, max).</summary>
    public static readonly Vector2 DrowsyGap = new(60f, 120f);

    /// <summary>Seconds each nod puts the body to sleep, (min, max).</summary>
    public static readonly Vector2 DrowsyLength = new(1f, 2f);

    private TimeSpan _nextTick;
    private readonly List<ProtoId<WFDrugProfilePrototype>> _profiles = new();

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextTick)
            return;

        _nextTick = _timing.CurTime + TimeSpan.FromSeconds(TickSeconds);
        Tick(TickSeconds);
    }

    /// <summary>Advances every intoxicated body by <paramref name="seconds"/>.</summary>
    public void Tick(float seconds)
    {
        var query = EntityQueryEnumerator<WFIntoxicationComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            TickBody((uid, comp), seconds);
        }
    }

    private void TickBody(Entity<WFIntoxicationComponent> ent, float seconds)
    {
        var (uid, comp) = ent;
        var speedBefore = GetSpeed(comp);
        var args = new EntityEffectBaseArgs(uid, EntityManager);
        // A corpse's levels and crashes still run down, but nothing acts on it.
        var alive = !_mobState.IsDead(uid);

        var blood = ReadBlood(uid);
        _profiles.Clear();
        _profiles.AddRange(comp.Levels.Keys);
        foreach (var profile in _blood.Keys)
        {
            if (!comp.Levels.ContainsKey(profile))
                _profiles.Add(profile);
        }

        foreach (var profile in _profiles)
        {
            if (!Proto.TryIndex(profile, out var proto))
            {
                comp.Levels.Remove(profile);
                comp.Fed.Remove(profile);
                comp.Rush.Remove(profile);
                comp.BloodSeen.Remove(profile);
                continue;
            }

            // Tolerance speeds the fed level's decay and blunts the blood's.
            var tolerance = ToleranceFactor(uid, proto.Family);
            var fed = Decayed(proto, comp.Fed.GetValueOrDefault(profile), tolerance * seconds);
            var fromBlood = _blood.GetValueOrDefault(profile) / tolerance;
            var level = fed + fromBlood;
            var rush = Rush(comp, profile, proto, fromBlood, seconds);
            if (rush > 0f)
            {
                // The rush deepens the high but never tips it into the overdose.
                var peak = level + rush;
                level = proto.Tiers.Count > 1 ? Math.Max(level, Math.Min(peak, proto.Tiers[^1].Min - 0.01f)) : peak;
            }

            var tier = proto.TierAt(level);

            if (fed > 0f)
                comp.Fed[profile] = fed;
            else
                comp.Fed.Remove(profile);

            if (blood is { } soln)
                Flush(soln, profile, proto, level, fromBlood, seconds);
            AnnouncePhase(ent, profile, proto, tier);

            if (tier >= 0)
            {
                comp.TimeAbove[profile] = comp.TimeAbove.GetValueOrDefault(profile) + seconds;
                if (alive)
                {
                    Apply(uid, args, proto.Tiers[tier].Effects, proto.Tiers[tier].StatusEffects);
                    Hallucinate(uid, proto.Tiers[tier]);
                }
            }
            else if (comp.TimeAbove.Remove(profile, out var above) && proto.Crash is { } crash && above >= crash.After)
            {
                if (alive && !comp.Crashes.ContainsKey(profile))
                    _stun.TryKnockdown(uid, crash.Collapse, true);

                // A redose paused any crash still running; the new one adds to it.
                var duration = comp.Crashes.GetValueOrDefault(profile) + crash.Duration.TotalSeconds + crash.PerSecondAbove * above;
                comp.Crashes[profile] = (float) Math.Min(duration, crash.MaxDuration.TotalSeconds);
            }

            if (level <= 0f)
                comp.Levels.Remove(profile);
            else
                comp.Levels[profile] = level;
        }

        _profiles.Clear();
        _profiles.AddRange(comp.Crashes.Keys);
        foreach (var profile in _profiles)
        {
            if (IsHigh(comp, profile))
                continue;

            var left = comp.Crashes[profile] - seconds;
            if (left <= 0f || !Proto.TryIndex(profile, out var proto) || proto.Crash == null)
            {
                comp.Crashes.Remove(profile);
                continue;
            }

            comp.Crashes[profile] = left;
            if (!alive)
                continue;

            Apply(uid, args, proto.Crash.Effects, proto.Crash.StatusEffects);
            Drain(uid, proto.Crash, seconds);
            if (proto.Crash.StatusEffects.Exists(s => s.Key == SharedDrowsinessSystem.DrowsinessKey))
                TameDrowsiness(uid);
        }

        UpdateTraits(ent, seconds, alive);

        if (!MathHelper.CloseTo(GetSpeed(comp), speedBefore))
            Movement.RefreshMovementSpeedModifiers(uid);

        if (comp.Levels.Count == 0 && comp.Crashes.Count == 0 && comp.Debt <= 0f)
        {
            RemCompDeferred<WFIntoxicationComponent>(uid);
            return;
        }

        Dirty(uid, comp);
    }

    /// <summary>The fed level after <paramref name="seconds"/> of decay.</summary>
    public static float Decayed(WFDrugProfilePrototype proto, float level, float seconds)
    {
        return Math.Max(0f, level - proto.Decay * seconds);
    }

    /// <summary>Gives a fresh dose's rush for level fed without the blood, as a neuroware program feeds it.</summary>
    public void AddRush(EntityUid uid, ProtoId<WFDrugProfilePrototype> profile, float level)
    {
        if (level <= 0f
            || !Proto.TryIndex(profile, out var proto)
            || proto.RushGain <= 0f
            || !TryComp<WFIntoxicationComponent>(uid, out var comp))
            return;

        comp.Rush[profile] = comp.Rush.GetValueOrDefault(profile) + level * proto.RushGain;
    }

    /// <summary>
    /// Fades a profile's rush and adds to it for any rise in its level from the blood since the last tick, which is
    /// a fresh dose. Returns the rush left.
    /// </summary>
    private static float Rush(WFIntoxicationComponent comp, ProtoId<WFDrugProfilePrototype> profile,
        WFDrugProfilePrototype proto, float fromBlood, float seconds)
    {
        var rush = comp.Rush.GetValueOrDefault(profile);
        if (proto.RushHalfLife > 0f)
            rush *= MathF.Pow(2f, -seconds / proto.RushHalfLife);

        var rise = fromBlood - comp.BloodSeen.GetValueOrDefault(profile);
        if (rise > 0f)
            rush += rise * proto.RushGain;

        if (fromBlood > 0f)
            comp.BloodSeen[profile] = fromBlood;
        else
            comp.BloodSeen.Remove(profile);

        if (rush < 0.05f)
        {
            comp.Rush.Remove(profile);
            return 0f;
        }

        comp.Rush[profile] = rush;
        return rush;
    }

    /// <summary>Tells the user when a profile moves into a new phase: light, heavy or overdose.</summary>
    private void AnnouncePhase(Entity<WFIntoxicationComponent> ent, ProtoId<WFDrugProfilePrototype> profile,
        WFDrugProfilePrototype proto, int tier)
    {
        var phase = proto.PhaseOf(tier);
        var before = ent.Comp.Phases.GetValueOrDefault(profile, WFDrugPhase.None);
        if (phase == before)
            return;

        if (phase == WFDrugPhase.None)
            ent.Comp.Phases.Remove(profile);
        else
            ent.Comp.Phases[profile] = phase;

        var line = (phase, phase > before) switch
        {
            (WFDrugPhase.Light, true) => "wf-drug-phase-light",
            (WFDrugPhase.Heavy, true) => "wf-drug-phase-heavy",
            (WFDrugPhase.Overdose, true) => "wf-drug-phase-overdose",
            (WFDrugPhase.Heavy, false) => "wf-drug-phase-easing",
            (WFDrugPhase.Light, false) => "wf-drug-phase-fading",
            _ => null,
        };

        if (line == null)
            return;

        var type = phase == WFDrugPhase.Overdose ? PopupType.LargeCaution
            : phase > before ? PopupType.Medium
            : PopupType.Small;
        _popup.PopupEntity(Loc.GetString(line), ent, ent, type);
    }

    private void Apply(EntityUid uid, EntityEffectBaseArgs args, List<EntityEffect> effects, List<WFDrugStatusEffect> statuses)
    {
        foreach (var effect in effects)
        {
            if (effect.ShouldApply(args, _random))
                effect.Effect(args);
        }

        foreach (var status in statuses)
        {
            if (status.Component != null)
                _status.TryAddStatusEffect(uid, status.Key, StatusTime, true, status.Component);
            else
                _status.TryAddStatusEffect(uid, status.Key, StatusTime, true);
        }
    }

    /// <summary>Wears stamina down at a crash's rate, never past its cap: every hit resets regen, so an uncapped drain stun-locks.</summary>
    private void Drain(EntityUid uid, WFDrugCrash crash, float seconds)
    {
        if (crash.Stamina <= 0f || !TryComp<StaminaComponent>(uid, out var stamina) || stamina.Critical)
            return;

        var room = stamina.CritThreshold * crash.StaminaCap - stamina.StaminaDamage;
        if (room > 0f)
            _stamina.TakeStaminaDamage(uid, Math.Min(crash.Stamina * seconds, room), stamina, visual: false, immediate: false);
    }

    /// <summary>
    /// Turns a Drowsiness status into a short nod every minute or two. Its defaults sleep the body for seconds at a
    /// time every few seconds.
    /// </summary>
    public void TameDrowsiness(EntityUid uid)
    {
        if (!TryComp<DrowsinessComponent>(uid, out var drowsy) || drowsy.TimeBetweenIncidents == DrowsyGap)
            return;

        drowsy.TimeBetweenIncidents = DrowsyGap;
        drowsy.DurationOfIncident = DrowsyLength;
        drowsy.NextIncidentTime = _timing.CurTime + TimeSpan.FromSeconds(_random.NextFloat(DrowsyGap.X, DrowsyGap.Y));
    }

    private void Hallucinate(EntityUid uid, WFDrugTier tier)
    {
        if (tier.Hallucinations.Count == 0
            || !_random.Prob(tier.HallucinationChance)
            || !_players.TryGetSessionByEntity(uid, out var session))
            return;

        _drift.Show(session, uid, _random.Pick(tier.Hallucinations));
    }
}
