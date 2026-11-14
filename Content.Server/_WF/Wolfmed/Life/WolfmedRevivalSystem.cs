using System.Linq;
using Content.Server._WF.Wolfmed.Consciousness;
using Content.Server.EUI;
using Content.Server.Ghost;
using Content.Server.Popups;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.IdentityManagement;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Content.Shared._Onyx.Body.Systems; // Playtest 5: the revived brain's floor
using Content.Shared._Shitmed.Body.Organ;
using Content.Shared._WF.Wolfmed.Body;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared._WF.Wolfmed.Life;
using Content.Shared.Atmos.Rotting;
using Content.Shared.Body.Components;
using Content.Shared.Body.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Traits.Assorted;
using Robust.Shared.Configuration;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.Wolfmed.Life;

/// <summary>What a defibrillator and a pair of hands on a chest are worth on a wound host.</summary>
/// <remarks>
/// Nothing here ever marks a body unrevivable. A failed shock is a shock that can be tried again; rot and
/// other content's <see cref="UnrevivableComponent"/> are the only hard stops, and neither is this system's.
/// A shock succeeds on a roll, so no line here ever promises one will work.
/// </remarks>
public sealed class WolfmedRevivalSystem : EntitySystem
{
    [Dependency] private IComponentFactory _factory = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private EuiManager _eui = default!;
    [Dependency] private ISharedPlayerManager _player = default!;
    [Dependency] private SharedMindSystem _mind = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedRottingSystem _rotting = default!;
    [Dependency] private WolfmedConsciousnessSystem _consciousness = default!;
    [Dependency] private WolfmedLifeSystem _life = default!;
    [Dependency] private PopupSystem _popup = default!; // M2
    [Dependency] private SharedAudioSystem _audio = default!; // M2
    [Dependency] private SharedBodySystem _body = default!; // M2
    [Dependency] private WolfmedShutdownSystem _shutdown = default!; // M2
    [Dependency] private WolfmedBodyTemperatureSystem _temperature = default!; // M5
    [Dependency] private OrganHealthSystem _organs = default!; // Playtest 5

    /// <summary>Test seam, mirroring <c>WolfmedEviscerationSystem.ForcedRoll</c>: a forced chance roll.</summary>
    public float? ForcedRoll;

    /// <summary>
    /// The line a failed roll gets, and only a failed roll: another shock may work. Every gate that no number
    /// of shocks will move has its own line (playtest 1: a bare "No response" used to stand for both).
    /// </summary>
    public const string NoResponse = "wolfmed-defib-no-response";

    /// <summary>The refusal for a body this system does not decide: gone, or not a wound host under consciousness.</summary>
    public const string NotMonitored = "wolfmed-defib-not-monitored";

    public const string Success = "wolfmed-defib-success";
    public const string NoBrain = "wolfmed-defib-no-brain";
    /// <summary>Playtest 5: only a destroyed positronic core. A destroyed brain comes back damaged instead.</summary>
    public const string BrainDead = "wolfmed-defib-brain-dead";
    public const string NoHeart = "wolfmed-defib-no-heart";
    public const string PulsePresent = "wolfmed-defib-pulse-present";
    public const string NoBlood = "wolfmed-defib-no-blood";

    /// <summary>M5 (plan §3.10): the core is still under the cold arrest line; a shock would only stop again.</summary>
    public const string TooCold = "wolfmed-defib-too-cold";

    /// <summary>The hand defibrillator's own rot line, so the pod refuses a rotten body in the same words.</summary>
    public const string Rotten = "defibrillator-rotten";

    /// <summary>True when the defibrillator and CPR should ask this system instead of the damage thresholds.</summary>
    public bool OwnsRevival(EntityUid body) => _life.OwnsDeath(body);

    /// <summary>
    /// One shock. Returns whether the heart restarted, with the locale key of the line the paddles should say
    /// either way; <see cref="LocalizeLine"/> turns it into text with the body's numbers in it.
    /// </summary>
    public bool TryDefibrillate(EntityUid body, out string message)
    {
        if (GetRefusal(body) is { } refusal)
        {
            message = refusal;
            return false;
        }

        if (!Roll(GetChance(body)))
        {
            message = NoResponse;
            return false;
        }

        Revive(body);
        message = Success;
        return true;
    }

    /// <summary>The locale key for why the paddles will not charge, or null when they will.</summary>
    // Shared by the hand defibrillator and the pod. Checked in order: rot, UnrevivableComponent, no brain, a destroyed
    // positronic core, no heart, a pulse, then the blood gate. LocalizeLine fills in the numbers.
    public string? GetRefusal(EntityUid body)
    {
        if (TerminatingOrDeleted(body) || !OwnsRevival(body))
            return NotMonitored;

        if (_rotting.IsRotten(body))
            return Rotten;

        if (TryComp(body, out UnrevivableComponent? unrevivable))
            return unrevivable.ReasonMessage;

        if (!_life.HasBrain(body))
            return NoBrain;

        // Playtest 5: a destroyed brain no longer refuses the paddles; Revive brings it back at wolfmed.revive_brain_floor
        // and the patient lives with the damage until it is repaired. A destroyed positronic core still needs core
        // repair first (WFSurgeryRepairCore): a chassis has no damaged-but-running state to come back in.
        if (_life.GetBrainOrgan(body) is not { } organ ||
            organ.Comp.Health <= FixedPoint2.Zero && _shutdown.IsMechanical(body))
            return BrainDead;

        if (_life.GetHeartHealth(body) == null && ExpectsHeart(body))
            return NoHeart;

        // Not in arrest and not dead: the heart is beating, and a shock would do nothing but hurt.
        if (!_life.InArrest(body) && !_mobState.IsDead(body))
            return PulsePresent;

        // Strictly under, and under the blood arrest line, so the common arrest can be restarted; the grace
        // after the shock is the medic's window to get the blood in.
        if (_life.GetBlood(body) < _cfg.GetCVar(WolfmedCVars.DefibBlood))
            return NoBlood;

        // M5: a heart restarted in a core still under the cold arrest line stops again on the next tick. Rewarm first.
        if (_temperature.IsColdArrest(body))
            return TooCold;

        return null;
    }

    /// <summary>
    /// A paddle or pod line with this body's numbers in it: blood %, the units to the post-shock target (plus
    /// the bleed over the grace) and the units to the brain-safe line. Lines that name none of them ignore
    /// the arguments.
    /// </summary>
    public string LocalizeLine(EntityUid body, string key)
    {
        var (toTarget, toSafe) = _life.GetTransfusionGuidance(body);
        return Loc.GetString(key,
            ("percent", MathF.Round(_life.GetBlood(body) * 100f)),
            ("units", MathF.Ceiling(toTarget)),
            ("safe", MathF.Ceiling(toSafe)),
            ("target", MathF.Round(_cfg.GetCVar(WolfmedCVars.PostShockBloodTarget) * 100f)),
            ("line", MathF.Round(_cfg.GetCVar(WolfmedCVars.BrainBloodStart) * 100f)),
            // M5: the cold refusal's core temperature and the line to rewarm past.
            ("kelvin", MathF.Round(_temperature.GetCore(body) ?? 0f)),
            ("rewarm", MathF.Ceiling(_temperature.GetLines(body)?.ColdArrest ?? 0f)));
    }

    /// <summary>
    /// Whether this body is built with a heart that Wolfmed models: its body prototype puts an organ carrying
    /// both a heart and Wolfmed organ health in some slot. A species that never had one is not refused for
    /// lacking it.
    /// </summary>
    public bool ExpectsHeart(EntityUid body)
    {
        if (!TryComp(body, out BodyComponent? comp) || comp.Prototype is not { } id ||
            !_prototypes.TryIndex<BodyPrototype>(id, out var prototype))
            return false;

        var heart = _factory.GetComponentName(typeof(HeartComponent));
        var organ = _factory.GetComponentName(typeof(WolfmedOrganComponent));
        foreach (var slot in prototype.Slots.Values)
        {
            foreach (var organId in slot.Organs.Values)
            {
                if (_prototypes.TryIndex<EntityPrototype>(organId, out var organProto) &&
                    organProto.Components.ContainsKey(heart) && organProto.Components.ContainsKey(organ))
                    return true;
            }
        }

        return false;
    }

    /// <summary>The paddles' chance to restart the heart.</summary>
    // On the arrest clock it scales with the oxygen left in the brain, which is what makes speed matter. A corpse has
    // no circulation to raise that number, so once its blood is put back it gets the flat base chance (playtest 5: a
    // destroyed brain no longer zeroes it; the patient pays in brain damage instead).
    public float GetChance(EntityUid body)
    {
        if (_life.GetBrainOrgan(body) is not { } organ ||
            organ.Comp.Health <= FixedPoint2.Zero && _shutdown.IsMechanical(body))
            return 0f;

        var chance = _cfg.GetCVar(WolfmedCVars.DefibChance);
        if (_mobState.IsDead(body))
            return Math.Clamp(chance, 0f, 1f);

        var floor = Math.Clamp(_cfg.GetCVar(WolfmedCVars.DefibOxygenationFloor), 0f, 1f);
        var oxygen = Math.Clamp(_life.GetOxygenation(body), 0f, 1f);
        return Math.Clamp(chance * (floor + (1f - floor) * oxygen), 0f, 1f);
    }

    /// <summary>Restarts the heart after a successful shock and runs the post-shock course.</summary>
    // The first shock of an arrest episode also lifts oxygenation to PostShockOxygenation and opens the grace in which
    // the blood and oxygen triggers wait. Another success within PostShockRepeatSeconds only restarts the heart, unless
    // the patient recovered in between. Consciousness decides the rest: with blood above the Downed line the patient
    // comes round Downed at once.
    public void Revive(EntityUid body)
    {
        var wasDead = _mobState.IsDead(body);
        var cause = CompOrNull<WolfmedCardiacArrestComponent>(body)?.Cause ?? string.Empty;
        _life.EndArrest(body);

        // Playtest 5: a destroyed brain comes back as major brain damage rather than staying dead for a surgeon. The
        // organ is set before the mob state moves, or the organ system would kill the body again on its next look.
        if (!_shutdown.IsMechanical(body) && _life.GetBrainOrgan(body) is { } brain && brain.Comp.Health <= FixedPoint2.Zero)
        {
            var floor = Math.Clamp(_cfg.GetCVar(WolfmedCVars.ReviveBrainFloor), 0.01f, 1f);
            _organs.SetHealth(brain, FixedPoint2.Max(FixedPoint2.New(0.01), FixedPoint2.New(brain.Comp.MaxHealth.Float() * floor)));
        }

        // Pre-merge review: a body that died since the last shock is a fresh episode, whatever the window says.
        var repeat = !wasDead && TryComp(body, out WolfmedPostShockComponent? previous) &&
                     previous.SinceRestore < _life.PostShockRepeatSeconds;

        if (!repeat)
        {
            // A body that had already died comes back on what the paddles put into it and nothing else.
            var restored = _life.PostShockOxygenation;
            _life.SetOxygenation(body, wasDead ? restored : MathF.Max(_life.GetOxygenation(body), restored));

            var post = EnsureComp<WolfmedPostShockComponent>(body);
            post.SinceRestore = 0f;
            post.GraceSeconds = MathF.Max(0f, _cfg.GetCVar(WolfmedCVars.PostShockGraceSeconds));
            post.Cause = wasDead ? "dead" : cause;
        }

        if (wasDead && _mobState.HasState(body, MobState.Critical))
            _mobState.ChangeMobState(body, MobState.Critical);

        // One zero-length tick so the hypoxia pressure is rewritten before consciousness reads it. On a
        // repeat shock with nothing fixed, this is also the tick that stops the heart again.
        _life.Tick(body, 0.0001f);
        _consciousness.Refresh(body);
    }

    /// <summary>
    /// Offers a revived body's ghost the way back, the prompt the hand defibrillator already opens (M1a, plan
    /// §5.4 item 6), so the Succumb dialog's promise holds for a pod revival too. True when a prompt opened.
    /// </summary>
    public bool OfferReturn(EntityUid body)
    {
        if (!_mind.TryGetMind(body, out _, out var mind) || mind.CurrentEntity == body ||
            !_player.TryGetSessionById(mind.UserId, out var session))
            return false;

        _eui.OpenEui(new ReturnToBodyEui(mind, _mind, _player), session);
        return true;
    }

    /// <summary>
    /// M2 (plan §7.2): why a dead chassis's restart button will not bring it back, or null when it will. It reads what
    /// a machine needs to run, never a damage total: a core in the chassis that is not destroyed, a head, a charged
    /// cell and a pump. Rot and other content's <see cref="UnrevivableComponent"/> still stop it.
    /// </summary>
    public string? GetRestartRefusal(EntityUid body)
    {
        if (TerminatingOrDeleted(body) || !OwnsRevival(body))
            return NotMonitored;

        if (_rotting.IsRotten(body))
            return Rotten;

        if (TryComp(body, out UnrevivableComponent? unrevivable))
            return unrevivable.ReasonMessage;

        if (!_body.GetBodyChildrenOfType(body, BodyPartType.Head).Any())
            return RestartNoHead;

        if (!_life.HasBrain(body))
            return RestartNoCore;

        if (_life.GetBrainOrgan(body) is not { } core || core.Comp.Health <= FixedPoint2.Zero)
            return RestartCoreDestroyed;

        if (!_shutdown.HasPump(body))
            return RestartNoPump;

        return _shutdown.HasPower(body) ? null : RestartNoPower;
    }

    public const string RestartNoHead = "wolfmed-restart-no-head";
    public const string RestartNoCore = "wolfmed-restart-no-core";
    public const string RestartCoreDestroyed = "wolfmed-restart-core-destroyed";
    public const string RestartNoPump = "wolfmed-restart-no-pump";
    public const string RestartNoPower = "wolfmed-restart-no-power";

    /// <summary>
    /// M2 (plan §7.2): the marked restart-button hook's hand-off. False when Wolfmed does not decide this body, and the
    /// button's own damage check runs. Otherwise the chassis comes back, or the button buzzes and says why not.
    /// </summary>
    public bool TryRestart(EntityUid body, SoundSpecifier? buzz = null)
    {
        if (!OwnsRevival(body) || !_mobState.IsDead(body))
            return false;

        if (GetRestartRefusal(body) is { } refusal)
        {
            if (buzz != null)
                _audio.PlayPvs(buzz, body);

            _popup.PopupEntity(Loc.GetString(refusal, ("target", Identity.Entity(body, EntityManager))), body);
            return true;
        }

        Restart(body);
        return true;
    }

    /// <summary>
    /// A dead chassis back on: Critical first, then consciousness decides from its inputs, as a shock does for flesh.
    /// Consciousness's own lines tell the player; the button plays its success sound on the way to Alive.
    /// </summary>
    public void Restart(EntityUid body)
    {
        if (_mobState.HasState(body, MobState.Critical))
            _mobState.ChangeMobState(body, MobState.Critical);

        _life.Tick(body, 0.0001f);
        _consciousness.Refresh(body);
    }

    /// <summary>
    /// CPR: hands on the chest for the duration of the do-after. It buys time on the oxygenation clock and
    /// moves a little blood; it never restarts the heart and it never revives anybody on its own.
    /// </summary>
    public bool StartCpr(EntityUid body, TimeSpan duration)
    {
        if (TerminatingOrDeleted(body) || !OwnsRevival(body))
            return false;

        var cpr = EnsureComp<WolfmedCprComponent>(body);
        cpr.Ends = _timing.CurTime + duration;
        return true;
    }

    private bool Roll(float chance)
    {
        var clamped = Math.Clamp(chance, 0f, 1f);
        return ForcedRoll is { } forced ? forced < clamped : _random.Prob(clamped);
    }
}
