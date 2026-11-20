using System.Linq;
using System.Numerics;
using Content.Server._WF.Remade.Monolith;
using Content.Shared._WF.Remade;
using Content.Shared.Doors.Components;
using Content.Shared.SurveillanceCamera.Components;
using Robust.Shared.Audio;
using Robust.Shared.Map;
using Robust.Shared.Random;

namespace Content.Server._WF.Remade.Director;

/// <summary>
/// Omens. Knocks on a shut door near a crewman, with now and then a Remade waiting behind it; scraping and breathing that moves
/// along the walls beside them; a Remade on a camera feed in a room nobody is in; and the lights stuttering just before a wave.
/// </summary>
public sealed partial class WFRemadeDirectorSystem
{
    private const float KnockMin = 3f;
    private const float KnockMax = 9f;
    private const float KnockGap = 0.75f;
    private const float CrawlStep = 1.4f;
    private const float CrawlGap = 0.55f;
    private const int CrawlSteps = 6;
    private const float CameraReach = 5f;
    private const float BrownoutRange = 25f;
    private const string PhantomProto = "WFRemadeCameraPhantom";

    private static readonly SoundSpecifier KnockSound = new SoundCollectionSpecifier("WFBetrayalKnocks");
    private static readonly SoundSpecifier CrawlScrape = new SoundCollectionSpecifier("WFBetrayalScrapes");
    private static readonly SoundSpecifier CrawlBreath = new SoundCollectionSpecifier("WFBetrayalBreaths");

    /// <summary>Things the omens do a moment later: the next knock, the next scrape along the wall, the next stutter.</summary>
    private readonly List<(TimeSpan At, Action Do)> _omens = new();

    private void UpdateOmens(TimeSpan now)
    {
        for (var i = _omens.Count - 1; i >= 0; i--)
        {
            if (now < _omens[i].At)
                continue;

            var action = _omens[i].Do;
            _omens.RemoveAt(i);
            action();
        }
    }

    private void Later(TimeSpan now, float seconds, Action action)
    {
        _omens.Add((now + TimeSpan.FromSeconds(seconds), action));
    }

    /// <summary>Three slow knocks on a shut door near the crewman. From Harvest, now and then something is behind it.</summary>
    public bool Knock(Entity<WFRemadeDirectorComponent, WFMonolithComponent> ent, EntityUid focus, TimeSpan now)
    {
        var here = WorldPos(focus);
        var doors = new HashSet<Entity<DoorComponent>>();
        _lookup.GetEntitiesInRange(Transform(focus).Coordinates, KnockMax, doors);
        foreach (var door in doors.OrderBy(_ => _random.Next()))
        {
            if (door.Comp.State != DoorState.Closed || (WorldPos(door) - here).Length() < KnockMin)
                continue;

            var at = Transform(door).Coordinates;
            for (var k = 0; k < 3; k++)
            {
                var uid = door.Owner;
                Later(now, k * KnockGap, () =>
                {
                    if (TerminatingOrDeleted(uid))
                        return;

                    _audio.PlayPvs(KnockSound, uid, AudioParams.Default.WithVolume(-2f));
                    _jitter.DoJitter(uid, TimeSpan.FromSeconds(0.3), true, 3f, 8f, true);
                });
            }

            if (ent.Comp2.Stage >= WFMonolithStage.Harvest && _random.Prob(0.2f))
                WaitBehind(ent, focus, at);

            return true;
        }

        return false;
    }

    /// <summary>Lays a Remade still and quiet on the far side of a door from the crewman.</summary>
    private void WaitBehind(Entity<WFRemadeDirectorComponent, WFMonolithComponent> ent, EntityUid focus, EntityCoordinates door)
    {
        var away = _transform.ToMapCoordinates(door).Position - WorldPos(focus);
        if (away.LengthSquared() < 0.01f)
            return;

        var spot = OnGrid(new MapCoordinates(_transform.ToMapCoordinates(door).Position + Vector2.Normalize(away) * 1.5f,
            Transform(focus).MapID));
        if (!OpenFloor(spot) || ent.Comp1.Horde.Count >= HordeCap(ent.Comp2.Stage) || CountDead(ent.Comp1) >= MaxDead(ent.Comp2.Stage)
            || Buy(ent, focus, false) is not { } proto)
        {
            return;
        }

        var remade = Spawn(proto, spot);
        Enlist(remade, ent.Owner);
        PlayDead(remade);
        Comp<WFRemadePlayingDeadComponent>(remade).WakeRadius = 2.5f;
        _appearance.SetData(remade, Content.Shared.Mobs.MobStateVisuals.State, Content.Shared.Mobs.MobState.Alive);
    }

    /// <summary>Scraping and breathing that moves along the nearest wall beside the crewman, step by step.</summary>
    public bool Crawl(EntityUid focus, TimeSpan now)
    {
        var xform = Transform(focus);
        var here = WorldPos(focus);
        var along = _random.NextAngle().ToVec();
        var side = new Vector2(-along.Y, along.X) * (_random.Prob(0.5f) ? 1f : -1f);

        // The wall: the first blocked tile out to the side.
        Vector2? wall = null;
        for (var k = 1; k <= 4; k++)
        {
            var probe = here + side * k;
            if (!OpenFloor(OnGrid(new MapCoordinates(probe, xform.MapID))))
            {
                wall = probe;
                break;
            }
        }

        if (wall is not { } start)
            return false;

        start -= along * CrawlStep * CrawlSteps / 2f;
        var map = xform.MapID;
        for (var k = 0; k < CrawlSteps; k++)
        {
            var at = OnGrid(new MapCoordinates(start + along * CrawlStep * k, map));
            var sound = k % 3 == 2 ? CrawlBreath : CrawlScrape;
            Later(now, k * CrawlGap, () =>
            {
                if (TerminatingOrDeleted(at.EntityId) || TerminatingOrDeleted(focus))
                    return;

                _audio.PlayStatic(sound, focus, at, AudioParams.Default.WithVolume(-5f).WithMaxDistance(10f));
            });
        }

        return true;
    }

    /// <summary>A Remade stands for a moment in front of a camera someone is watching, where no crewman can see it.</summary>
    public bool CameraLie(Entity<WFRemadeDirectorComponent, WFMonolithComponent> ent)
    {
        var origin = _transform.GetMapCoordinates(ent.Owner);
        var cameras = EntityQueryEnumerator<SurveillanceCameraComponent, TransformComponent>();
        while (cameras.MoveNext(out var uid, out var camera, out var xform))
        {
            if (camera.ActiveViewers.Count == 0 && camera.ActiveMonitors.Count == 0 || !camera.Active || xform.MapID != origin.MapId)
                continue;

            if ((_transform.GetWorldPosition(xform) - origin.Position).Length() > ent.Comp1.AwakeRadius * 2f)
                continue;

            for (var attempt = 0; attempt < 6; attempt++)
            {
                var spot = OpenSpotNear(xform.Coordinates, CameraReach);
                if (!OpenFloor(spot) || SeenByCrew(spot, CrewSight) || !_examine.InRangeUnOccluded(uid, _transform.ToMapCoordinates(spot), CameraReach + 1f))
                    continue;

                var phantom = Spawn(PhantomProto, spot);
                _transform.SetWorldRotation(phantom, (_transform.GetWorldPosition(xform) - _transform.ToMapCoordinates(spot).Position).ToWorldAngle());
                return true;
            }
        }

        return false;
    }

    /// <summary>The lights round the prey stutter three times over a few seconds: a wave is coming.</summary>
    private void Brownout(EntityUid prey, TimeSpan now)
    {
        var coordinates = Transform(prey).Coordinates;
        for (var k = 0; k < 3; k++)
        {
            Later(now, k * 1.4f, () =>
            {
                if (TerminatingOrDeleted(coordinates.EntityId))
                    return;

                _lightsNear.Clear();
                _lookup.GetEntitiesInRange(coordinates, BrownoutRange, _lightsNear);
                foreach (var light in _lightsNear)
                {
                    _ghost.DoGhostBooEvent(light);
                }
            });
        }
    }
}
