using Content.Server._WF.Wolfmed.Autodoc;
using Content.Server._WF.Wolfmed.Wounds;
using Content.Server.Body.Components;
using Content.Server.Chat.Systems;
using Content.Server.Medical;
using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Wolfmed.Autodoc;
using Content.Shared._WF.Wolfmed.Body;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared._WF.Wolfmed.Consciousness;
using Content.Shared._WF.Wolfmed.Life;
using Content.Shared._WF.Wolfmed.Reagents;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.Body.Components;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Chat;
using Content.Shared.Chat.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.Wolfmed.Life;

/// <summary>
/// Playtest 4 (SEPSIS): a sick body shows it. Every wolfmed.condition_emote_interval each organic wound host is checked
/// for what is wrong with it, and one condition emote goes to chat: choking, coughing, wheezing, retching, shivering.
/// </summary>
// The conditions are walked in priority order and each one that holds rolls wolfmed.condition_emote_chance; the first
// success plays, so at most one emote a check and a lower condition still shows up now and then. The emote played
// last check is passed over while another condition holds, so a septic patient alternates rather than coughing
// forever. Nothing for the dead, a stopped heart (it looks dead), a pod mid-procedure, strong sedation or a chassis.
// Unconscious bodies still choke and cough, so the emotes skip the action blocker.
public sealed class WolfmedConditionEmoteSystem : EntitySystem
{
    public static readonly ProtoId<EmotePrototype> Choke = "WFWolfmedChoke";
    public static readonly ProtoId<EmotePrototype> Cough = "WFWolfmedCough";
    public static readonly ProtoId<EmotePrototype> CoughBlood = "WFWolfmedCoughBlood";
    public static readonly ProtoId<EmotePrototype> Wheeze = "WFWolfmedWheeze";
    public static readonly ProtoId<EmotePrototype> Retch = "WFWolfmedRetch";
    public static readonly ProtoId<EmotePrototype> Shiver = "WFWolfmedShiver";

    [Dependency] private AutodocSystem _autodoc = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private VomitSystem _vomit = default!;
    [Dependency] private WolfmedBodyTemperatureSystem _temperature = default!;
    [Dependency] private WolfmedBreathingSystem _breathing = default!;
    [Dependency] private WolfmedInfectionSystem _infection = default!;
    [Dependency] private WolfmedLifeSystem _life = default!;
    [Dependency] private WolfmedPainReliefSystem _relief = default!;
    [Dependency] private WolfmedToxinSystem _toxin = default!;
    [Dependency] private WolfmedWoundTraitSystem _traits = default!;
    [Dependency] private WoundSystem _wounds = default!;

    private readonly List<EntityUid> _due = new();
    private readonly List<ProtoId<EmotePrototype>> _holding = new();
    private TimeSpan _nextCheck;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_timing.CurTime < _nextCheck)
            return;

        _nextCheck = _timing.CurTime + TimeSpan.FromSeconds(MathF.Max(1f, _cfg.GetCVar(WolfmedCVars.ConditionEmoteInterval)));
        var chance = Math.Clamp(_cfg.GetCVar(WolfmedCVars.ConditionEmoteChance), 0f, 1f);
        if (chance <= 0f)
            return;

        // Buffered: a vomit spills a puddle, and an emote runs other systems' handlers.
        _due.Clear();
        var query = EntityQueryEnumerator<WoundHostComponent>();
        while (query.MoveNext(out var uid, out _))
            _due.Add(uid);

        foreach (var body in _due)
        {
            if (!TerminatingOrDeleted(body))
                Check(body, chance);
        }
    }

    private void Check(EntityUid body, float chance)
    {
        if (!CanShow(body))
            return;

        _holding.Clear();
        Conditions(body, _holding);
        if (_holding.Count == 0)
            return;

        var last = CompOrNull<WolfmedConditionEmoteComponent>(body)?.LastEmote;
        foreach (var emote in _holding)
        {
            if (_holding.Count > 1 && emote.Id == last)
                continue;

            if (!_random.Prob(chance))
                continue;

            Play(body, emote);
            return;
        }
    }

    /// <summary>Whether this body may show a condition at all: alive, flesh, not in arrest, a working pod or deep sedation.</summary>
    public bool CanShow(EntityUid body)
    {
        if (_mobState.IsDead(body) || HasComp<WolfmedCardiacArrestComponent>(body) || !IsOrganic(body))
            return false;

        if (TryComp(body, out WolfmedAutodocOccupantComponent? occupant) &&
            TryComp(occupant.Pod, out AutodocComponent? pod) && _autodoc.IsRunning((occupant.Pod, pod)))
            return false;

        return _relief.GetSedation(body) < _cfg.GetCVar(WolfmedCVars.SedationWarnHeavy);
    }

    /// <summary>The emotes whose conditions hold right now, in priority order.</summary>
    public void Conditions(EntityUid body, List<ProtoId<EmotePrototype>> into)
    {
        // The hypoxia sub-source the analyzer names, read fresh: the one consciousness keeps outlasts the drain.
        var source = WolfmedCauseSource.None;
        if (_life.GetBrain(body) is { } brain && _life.DrainRate(body, brain, out var drain) > 0f)
            source = drain;

        var lungs = LungsFailing(body);
        var chestBleed = ChestInternalBleed(body);
        var sepsis = _infection.GetSepsis(body);

        // Airway blocked, or drowning in blood: a holed lung bleeding into the chest.
        if (source == WolfmedCauseSource.Airway || chestBleed && LungsDamagedInPlace(body))
            into.Add(Choke);

        if (lungs)
            into.Add(CoughBlood);
        else if (chestBleed || sepsis >= _cfg.GetCVar(WolfmedCVars.ConditionCoughSepsis))
            into.Add(Cough);

        // Breathing, but not enough: the lungs are the largest drain on the brain.
        if (source == WolfmedCauseSource.Lungs &&
            _life.GetOxygenation(body) < _cfg.GetCVar(WolfmedCVars.ConditionWheezeOxygenation))
            into.Add(Wheeze);

        var (toxinDown, _) = _toxin.GetLevels(body);
        if (sepsis >= _cfg.GetCVar(WolfmedCVars.ConditionRetchSepsis) || toxinDown >= 1f)
            into.Add(Retch);

        if (_infection.HasFever(body) || _temperature.GetLevels(body).ColdDown >= 1f)
            into.Add(Shiver);
    }

    private void Play(EntityUid body, ProtoId<EmotePrototype> emote)
    {
        var comp = EnsureComp<WolfmedConditionEmoteComponent>(body);
        comp.LastEmote = emote.Id;
        comp.EmoteCount++;
        _chat.TryEmoteWithChat(body, emote.Id, ChatTransmitRange.Normal, ignoreActionBlocker: true, forceEmote: true);

        if (emote != Retch || _timing.CurTime < comp.NextVomit || !BringsUp(body) ||
            _body.GetBodyOrganEntityComps<StomachComponent>(body).Count == 0)
            return;

        comp.NextVomit = _timing.CurTime + TimeSpan.FromSeconds(MathF.Max(0f, _cfg.GetCVar(WolfmedCVars.ConditionVomitInterval)));
        comp.VomitCount++;
        _vomit.Vomit(body);
    }

    /// <summary>A retch brings something up: sepsis past its vomit line, or the toxin load at the coma band.</summary>
    private bool BringsUp(EntityUid body) =>
        _infection.GetSepsis(body) >= _cfg.GetCVar(WolfmedCVars.ConditionVomitSepsis) ||
        _toxin.GetLevels(body).Out >= 1f;

    /// <summary>The torso decides, as the synthetic HUD's does: a human with one cybernetic arm is still flesh.</summary>
    private bool IsOrganic(EntityUid body)
    {
        foreach (var (part, bodyPart) in _body.GetBodyChildren(body))
        {
            if (bodyPart.PartType == BodyPartType.Torso)
                return _traits.IsOrganic(part);
        }

        return false;
    }

    /// <summary>Lungs impaired or failed, or none left in a body that breathes: the analyzer's "no lungs".</summary>
    private bool LungsFailing(EntityUid body) =>
        LungsDamagedInPlace(body) || _breathing.Assess(body).Source == WolfmedBreathingSource.Lungs;

    private bool LungsDamagedInPlace(EntityUid body)
    {
        if (!TryComp(body, out BodyComponent? bodyComp))
            return false;

        foreach (var lung in _body.GetBodyOrganEntityComps<LungComponent>((body, bodyComp)))
        {
            if (TryComp(lung.Owner, out WolfmedOrganComponent? health) && health.Band != WolfmedOrganBand.Ok)
                return true;
        }

        return false;
    }

    /// <summary>An open wound on the torso bleeding inside it.</summary>
    private bool ChestInternalBleed(EntityUid body)
    {
        foreach (var (part, bodyPart) in _body.GetBodyChildren(body))
        {
            if (bodyPart.PartType != BodyPartType.Torso || !TryComp(part, out WoundableComponent? woundable))
                continue;

            foreach (var wound in _wounds.GetWounds((part, woundable)))
            {
                if (wound.Comp.State == WoundState.Open &&
                    TryComp(wound, out WoundInternalBleedingComponent? bleeding) &&
                    bleeding.Severity > FixedPoint2.Zero)
                    return true;
            }
        }

        return false;
    }
}
