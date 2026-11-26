using System.Linq;
using Content.Server._WF.Remade.Conversion;
using Content.Server._WF.Remade.Monolith;
using Content.Server._WF.Remade.Vents;
using Content.Shared._WF.Remade;
using Content.Shared.Damage;
using Content.Shared.Jittering;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Robust.Shared.Audio;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._WF.Remade.Director;

/// <summary>
/// Vent runs and vent ambushes. A small, fast Remade is bought and sent from one vent to another through the ducts, rattling the
/// ones it passes; it either bursts out hunting or stays in the exit vent and waits for a crewman to come within a few steps,
/// tells him with a rattle, and bursts out at him with a leap. The vent it leaves is dented.
/// </summary>
public sealed partial class WFRemadeDirectorSystem
{
    [Dependency] private SharedJitteringSystem _jitter = default!;

    /// <summary>How close a crewman walks to a lair before it bursts.</summary>
    public const float AmbushRadius = 3f;

    private const float AmbushTell = 0.7f;
    private const float AmbushWait = 240f;
    private const float VentDamage = 30f;
    private const float VentRunMin = 4f;
    private const float VentRunMax = 28f;

    private static readonly SoundSpecifier TellSound = new SoundCollectionSpecifier("WFEnvKnock");

    /// <summary>The small, fast Remade that go through the vents.</summary>
    private static readonly EntProtoId[] VentRunners =
    {
        "WFRemadeHopper", "WFRemadeHopperFennec", "WFRemadeSlasher", "WFRemadeLeaper", "WFRemadeLurker", "WFRemadeTwitcher",
    };

    private readonly HashSet<Entity<ActorComponent>> _lurkNear = new();

    /// <summary>The most ambushers a Monolith keeps lying in vents at once, by stage.</summary>
    public static int MaxAmbushers(WFMonolithStage stage)
    {
        return stage switch
        {
            WFMonolithStage.Growth => 1,
            WFMonolithStage.Harvest => 2,
            WFMonolithStage.Rapture => 3,
            WFMonolithStage.Crescendo => 4,
            _ => 0,
        };
    }

    /// <summary>
    /// Buys a runner and sends it through the vents to one near a crewman, who is not shaken. With <paramref name="ambush"/> it lies in
    /// wait there. False if the horde is full, no vents fit, or it can't pay.
    /// </summary>
    public bool TryVentRun(Entity<WFRemadeDirectorComponent, WFMonolithComponent> ent, bool ambush, TimeSpan now)
    {
        var director = ent.Comp1;
        var stage = ent.Comp2.Stage;
        if (stage < WFMonolithStage.Growth || director.Horde.Count >= HordeCap(stage) || CountSector() >= SectorCap)
            return false;

        GatherCrew(ent, director.AwakeRadius);
        var options = _crew.Where(crewman => StressOf(crewman) < director.StressHigh && Transform(crewman).GridUid != null).ToList();
        if (options.Count == 0)
            return false;

        var prey = _random.Pick(options);
        var grid = Transform(prey).GridUid!.Value;
        var here = WorldPos(prey);
        var vents = VentsOn(grid);
        var exits = vents.Where(vent => (WorldPos(vent) - here).Length() is >= VentRunMin and <= VentRunMax).ToList();
        if (exits.Count == 0)
            return false;

        if (ambush && CountAmbushers(ent.Owner) >= MaxAmbushers(stage))
            ambush = false;

        var exit = _random.Pick(exits);
        var origins = vents
            .Where(vent => vent != exit && VentDistance(vent, exit) >= TripMin && !SeenByCrew(Transform(vent).Coordinates, CrewSight))
            .ToList();
        if (origins.Count == 0)
            return false;

        var origin = _random.Pick(origins);
        if (BuyRunner(ent) is not { } mob)
            return false;

        var remade = Spawn(mob.Entity, Transform(origin).Coordinates);
        Enlist(remade, ent.Owner);
        EnsureComp<WFRemadeBehaviourComponent>(remade).AfterVent = WFRemadeMode.Hunt;
        if (!TryVentTravel(remade, origin, exit, Transform(exit).Coordinates, now, null))
        {
            director.Horde.Remove(remade);
            QueueDel(remade);
            director.Biomass += mob.Cost;
            return false;
        }

        Comp<WFRemadeVentTripComponent>(remade).Ambush = ambush;
        var state = EnsureComp<WFVentStateComponent>(ent.Owner);
        state.Runs++;
        if (ambush)
            state.Ambushes++;

        return true;
    }

    /// <summary>One runner by weight that the Monolith can pay for at its stage, paid for. Null if none.</summary>
    private WFRemadeMobPrototype? BuyRunner(Entity<WFRemadeDirectorComponent, WFMonolithComponent> ent)
    {
        var director = ent.Comp1;
        var options = _proto.EnumeratePrototypes<WFRemadeMobPrototype>()
            .Where(mob => VentRunners.Contains(mob.Entity)
                          && mob.SpawnWeight > 0f
                          && ent.Comp2.Stage >= mob.MinStage
                          && director.Biomass >= mob.Cost
                          && (mob.MaxAlive <= 0 || CountAlive(director, mob) < mob.MaxAlive))
            .ToList();
        if (options.Count == 0)
            return null;

        var pick = PickWeighted(options);
        director.Biomass -= pick.Cost;
        return pick;
    }

    /// <summary>Ambushers a Monolith has lying in vents now.</summary>
    public int CountAmbushers(EntityUid monolith)
    {
        var count = 0;
        var query = EntityQueryEnumerator<WFRemadeVentAmbushComponent>();
        while (query.MoveNext(out _, out var ambush))
        {
            if (ambush.Holder == monolith)
                count++;
        }

        return count;
    }

    /// <summary>A Remade that has come through the vents settles in the exit one to wait.</summary>
    private void BeginAmbush(EntityUid remade, EntityUid vent, EntityUid holder, TimeSpan now)
    {
        var ambush = EnsureComp<WFRemadeVentAmbushComponent>(remade);
        ambush.Vent = vent;
        ambush.At = Transform(vent).Coordinates;
        ambush.Holder = holder;
        ambush.Until = now + TimeSpan.FromSeconds(AmbushWait);
        ambush.BurstAt = null;
        ambush.Target = null;
        EnsureComp<WFRemadeBehaviourComponent>(remade).Mode = WFRemadeMode.Hidden;
    }

    /// <summary>Watches the lairs: a crewman close enough hears the vent rattle, and a moment later the thing in it comes out.</summary>
    public void UpdateAmbushes(TimeSpan now)
    {
        var query = EntityQueryEnumerator<WFRemadeVentAmbushComponent>();
        while (query.MoveNext(out var uid, out var ambush))
        {
            var vent = ambush.Vent;
            var at = TerminatingOrDeleted(vent) ? ambush.At : Transform(vent).Coordinates;
            if (ambush.BurstAt is { } burst)
            {
                if (now >= burst)
                    BurstFromVent(uid, ambush, at, now);

                continue;
            }

            if (now >= ambush.Until || TerminatingOrDeleted(vent))
            {
                BurstFromVent(uid, ambush, at, now);
                continue;
            }

            if (NearestCrewman(at, AmbushRadius) is not { } target)
                continue;

            ambush.Target = target;
            ambush.BurstAt = now + TimeSpan.FromSeconds(AmbushTell);
            _audio.PlayPvs(TellSound, vent);
            _audio.PlayPvs(CompOrNull<WFRemadeDirectorComponent>(ambush.Holder)?.RattleSound, vent);
            _jitter.DoJitter(vent, TimeSpan.FromSeconds(AmbushTell + 0.3f), true, 8f, 20f, true);
            _popup.PopupEntity(Loc.GetString("wf-remade-vent-tell"), vent, target, PopupType.MediumCaution);
        }
    }

    /// <summary>The nearest living, non-Remade player within a range of a place.</summary>
    private EntityUid? NearestCrewman(Robust.Shared.Map.EntityCoordinates at, float range)
    {
        _lurkNear.Clear();
        _lookup.GetEntitiesInRange(at, range, _lurkNear);
        var here = _transform.ToMapCoordinates(at).Position;
        EntityUid? best = null;
        var bestDistance = float.MaxValue;
        foreach (var crewman in _lurkNear)
        {
            if (HasComp<WFRemadeComponent>(crewman) || _mobState.IsDead(crewman))
                continue;

            var distance = (WorldPos(crewman) - here).LengthSquared();
            if (distance < bestDistance)
            {
                best = crewman;
                bestDistance = distance;
            }
        }

        return best;
    }

    /// <summary>It tears out of the vent, dents it, shouts, and leaps at the crewman if it can.</summary>
    private void BurstFromVent(EntityUid remade, WFRemadeVentAmbushComponent ambush, Robust.Shared.Map.EntityCoordinates at, TimeSpan now)
    {
        var holder = ambush.Holder;
        var vent = ambush.Vent;
        var target = ambush.Target is { } chosen && !TerminatingOrDeleted(chosen) && !_mobState.IsDead(chosen) ? chosen : NearestCrewman(at, AmbushRadius * 2f);
        RemCompDeferred<WFRemadeVentAmbushComponent>(remade);

        if (TerminatingOrDeleted(holder)
            || !_container.TryGetContainer(holder, HoldId, out var container)
            || !_container.Remove(remade, container, destination: at))
        {
            return;
        }

        Thaw(remade);
        DamageVent(vent);
        if (TryComp<WFRemadeDirectorComponent>(holder, out var director))
            _audio.PlayPvs(director.BurstSound, remade);

        _popup.PopupEntity(Loc.GetString("wf-remade-vent-burst"), remade, PopupType.LargeCaution);
        if (TryComp<WFRemadeVoiceComponent>(remade, out var voice))
            _voice.Shout((remade, voice));

        var behaviour = EnsureComp<WFRemadeBehaviourComponent>(remade);
        behaviour.ArrivalVent = TerminatingOrDeleted(vent) ? null : vent;
        behaviour.Mode = WFRemadeMode.Hunt;
        ClearKnown(remade, behaviour);
        if (target is { } prey)
        {
            SetKnown(remade, behaviour, Transform(prey).Coordinates, now);
            _abilities.Ambush(remade, prey);
        }

        if (TryComp<WFVentStateComponent>(holder, out var state))
            state.Bursts++;
    }

    /// <summary>Dents a vent a Remade has come out of.</summary>
    private void DamageVent(EntityUid? vent)
    {
        if (vent is not { } uid || TerminatingOrDeleted(uid))
            return;

        _damageable.TryChangeDamage(uid, new DamageSpecifier(_proto.Index(Brute), VentDamage), ignoreResistances: true, interruptsDoAfters: false);
    }
}
