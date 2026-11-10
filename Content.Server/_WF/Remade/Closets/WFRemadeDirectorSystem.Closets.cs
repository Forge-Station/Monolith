using Content.Server._WF.Remade.Closets;
using Content.Server._WF.Remade.Conversion;
using Content.Server._WF.Remade.Monolith;
using Content.Shared._WF.Remade;
using Content.Shared.Morgue.Components;
using Content.Shared.Popups;
using Content.Shared.Storage.Components;
using Content.Server.Storage.Components;
using Robust.Shared.Audio;
using Robust.Shared.Player;
using Robust.Shared.Random;

namespace Content.Server._WF.Remade.Director;

/// <summary>
/// Closets. The Director shuts a Remade in an empty locker near a crewman; it breathes and scrapes inside, and comes out when
/// someone opens the locker or stands right beside it. A crewman hiding in a locker drops out of the Director's sight, until
/// they talk: then a Remade that finds the locker tears it open.
/// </summary>
public sealed partial class WFRemadeDirectorSystem
{
    private const float ClosetMin = 6f;
    private const float ClosetMax = 22f;
    private const float ClosetWake = 1.3f;
    private const float RattleHear = 9f;
    private const float GiveUpSeconds = 360f;
    private const float GiveUpClear = 20f;
    private const float TearReach = 1.6f;
    private const float GiveawaySeconds = 30f;

    private static readonly TimeSpan ClosetRate = TimeSpan.FromSeconds(1);
    private static readonly SoundSpecifier[] Rattles =
    {
        new SoundCollectionSpecifier("WFBetrayalBreaths"),
        new SoundCollectionSpecifier("WFBetrayalScrapes"),
        new SoundCollectionSpecifier("WFEnvKnock"),
    };

    private TimeSpan _nextCloset;

    private void InitializeClosets()
    {
        SubscribeLocalEvent<WFRemadeLurkedLockerComponent, StorageAfterOpenEvent>(OnLurkedOpened);
    }

    /// <summary>How many lockers a stone's Remade may wait in at once, by stage.</summary>
    public static int MaxLurkers(WFMonolithStage stage)
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

    private int CountLurkers(WFRemadeDirectorComponent director)
    {
        var count = 0;
        foreach (var uid in director.Horde)
        {
            if (HasComp<WFRemadeClosetLurkerComponent>(uid))
                count++;
        }

        return count;
    }

    /// <summary>Whether a crewman is hiding in a locker and has kept quiet in it.</summary>
    private bool HiddenQuietly(EntityUid crewman)
    {
        if (!_container.TryGetContainingContainer(crewman, out var container) || !HasComp<EntityStorageComponent>(container.Owner))
            return false;

        return !TryComp<WFRemadeGiveawayComponent>(container.Owner, out var giveaway) || _timing.CurTime >= giveaway.Until;
    }

    /// <summary>A crewman talking in a locker gives it away.</summary>
    private void GiveAway(EntityUid speaker)
    {
        if (!_container.TryGetContainingContainer(speaker, out var container) || !HasComp<EntityStorageComponent>(container.Owner))
            return;

        EnsureComp<WFRemadeGiveawayComponent>(container.Owner).Until = _timing.CurTime + TimeSpan.FromSeconds(GiveawaySeconds);
    }

    /// <summary>Shuts one Remade in an empty locker near a crewman. False if there is none or the stone has enough waiting.</summary>
    public bool TryLurkInCloset(Entity<WFRemadeDirectorComponent, WFMonolithComponent> ent, TimeSpan now)
    {
        var director = ent.Comp1;
        var stage = ent.Comp2.Stage;
        if (CountLurkers(director) >= MaxLurkers(stage) || director.Horde.Count >= HordeCap(stage))
            return false;

        if (PickFocus(director, now) is not { } focus || StressOf(focus) >= director.StressHigh)
            return false;

        var lockers = LockersNear(focus, ClosetMin, ClosetMax);
        if (lockers.Count == 0 || Buy(ent, focus, false) is not { } proto)
            return false;

        var locker = _random.Pick(lockers);
        var remade = Spawn(proto, Transform(locker).Coordinates);
        Enlist(remade, ent.Owner);
        PlayDead(remade);
        Comp<WFRemadePlayingDeadComponent>(remade).WakeRadius = ClosetWake;
        if (!HideInTray(remade, locker))
        {
            Del(remade);
            return false;
        }

        var lurker = EnsureComp<WFRemadeClosetLurkerComponent>(remade);
        lurker.Locker = locker;
        lurker.NextRattle = now + TimeSpan.FromSeconds(_random.NextFloat(6f, 14f));
        lurker.GiveUpAt = now + TimeSpan.FromSeconds(GiveUpSeconds);
        EnsureComp<WFRemadeLurkedLockerComponent>(locker).Lurker = remade;
        return true;
    }

    /// <summary>Shut, empty lockers on a crewman's grid between two ranges of them. Morgue trays are the fakes' business.</summary>
    private List<EntityUid> LockersNear(EntityUid near, float min, float max)
    {
        var here = WorldPos(near);
        var grid = Transform(near).GridUid;
        var lockers = new List<EntityUid>();
        var query = EntityQueryEnumerator<EntityStorageComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var storage, out var xform))
        {
            if (storage.Open || xform.GridUid != grid || HasComp<MorgueComponent>(uid) || HasComp<WFRemadeLurkedLockerComponent>(uid)
                || _container.IsEntityInContainer(uid) || storage.Contents.ContainedEntities.Count > 0)
            {
                continue;
            }

            var distance = (_transform.GetWorldPosition(xform) - here).Length();
            if (distance >= min && distance <= max)
                lockers.Add(uid);
        }

        return lockers;
    }

    private void OnLurkedOpened(Entity<WFRemadeLurkedLockerComponent> ent, ref StorageAfterOpenEvent args)
    {
        var lurker = ent.Comp.Lurker;
        RemCompDeferred<WFRemadeLurkedLockerComponent>(ent);
        if (TerminatingOrDeleted(lurker))
            return;

        RemComp<WFRemadeClosetLurkerComponent>(lurker);
        Rise(lurker, NearestPlayer(ent.Owner, 3f));
    }

    private void UpdateClosets(TimeSpan now)
    {
        if (now < _nextCloset)
            return;

        _nextCloset = now + ClosetRate;

        var lurkers = EntityQueryEnumerator<WFRemadeClosetLurkerComponent>();
        var gone = new List<EntityUid>();
        while (lurkers.MoveNext(out var uid, out var lurker))
        {
            // Out of the locker by any means, or up and about: it is an ordinary Remade now.
            if (TerminatingOrDeleted(lurker.Locker) || !HasComp<WFRemadePlayingDeadComponent>(uid)
                || !_container.TryGetContainingContainer(uid, out var container) || container.Owner != lurker.Locker)
            {
                gone.Add(uid);
                continue;
            }

            if (now >= lurker.GiveUpAt && NearestPlayer(lurker.Locker, GiveUpClear) == null)
            {
                RemComp<WFRemadeLurkedLockerComponent>(lurker.Locker);
                QueueDel(uid);
                continue;
            }

            if (now < lurker.NextRattle)
                continue;

            lurker.NextRattle = now + TimeSpan.FromSeconds(_random.NextFloat(10f, 25f));
            if (NearestPlayer(lurker.Locker, RattleHear) == null)
                continue;

            _audio.PlayPvs(_random.Pick(Rattles), lurker.Locker, AudioParams.Default.WithVolume(-4f).WithMaxDistance(RattleHear));
            _jitter.DoJitter(lurker.Locker, TimeSpan.FromSeconds(0.4), true, 4f, 10f, true);
        }

        foreach (var uid in gone)
        {
            if (TryComp<WFRemadeClosetLurkerComponent>(uid, out var lurker))
                RemComp<WFRemadeLurkedLockerComponent>(lurker.Locker);

            RemComp<WFRemadeClosetLurkerComponent>(uid);
        }

        TearGiveaways(now);
    }

    /// <summary>A Remade beside a locker someone gave away tears it open.</summary>
    private void TearGiveaways(TimeSpan now)
    {
        var near = new HashSet<Entity<WFRemadeComponent>>();
        var query = EntityQueryEnumerator<WFRemadeGiveawayComponent, EntityStorageComponent, TransformComponent>();
        var done = new List<EntityUid>();
        while (query.MoveNext(out var uid, out var giveaway, out var storage, out var xform))
        {
            if (now >= giveaway.Until || storage.Open)
            {
                done.Add(uid);
                continue;
            }

            near.Clear();
            _lookup.GetEntitiesInRange(xform.Coordinates, TearReach, near);
            foreach (var remade in near)
            {
                if (_mobState.IsIncapacitated(remade) || HasComp<WFRemadePlayingDeadComponent>(remade))
                    continue;

                _fakeStorage.OpenStorage(uid);
                _audio.PlayPvs(TrayBurst, uid);
                _popup.PopupEntity(Loc.GetString("wf-remade-closet-torn", ("locker", uid)), uid, PopupType.LargeCaution);
                done.Add(uid);
                break;
            }
        }

        foreach (var uid in done)
        {
            RemComp<WFRemadeGiveawayComponent>(uid);
        }
    }
}
