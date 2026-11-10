using System.Linq;
using Content.Server._WF.Remade.Betrayal;
using Content.Server._WF.Remade.Corruption;
using Content.Server._WF.Remade.Counterplay;
using Content.Server._WF.Remade.Director;
using Content.Server._WF.Remade.Monolith;
using Content.Server.Body.Systems;
using Content.Shared._WF.Remade;
using Content.Shared._WF.Remade.Limbs;
using Content.Shared.Atmos.Rotting;
using Content.Shared.Body.Components;
using Content.Shared.Body.Part;
using Content.Shared.Humanoid;
using Content.Shared.Jittering;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.Remade.Conversion;

/// <summary>Turns corpses into the Remade, by an Infector's proboscis or after lying on corruption. Rules from DS13-1.0's conversion.dm.</summary>
public sealed partial class WFRemadeConversionSystem : EntitySystem
{
    [Dependency] private BodySystem _body = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedJitteringSystem _jitter = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedWFRemadeLimbsSystem _limbs = default!;
    [Dependency] private WFCorruptionSystem _corruption = default!;
    [Dependency] private WFRemadeDirectorSystem _director = default!;

    // DS13 plays its "fracture" set here: bones going. Wolfmed has a bone-break set that does the same job.
    private static readonly SoundSpecifier FractureSound = new SoundCollectionSpecifier("WFWolfmedBoneCrack");
    private static readonly TimeSpan ConvertTime = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan IncubateTime = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan PulseRate = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan ScanRate = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan FreshFor = TimeSpan.FromMinutes(15);
    private const int WrithingLines = 6;
    private const int LimbsWhole = 4;
    private const float ReanimateReach = 2f;

    private TimeSpan _nextScan;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var converting = EntityQueryEnumerator<WFRemadeConvertingComponent>();
        while (converting.MoveNext(out var uid, out var comp))
        {
            UpdateConverting((uid, comp), now);
        }

        if (now < _nextScan)
            return;

        _nextScan = now + ScanRate;
        ScanCorpses(now);
    }

    /// <summary>Whether this body can still turn: a dead, whole-headed humanoid or animal not already turning.</summary>
    public bool CanConvert(EntityUid uid)
    {
        if (TerminatingOrDeleted(uid)
            || !IsTurnable(uid)
            || !_mobState.IsDead(uid)
            || HasComp<WFRemadeConvertingComponent>(uid)
            || HasComp<WFNecrotoxinProtectedComponent>(uid))
        {
            return false;
        }

        return HasHeadIfAny(uid) && (!IsAnimal(uid) || AnimalWeights(uid).Count > 0);
    }

    /// <summary>A humanoid must have a head; an animal must if it has a body, and one with no body has nothing to lose.</summary>
    private bool HasHeadIfAny(EntityUid uid)
    {
        if (!TryComp<BodyComponent>(uid, out var body))
            return !HasComp<HumanoidAppearanceComponent>(uid);

        return _body.GetBodyChildrenOfType(uid, BodyPartType.Head, body).Any();
    }

    /// <summary>Starts the throes: the corpse shakes for all to see, then rises. An instant one rises on the next update.</summary>
    public bool TryStartConversion(EntityUid corpse, EntityUid? source = null, EntityUid? reanimator = null, bool instant = false)
    {
        // An Infector with no proboscis can't.
        if (!CanConvert(corpse) || (reanimator is { } infector && !_limbs.Can(infector, WFRemadeLimbStat.Reanimate)))
            return false;

        RemComp<WFRemadeIncubatingComponent>(corpse);
        var now = _timing.CurTime;
        var converting = EnsureComp<WFRemadeConvertingComponent>(corpse);
        converting.EndsAt = instant ? now : now + ConvertTime;
        converting.NextPulse = now;
        converting.Source = source;
        converting.Reanimator = reanimator;
        return true;
    }

    private void UpdateConverting(Entity<WFRemadeConvertingComponent> ent, TimeSpan now)
    {
        if (!IsTurnable(ent)
            || !_mobState.IsDead(ent)
            || !ReanimatorHolds(ent))
        {
            RemCompDeferred<WFRemadeConvertingComponent>(ent);
            return;
        }

        if (now >= ent.Comp.EndsAt)
        {
            Finish(ent);
            return;
        }

        if (now < ent.Comp.NextPulse)
            return;

        ent.Comp.NextPulse = now + PulseRate;
        Shake(ent);
        if (_random.Prob(0.5f))
            _audio.PlayPvs(FractureSound, ent, AudioParams.Default.WithVolume(-9f).WithMaxDistance(7f));
        if (_random.Prob(0.4f))
        {
            var line = Loc.GetString($"wf-remade-writhing-{_random.Next(1, WrithingLines + 1)}", ("corpse", ent.Owner));
            _popup.PopupEntity(line, ent, PopupType.MediumCaution);
        }
    }

    private bool ReanimatorHolds(Entity<WFRemadeConvertingComponent> ent)
    {
        if (ent.Comp.Reanimator is not { } reanimator)
            return true;

        return !TerminatingOrDeleted(reanimator)
               && !_mobState.IsDead(reanimator)
               && _transform.InRange(reanimator, ent.Owner, ReanimateReach);
    }

    /// <summary>The corpse bursts and what was in it stands up. Gear falls, and any mind in the body goes to its ghost.</summary>
    private void Finish(Entity<WFRemadeConvertingComponent> ent)
    {
        if (!CanFinish(ent))
        {
            RemCompDeferred<WFRemadeConvertingComponent>(ent);
            return;
        }

        var coordinates = _transform.GetMoverCoordinates(ent);
        var remade = Spawn(Pick(ent), coordinates);
        _director.Enlist(remade, ent.Comp.Source ?? _director.NearestMonolith(ent));
        var risen = new WFRemadeRisenEvent(remade, ent);
        RaiseLocalEvent(remade, ref risen, true);

        // A head that was turning the body goes into it.
        if (ent.Comp.Reanimator is { } head && HasComp<WFTakesBodyComponent>(head))
            QueueDel(head);

        Gib(ent);
    }

    private bool CanFinish(EntityUid uid)
    {
        return HasHeadIfAny(uid) && (!IsAnimal(uid) || AnimalWeights(uid).Count > 0);
    }

    /// <summary>Picks what a corpse rises as, weighted by its compatibility.</summary>
    public EntProtoId Pick(EntityUid corpse)
    {
        var weights = IsAnimal(corpse) ? AnimalWeights(corpse) : Weights(corpse);
        var roll = _random.NextFloat(0f, weights.Values.Sum());
        foreach (var (mob, weight) in weights)
        {
            roll -= weight;
            if (roll <= 0f)
                return mob.Entity;
        }

        return weights.Keys.Last().Entity;
    }

    /// <summary>Fresh bodies take better and bodies in pieces take worse.</summary>
    public float Compatibility(EntityUid corpse)
    {
        var compatibility = 1f;
        if (!TryComp<PerishableComponent>(corpse, out var perishable) || perishable.RotAccumulator < FreshFor)
            compatibility += 0.25f;

        if (TryComp<BodyComponent>(corpse, out var body))
        {
            var limbs = _body.GetBodyChildrenOfType(corpse, BodyPartType.Arm, body).Count()
                        + _body.GetBodyChildrenOfType(corpse, BodyPartType.Leg, body).Count();
            compatibility -= 0.075f * Math.Max(0, LimbsWhole - limbs);
        }

        return Math.Max(compatibility, 0.1f);
    }

    /// <summary>Starts corpses on corruption incubating, stops those moved off it, and turns the ones whose time is up.</summary>
    private void ScanCorpses(TimeSpan now)
    {
        var incubating = EntityQueryEnumerator<WFRemadeIncubatingComponent, TransformComponent>();
        while (incubating.MoveNext(out var uid, out var comp, out var xform))
        {
            if (!CanConvert(uid) || !_corruption.IsOnCorruption(uid, xform))
            {
                RemCompDeferred<WFRemadeIncubatingComponent>(uid);
                continue;
            }

            if (now >= comp.TurnsAt)
                TryStartConversion(uid, comp.Source);
        }

        // Humanoids and animals alike; CanConvert settles which bodies count.
        var bodies = EntityQueryEnumerator<MobStateComponent, TransformComponent>();
        while (bodies.MoveNext(out var uid, out var state, out var xform))
        {
            if (state.CurrentState != MobState.Dead
                || HasComp<WFRemadeIncubatingComponent>(uid)
                || !CanConvert(uid)
                || !_corruption.IsOnCorruption(uid, xform))
            {
                continue;
            }

            var comp = AddComp<WFRemadeIncubatingComponent>(uid);
            comp.TurnsAt = now + IncubateTime;
            comp.Source = _director.NearestMonolith(uid);
        }
    }
}
