using System.Numerics;
using Content.Server.Light.EntitySystems;
using Content.Shared.Light.Components;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.CavernEncounters;

/// <summary>
/// The signs that give an encounter away before you reach it: telegraph sounds played now and then while a player is
/// in earshot, and the Lamplighter's omen lights going out as a player comes near.
/// </summary>
public sealed partial class WFCavernTelegraphSystem : EntitySystem
{
    [Dependency] private HandheldLightSystem _handheld = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(0.5);

    private TimeSpan _nextCheck;
    private readonly List<MapCoordinates> _players = new();

    /// <inheritdoc/>
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        if (now < _nextCheck)
            return;

        _nextCheck = now + CheckInterval;

        _players.Clear();
        var actors = EntityQueryEnumerator<ActorComponent, TransformComponent>();
        while (actors.MoveNext(out var actor, out _, out var xform))
        {
            if (xform.MapID != MapId.Nullspace)
                _players.Add(_transform.GetMapCoordinates(actor, xform));
        }

        var telegraphs = EntityQueryEnumerator<WFCavernTelegraphComponent, TransformComponent>();
        while (telegraphs.MoveNext(out var uid, out var telegraph, out var xform))
        {
            if (now < telegraph.NextPlay)
                continue;

            if (HasComp<MobStateComponent>(uid) && !_mobState.IsAlive(uid))
                continue;

            if (!AnyPlayerWithin(_transform.GetMapCoordinates(uid, xform), telegraph.Range))
                continue;

            Play((uid, telegraph));
        }

        var omens = EntityQueryEnumerator<WFCavernOmenLightComponent, HandheldLightComponent, TransformComponent>();
        while (omens.MoveNext(out var uid, out var omen, out var light, out var xform))
        {
            if (omen.DiesAt is not { } dies)
            {
                if (light.Activated && AnyPlayerWithin(_transform.GetMapCoordinates(uid, xform), omen.TriggerRange))
                    omen.DiesAt = now + _random.Next(TimeSpan.Zero, omen.MaxDelay);

                continue;
            }

            if (now < dies)
                continue;

            _handheld.TurnOff((uid, light), makeNoise: false);
            _audio.PlayPvs(omen.Sound, uid);
            RemCompDeferred<WFCavernOmenLightComponent>(uid);
        }
    }

    /// <summary>Plays the telegraph's sound, carrying its range, and sets when it may play again.</summary>
    public void Play(Entity<WFCavernTelegraphComponent> ent)
    {
        var comp = ent.Comp;
        var now = _timing.CurTime;

        _audio.PlayPvs(comp.Sound, ent, AudioParams.Default.WithMaxDistance(comp.Range).WithVolume(comp.Volume));
        comp.LastPlayed = now;
        comp.NextPlay = now + _random.Next(comp.MinInterval, comp.MaxInterval);
    }

    /// <summary>Whether a player stands on the same map within a range of a point.</summary>
    private bool AnyPlayerWithin(MapCoordinates point, float range)
    {
        foreach (var player in _players)
        {
            if (player.MapId == point.MapId && Vector2.DistanceSquared(player.Position, point.Position) <= range * range)
                return true;
        }

        return false;
    }
}
