using Content.Server.Atmos.EntitySystems;
using Content.Shared._WF.PlanetVents;
using Content.Shared.Atmos;
using Robust.Server.Player;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.PlanetVents;

/// <summary>Runs <see cref="WFPlanetVentComponent"/>: rest, inhale, exhale, on a timer per vent.</summary>
public sealed class WFPlanetVentSystem : EntitySystem
{
    [Dependency] private AtmosphereSystem _atmos = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(0.25);

    private TimeSpan _nextCheck;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFPlanetVentComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(Entity<WFPlanetVentComponent> ent, ref MapInitEvent args)
    {
        // Spread the first breaths so a field of vents doesn't breathe in step.
        ent.Comp.NextPhase = _timing.CurTime + _random.Next(TimeSpan.Zero, ent.Comp.MaxRest);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;
        if (now < _nextCheck)
            return;
        _nextCheck = now + CheckInterval;

        var query = EntityQueryEnumerator<WFPlanetVentComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var vent, out var xform))
        {
            if (now < vent.NextPhase)
                continue;

            switch (vent.Phase)
            {
                case WFPlanetVentPhase.Rest:
                    if (vent.RequirePlayer && !PlayerNear(xform, vent.PlayerRange))
                    {
                        vent.NextPhase = now + Rest(vent);
                        break;
                    }

                    Inhale((uid, vent, xform));
                    vent.NextPhase = now + vent.InhaleTime;
                    break;
                case WFPlanetVentPhase.Inhale:
                    Exhale((uid, vent, xform));
                    vent.NextPhase = now + vent.ExhaleTime;
                    break;
                default:
                    SetPhase((uid, vent), WFPlanetVentPhase.Rest);
                    vent.NextPhase = now + Rest(vent);
                    break;
            }
        }
    }

    private void Inhale(Entity<WFPlanetVentComponent, TransformComponent> ent)
    {
        var (uid, vent, xform) = ent;
        SetPhase((uid, vent), WFPlanetVentPhase.Inhale);
        _audio.PlayPvs(vent.InhaleSound, uid, AudioParams.Default.WithVariation(0.1f));

        // Open ground shares the world's air, which nothing changes; only a tracked tile gives up its air.
        if (_atmos.GetTileMixture((uid, xform), excite: true) is { Immutable: false } air)
            air.Remove(vent.InhaleMoles);
    }

    private void Exhale(Entity<WFPlanetVentComponent, TransformComponent> ent)
    {
        var (uid, vent, xform) = ent;
        SetPhase((uid, vent), WFPlanetVentPhase.Exhale);
        _audio.PlayPvs(vent.ExhaleSound, uid, AudioParams.Default.WithVariation(0.1f));
        if (vent.Puff is { } puff)
            Spawn(puff, xform.Coordinates);

        if (_atmos.GetTileMixture((uid, xform), excite: true) is not { Immutable: false } air)
            return;

        var breath = new GasMixture(air.Volume) { Temperature = vent.ExhaleTemperature };
        breath.AdjustMoles(vent.Gas, vent.ExhaleMoles);
        _atmos.Merge(air, breath);
    }

    private void SetPhase(Entity<WFPlanetVentComponent> ent, WFPlanetVentPhase phase)
    {
        ent.Comp.Phase = phase;
        _appearance.SetData(ent, WFPlanetVentVisuals.Phase, phase);
    }

    private TimeSpan Rest(WFPlanetVentComponent vent)
    {
        return _random.Next(vent.MinRest, vent.MaxRest);
    }

    private bool PlayerNear(TransformComponent xform, float range)
    {
        var position = _transform.GetMapCoordinates(xform);
        foreach (var session in _players.Sessions)
        {
            if (session.AttachedEntity is not { } player || !TryComp(player, out TransformComponent? playerXform))
                continue;
            var at = _transform.GetMapCoordinates(playerXform);
            if (at.MapId == position.MapId && (at.Position - position.Position).LengthSquared() <= range * range)
                return true;
        }

        return false;
    }
}
