using Content.Server._WF.Remade.Conversion;
using Content.Server._WF.Remade.Corruption;
using Content.Server._WF.Remade.Director;
using Content.Server.Bed.Components;
using Content.Server.Storage.Components;
using Content.Shared._WF.Remade;
using Content.Shared._WF.Remade.Dread;
using Content.Shared.Buckle.Components;
using Content.Shared.Jittering;
using Content.Shared.Medical.Cryogenics;
using Content.Shared.Morgue.Components;
using Content.Shared.Popups;
using Content.Shared.Storage.EntitySystems;
using Content.Shared.Tag;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Content.Server._WF.Remade.Monolith;

namespace Content.Server._WF.Remade.Morgue;

/// <summary>
/// The dead in the cold store go first. A body in a morgue tray, body bag, cryo pod or stasis bed on a ship with a stone at Hymn or
/// above, or within its reach, turns on a short timer with no corruption under it: it knocks from inside, then the tray bursts open
/// and the Remade climbs out. The same rules as any corpse apply (a head, no necrotoxin). A loaded tray also draws an Infector.
/// </summary>
public sealed class WFMorgueSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedEntityStorageSystem _storage = default!;
    [Dependency] private SharedJitteringSystem _jitter = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private TagSystem _tag = default!;
    [Dependency] private WFCorruptionSystem _corruption = default!;
    [Dependency] private WFRemadeConversionSystem _conversion = default!;
    [Dependency] private WFRemadeDirectorSystem _director = default!;

    /// <summary>Seconds a body takes to turn in the cold store, against ninety on corruption and twelve in the throes.</summary>
    public const float TurnMin = 14f;
    public const float TurnMax = 24f;

    /// <summary>Seconds before the burst that it starts knocking.</summary>
    private const float KnockLead = 9f;

    /// <summary>Seconds between the tray bursting and the body standing.</summary>
    private const float RiseDelay = 1.5f;

    private const float FeedBiomass = 15f;
    private const float CallReach = 2.4f;
    private const float CallGiveUp = 90f;
    private const float CallEvery = 8f;

    private static readonly TimeSpan Rate = TimeSpan.FromSeconds(0.5);
    private static readonly TimeSpan ScanRate = TimeSpan.FromSeconds(2);
    private static readonly ProtoId<TagPrototype> BodyBagTag = "BodyBag";
    private static readonly SoundSpecifier KnockSound = new SoundCollectionSpecifier("WFEnvKnock");
    private static readonly SoundSpecifier BurstSound = new SoundCollectionSpecifier("WFEnvBurst");
    private static readonly SoundSpecifier PrySound = new SoundCollectionSpecifier("WFRemadeInfectorWork");
    private static readonly EntProtoId Gore = "WFEnvGoreSpray";

    private TimeSpan _nextTick;
    private TimeSpan _nextScan;
    private TimeSpan _nextCall;
    private readonly List<(EntityUid Uid, WFMonolithComponent Comp)> _stones = new();
    private readonly List<(EntityUid Body, EntityUid Holder)> _bodies = new();

    /// <summary>Bodies that have burst out of the cold store so far. Tests read it.</summary>
    public int Bursts;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        if (now < _nextTick)
            return;

        _nextTick = now + Rate;

        _stones.Clear();
        var stones = EntityQueryEnumerator<WFMonolithComponent>();
        while (stones.MoveNext(out var uid, out var monolith))
        {
            if (monolith.Stage >= WFMonolithStage.Hymn)
                _stones.Add((uid, monolith));
        }

        if (now >= _nextScan)
        {
            _nextScan = now + ScanRate;
            Scan(now);
        }

        var turning = EntityQueryEnumerator<WFMorgueTurningComponent>();
        while (turning.MoveNext(out var uid, out var comp))
        {
            Advance((uid, comp), now);
        }

        if (now >= _nextCall)
        {
            _nextCall = now + TimeSpan.FromSeconds(CallEvery);
            CallInfectors(now);
        }

        var calls = EntityQueryEnumerator<WFMorgueCallComponent, TransformComponent>();
        while (calls.MoveNext(out var uid, out var call, out var xform))
        {
            UpdateCall((uid, call), xform, now);
        }
    }

    /// <summary>Marks the bodies in cold storage that a stone reaches, and drops the mark from any it no longer does.</summary>
    private void Scan(TimeSpan now)
    {
        if (_stones.Count == 0)
            return;

        _bodies.Clear();
        var storages = EntityQueryEnumerator<EntityStorageComponent>();
        while (storages.MoveNext(out var uid, out var storage))
        {
            if (!HasComp<MorgueComponent>(uid) && !_tag.HasTag(uid, BodyBagTag))
                continue;

            foreach (var body in storage.Contents.ContainedEntities)
            {
                _bodies.Add((body, uid));
            }
        }

        var pods = EntityQueryEnumerator<CryoPodComponent>();
        while (pods.MoveNext(out var uid, out var pod))
        {
            if (pod.BodyContainer?.ContainedEntity is { } body)
                _bodies.Add((body, uid));
        }

        var beds = EntityQueryEnumerator<StasisBedComponent, StrapComponent>();
        while (beds.MoveNext(out var uid, out _, out var strap))
        {
            foreach (var body in strap.BuckledEntities)
            {
                _bodies.Add((body, uid));
            }
        }

        foreach (var (body, holder) in _bodies)
        {
            if (HasComp<WFMorgueTurningComponent>(body) || !_conversion.CanConvert(body) || ReachedBy(holder) is not { } stone)
                continue;

            var comp = AddComp<WFMorgueTurningComponent>(body);
            comp.Stone = stone;
            comp.Holder = holder;
            comp.TurnsAt = now + TimeSpan.FromSeconds(_random.NextFloat(TurnMin, TurnMax));
            comp.NextKnock = comp.TurnsAt - TimeSpan.FromSeconds(KnockLead);
        }
    }

    /// <summary>The stone that reaches a place: one at Hymn or above on its grid, or one whose influence falls on it.</summary>
    public EntityUid? ReachedBy(EntityUid thing)
    {
        var grid = Transform(thing).GridUid;
        if (_corruption.IsSanctuary(grid))
            return null;

        var here = _transform.GetMapCoordinates(thing);
        EntityUid? best = null;
        var bestDistance = float.MaxValue;
        foreach (var (uid, monolith) in _stones)
        {
            var there = _transform.GetMapCoordinates(uid);
            var sameMap = there.MapId == here.MapId;
            var distance = sameMap ? (there.Position - here.Position).Length() : float.MaxValue;
            var reaches = grid != null && Transform(uid).GridUid == grid
                          || WFDreadMath.Influence(monolith.Stage, distance, sameMap, false) > 0f;
            if (reaches && distance < bestDistance)
            {
                best = uid;
                bestDistance = distance;
            }
        }

        return best;
    }

    private void Advance(Entity<WFMorgueTurningComponent> ent, TimeSpan now)
    {
        var comp = ent.Comp;
        if (TerminatingOrDeleted(comp.Holder)
            || TerminatingOrDeleted(comp.Stone)
            || !_conversion.CanConvert(ent)
            || comp.RisesAt == null && !InCold(ent, comp.Holder))
        {
            RemCompDeferred<WFMorgueTurningComponent>(ent);
            return;
        }

        if (comp.RisesAt is { } rises)
        {
            if (now >= rises)
            {
                RemCompDeferred<WFMorgueTurningComponent>(ent);
                _conversion.TryStartConversion(ent, comp.Stone, instant: true);
            }

            return;
        }

        // Until the stone has built up, the body only knocks, and slower.
        var held = _director.HoldingBack(comp.Stone, now);
        if (now >= comp.TurnsAt)
        {
            if (!held)
            {
                Burst(ent, now);
                return;
            }

            comp.TurnsAt = now + TimeSpan.FromSeconds(5);
        }

        if (now >= comp.NextKnock)
        {
            comp.NextKnock = now + TimeSpan.FromSeconds(held ? _random.NextFloat(5f, 12f) : _random.NextFloat(1.2f, 2.2f));
            _audio.PlayPvs(KnockSound, comp.Holder);
            _jitter.DoJitter(comp.Holder, TimeSpan.FromSeconds(0.5), true, 6f, 14f, true);
            if (_random.Prob(0.25f))
                _popup.PopupEntity(Loc.GetString("wf-remade-morgue-knock", ("tray", comp.Holder)), comp.Holder, PopupType.MediumCaution);
        }
    }

    /// <summary>Whether the body is still where the stone found it.</summary>
    private bool InCold(EntityUid body, EntityUid holder)
    {
        if (_container.TryGetContainingContainer(body, out var container) && container.Owner == holder)
            return true;

        if (!TryComp<StrapComponent>(holder, out var strap))
            return false;

        foreach (var buckled in strap.BuckledEntities)
        {
            if (buckled == body)
                return true;
        }

        return false;
    }

    /// <summary>The tray pops open and the body is out of it, still dead for a breath, then it turns at once.</summary>
    private void Burst(Entity<WFMorgueTurningComponent> ent, TimeSpan now)
    {
        var comp = ent.Comp;
        if (HasComp<EntityStorageComponent>(comp.Holder))
            _storage.OpenStorage(comp.Holder);

        if (_container.IsEntityInContainer(ent))
            _container.TryRemoveFromContainer(ent.Owner, true);

        comp.RisesAt = now + TimeSpan.FromSeconds(RiseDelay);
        Bursts++;
        _audio.PlayPvs(BurstSound, comp.Holder);
        _popup.PopupEntity(Loc.GetString("wf-remade-morgue-burst", ("tray", comp.Holder)), comp.Holder, PopupType.LargeCaution);
        _jitter.DoJitter(ent, TimeSpan.FromSeconds(RiseDelay + 0.5f), true, 10f, 18f, true);
        Spawn(Gore, Transform(comp.Holder).Coordinates);
        _director.Feed(comp.Stone, FeedBiomass);
    }

    /// <summary>A stone's Director sends a free Infector to a tray whose body is still turning.</summary>
    private void CallInfectors(TimeSpan now)
    {
        var turning = EntityQueryEnumerator<WFMorgueTurningComponent>();
        while (turning.MoveNext(out _, out var comp))
        {
            if (comp.RisesAt != null || !TryComp<WFMonolithComponent>(comp.Stone, out var monolith) || monolith.Stage < WFMonolithStage.Harvest)
                continue;

            if (HasCall(comp.Holder) || _director.SendInfector(comp.Stone, Transform(comp.Holder).Coordinates) is not { } infector)
                continue;

            var call = EnsureComp<WFMorgueCallComponent>(infector);
            call.Holder = comp.Holder;
            call.GiveUpAt = now + TimeSpan.FromSeconds(CallGiveUp);
        }
    }

    private bool HasCall(EntityUid holder)
    {
        var calls = EntityQueryEnumerator<WFMorgueCallComponent>();
        while (calls.MoveNext(out _, out var call))
        {
            if (call.Holder == holder)
                return true;
        }

        return false;
    }

    private void UpdateCall(Entity<WFMorgueCallComponent> ent, TransformComponent xform, TimeSpan now)
    {
        var holder = ent.Comp.Holder;
        if (TerminatingOrDeleted(holder) || now >= ent.Comp.GiveUpAt)
        {
            RemCompDeferred<WFMorgueCallComponent>(ent);
            return;
        }

        if (!_transform.InRange(xform.Coordinates, Transform(holder).Coordinates, CallReach))
            return;

        // It pries the tray open, and what is inside is not waiting any longer.
        RemCompDeferred<WFMorgueCallComponent>(ent);
        _audio.PlayPvs(PrySound, ent);
        if (!TryComp<EntityStorageComponent>(holder, out var storage))
            return;

        foreach (var body in storage.Contents.ContainedEntities)
        {
            if (TryComp<WFMorgueTurningComponent>(body, out var turning) && turning.RisesAt == null)
                turning.TurnsAt = now;
        }
    }
}
