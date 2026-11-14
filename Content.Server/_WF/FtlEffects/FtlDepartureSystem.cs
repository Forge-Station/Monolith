using Content.Server.Shuttles.Events;
using Content.Server.Shuttles.Systems;
using Content.Shared._WF.FtlEffects;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Systems;
using Robust.Server.GameStates;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._WF.FtlEffects;

/// <summary>Publishes departure visuals for normal, docking and administrative FTL jumps.</summary>
public sealed partial class FtlDepartureSystem : EntitySystem
{
    /// <summary>How far past the hull a player may stand and still be sent an arriving ship early.</summary>
    private const float PreloadRange = 48f;

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private ISharedPlayerManager _players = default!;
    [Dependency] private PvsOverrideSystem _pvs = default!;
    [Dependency] private ShuttleSystem _shuttles = default!;
    [Dependency] private SharedTransformSystem _transforms = default!;

    private readonly HashSet<EntityUid> _convoy = new();
    private readonly HashSet<EntityUid> _followers = new();
    private readonly Dictionary<EntityUid, Preload> _preloads = new();
    private readonly List<EntityUid> _finished = new();

    public override void Initialize()
    {
        base.Initialize();
        UpdatesAfter.Add(typeof(ShuttleSystem));
        SubscribeLocalEvent<FTLComponent, FTLStartedEvent>(OnStarted);
        SubscribeLocalEvent<MapGridComponent, FTLCompletedEvent>(OnCompleted);
    }

    private void OnStarted(Entity<FTLComponent> ent, ref FTLStartedEvent args)
    {
        var started = _timing.CurTime - TimeSpan.FromSeconds(ent.Comp.StartupTime);
        Publish(ent, started, _timing.CurTime, true, false);
    }

    private void OnCompleted(Entity<MapGridComponent> ent, ref FTLCompletedEvent args)
    {
        Publish(ent, _timing.CurTime, _timing.CurTime, true, true);
    }

    /// <summary>Gives the ship and everything docked to it the same clock, with the ship as the lead.</summary>
    public void Publish(EntityUid lead, TimeSpan started, TimeSpan departure, bool entered, bool arriving)
    {
        _convoy.Clear();
        _shuttles.GetAllDockedShuttles(lead, _convoy);
        foreach (var uid in _convoy)
        {
            // A docked ship spooling up its own jump keeps its own clock.
            if (uid != lead && !entered && (TryComp<FTLComponent>(uid, out var own) && own.State == FTLState.Starting
                || !_followers.Add(uid)))
                continue;

            var follows = uid == lead ? (EntityUid?) null : lead;
            var effect = EnsureComp<FtlDepartureComponent>(uid);
            if (effect.Started == started && effect.Departure == departure && effect.Entered == entered
                && effect.Arriving == arriving && effect.Lead == follows)
                continue;

            effect.Started = started;
            effect.Departure = departure;
            effect.Entered = entered;
            effect.Arriving = arriving;
            effect.Lead = follows;
            Dirty(uid, effect);
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;
        _followers.Clear();
        var jumping = EntityQueryEnumerator<FTLComponent, MapGridComponent>();
        while (jumping.MoveNext(out var uid, out var ftl, out var grid))
        {
            if (ftl.LinkedShuttle != null)
                continue;

            if (ftl.State == FTLState.Starting)
                Publish(uid, ftl.StateTime.Start, ftl.StateTime.End, false, false);
            else if (ftl.State == FTLState.Arriving)
                SendAhead(uid, ftl, grid);
        }

        var effects = EntityQueryEnumerator<FtlDepartureComponent>();
        while (effects.MoveNext(out var uid, out var effect))
        {
            if (effect.Arriving)
            {
                if (now >= effect.Departure + TimeSpan.FromSeconds(FtlDepartureTiming.FadeDuration))
                    RemCompDeferred<FtlDepartureComponent>(uid);
                continue;
            }

            // A planet hop keeps its own ripple and ends it itself.
            if (HasComp<Content.Server._WF.Planets.Hop.WFPlanetHopComponent>(effect.Lead ?? uid))
            {
                if (effect.Entered && now >= effect.Departure + TimeSpan.FromSeconds(FtlDepartureTiming.FadeDuration))
                    RemCompDeferred<FtlDepartureComponent>(uid);
                continue;
            }

            if (!TryComp<FTLComponent>(effect.Lead ?? uid, out var ftl) ||
                ftl.State is not (FTLState.Starting or FTLState.Travelling or FTLState.Arriving))
            {
                RemCompDeferred<FtlDepartureComponent>(uid);
                continue;
            }

            if (ftl.State == FTLState.Starting)
            {
                // A ship that undocked during the spool-up stays behind.
                if (effect.Lead != null && !_followers.Contains(uid))
                    RemCompDeferred<FtlDepartureComponent>(uid);
                continue;
            }

            if (!effect.Entered)
            {
                effect.Entered = true;
                effect.Departure = now;
                Dirty(uid, effect);
            }

            if (now >= effect.Departure + TimeSpan.FromSeconds(FtlDepartureTiming.FadeDuration))
                RemCompDeferred<FtlDepartureComponent>(uid);
        }

        _finished.Clear();
        foreach (var uid in _preloads.Keys)
        {
            if (!TryComp<FTLComponent>(uid, out var ftl) || ftl.State != FTLState.Arriving)
                _finished.Add(uid);
        }
        foreach (var uid in _finished)
        {
            Release(uid);
        }
    }

    /// <summary>
    /// Sends an arriving convoy to players near its destination while it is still in hyperspace, so the whole
    /// ship is on their client when it drops out. Session overrides stay within the PVS entity budget.
    /// </summary>
    private void SendAhead(EntityUid uid, FTLComponent ftl, MapGridComponent grid)
    {
        if (!Exists(ftl.TargetCoordinates.EntityId))
            return;

        if (!_preloads.TryGetValue(uid, out var preload))
        {
            preload = new Preload();
            _shuttles.GetAllDockedShuttles(uid, preload.Grids);
            _preloads.Add(uid, preload);
        }

        var target = _transforms.ToMapCoordinates(ftl.TargetCoordinates);
        var reach = PreloadRange + grid.LocalAABB.Center.Length() + grid.LocalAABB.Size.Length() / 2f;
        foreach (var session in _players.Sessions)
        {
            if (preload.Sessions.Contains(session) || session.AttachedEntity is not { } player)
                continue;

            var xform = Transform(player);
            if (xform.MapID != target.MapId ||
                (_transforms.GetWorldPosition(xform) - target.Position).LengthSquared() > reach * reach)
                continue;

            preload.Sessions.Add(session);
            foreach (var member in preload.Grids)
            {
                _pvs.AddSessionOverride(member, session);
            }
        }
    }

    private void Release(EntityUid uid)
    {
        if (!_preloads.Remove(uid, out var preload))
            return;

        foreach (var session in preload.Sessions)
        {
            foreach (var member in preload.Grids)
            {
                _pvs.RemoveSessionOverride(member, session);
            }
        }
    }

    private sealed class Preload
    {
        public readonly HashSet<EntityUid> Grids = new();
        public readonly HashSet<ICommonSession> Sessions = new();
    }
}
