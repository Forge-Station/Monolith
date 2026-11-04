using System.Numerics;
using Content.Server._FarHorizons.StarSystem;
using Content.Server.GameTicking;
using Content.Shared._FarHorizons.StarSystem.Prototypes;
using Content.Shared._WF.CCVar;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._WF.Planets;

/// <summary>
/// Builds the lobby preset's round-start worlds while the lobby is open, at the spots its star system will put
/// them, so their preload runs before anyone is in the round. Whatever no body takes over is deleted once the
/// round is under way.
/// </summary>
public sealed partial class WFPlanetRegistrySystem
{
    [Dependency] private IComponentFactory _factory = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private GameTicker _ticker = default!;

    /// <summary>How long after the round starts unadopted prebuilt networks are deleted: the star system spawns on the first tick.</summary>
    private static readonly TimeSpan OrphanGrace = TimeSpan.FromSeconds(5);

    private bool _prebuildPending;
    private TimeSpan? _orphanSweep;

    private void InitializeLobby()
    {
        SubscribeLocalEvent<GameRunLevelChangedEvent>(OnRunLevelChanged);
    }

    private void OnRunLevelChanged(GameRunLevelChangedEvent args)
    {
        switch (args.New)
        {
            // Next tick: the lobby's preset is settled after the run level changes.
            case GameRunLevel.PreRoundLobby:
                _prebuildPending = true;
                // A round restarted inside the grace would otherwise sweep the new lobby's worlds away.
                _orphanSweep = null;
                break;
            case GameRunLevel.InRound:
                _prebuildPending = false;
                _orphanSweep = _timing.CurTime + OrphanGrace;
                break;
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_prebuildPending)
        {
            _prebuildPending = false;
            Prebuild();
        }

        if (_orphanSweep is { } due && _timing.CurTime >= due)
        {
            _orphanSweep = null;
            var cleared = _networks.ClearPrebuilt();

            if (cleared > 0)
                Log.Warning($"Deleted {cleared} prebuilt planet networks no sector body took over.");
        }
    }

    /// <summary>Builds every round-start world of the lobby preset's star system.</summary>
    private void Prebuild()
    {
        if (!_cfg.GetCVar(PlanetCVars.PlanetNetworks) || !_cfg.GetCVar(PlanetCVars.Prebuild))
            return;

        if (_ticker.RunLevel != GameRunLevel.PreRoundLobby || _ticker.Preset is not { } preset)
            return;

        _networks.ClearPrebuilt();
        var built = 0;

        foreach (var ruleId in preset.Rules)
        {
            if (!_proto.TryIndex<EntityPrototype>(ruleId, out var rule)
                || !rule.TryGetComponent<StarSystemRuleComponent>(out var starSystem, _factory)
                || !_proto.TryIndex(starSystem.System, out var system))
                continue;

            foreach (var entry in system.Planets)
            {
                if (!_surfaces.TryGetValue(entry.Planet, out var surface) || !surface.BuildAtRoundStart)
                    continue;

                if (!_proto.TryIndex(entry.Planet, out var type))
                    continue;

                // The same spot the star system builder spawns the body at.
                var centre = new Vector2(MathF.Cos(entry.Angle), MathF.Sin(entry.Angle)) * entry.Distance;

                if (_networks.Prebuild(surface, centre, type.Name))
                    built++;
            }
        }

        if (built > 0)
            Log.Info($"Prebuilt {built} planet networks for preset \"{preset.ID}\" in the lobby.");
    }
}
