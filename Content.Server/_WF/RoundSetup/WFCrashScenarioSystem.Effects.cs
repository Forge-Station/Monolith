using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Content.Server._WF.Planets.Flight;

namespace Content.Server._WF.RoundSetup;

public sealed partial class WFCrashScenarioSystem
{
    [Dependency] private SharedAudioSystem _burstAudio = default!;
    private static readonly SoundSpecifier BurstSound = new SoundCollectionSpecifier("ExplosionSmall");

    /// <summary>At most three harmless bursts per crew, shared by all fragments and stopped at atmospheric entry.</summary>
    private void UpdateCosmeticBursts()
    {
        foreach (var scenario in _scenarios.Values)
        {
            if (!scenario.Used || scenario.CosmeticBursts >= 3)
                continue;
            foreach (var grid in scenario.Grids)
            {
                if (TerminatingOrDeleted(grid) || MetaData(grid).EntityPaused
                    || !TryComp<WFCrashScenarioComponent>(grid, out var crash)
                    || !crash.Started || crash.Descending || crash.Crashed || HasComp<WFCrashImpactComponent>(grid))
                    continue;
                var remaining = crash.DescendAt - _timing.CurTime;
                if (remaining <= TimeSpan.Zero || remaining > scenario.NextBurstRemaining
                    || !TryFindFloor(grid, out var at))
                    continue;

                Spawn("WFCrashCountdownBurst", at);
                var fire = Spawn("WFCrashCountdownFlame", at);
                _burstAudio.PlayPvs(BurstSound, fire, AudioParams.Default.WithVolume(-6f).WithMaxDistance(15f));
                scenario.CosmeticBursts++;
                scenario.NextBurstRemaining = remaining - TimeSpan.FromSeconds(_random.Next(40, 56));
                break;
            }
        }
    }
}
