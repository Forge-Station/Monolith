using System.Threading.Tasks;
using Content.Server.GameTicking;
using Content.Shared._WF.CCVar;
using Robust.Shared.Timing;

namespace Content.Server._WF.Planets.Bounds;

/// <summary>
/// The tick budget for world generation outside the chunk loading, such as sites, roads and the boundary ring: work
/// awaits <see cref="Breathe"/> as it goes, so no tick runs long.
/// </summary>
public sealed partial class WFPlanetPreloadSystem
{
    [Dependency] private IGameTiming _timing = default!;

    private readonly System.Diagnostics.Stopwatch _slice = new();
    private GameTick _sliceTick;

    // An engine timer can't be used: one awaited from a timer's own continuation fires in the same tick.
    private List<TaskCompletionSource> _waiting = new();
    private List<TaskCompletionSource> _resuming = new();

    /// <summary>Resumes in this system's update on the next tick.</summary>
    public Task NextTick()
    {
        var tcs = new TaskCompletionSource();
        _waiting.Add(tcs);
        return tcs.Task;
    }

    /// <summary>Milliseconds a tick world generation may take: the lobby's while the round hasn't started.</summary>
    public float GenerationBudget() =>
        _cfg.GetCVar(_ticker.RunLevel == GameRunLevel.PreRoundLobby ? PlanetCVars.GenerationBudgetLobby : PlanetCVars.GenerationBudget);

    /// <summary>
    /// Milliseconds of planet work, generation and chunk loading together, done so far this tick; the first ask in a tick
    /// starts the clock. Each budget counts the other's share, so the two never stack up in one tick.
    /// </summary>
    private double TickWork()
    {
        if (_timing.CurTick != _sliceTick)
        {
            _sliceTick = _timing.CurTick;
            _slice.Restart();
        }

        return _slice.Elapsed.TotalMilliseconds;
    }

    /// <summary>Whether this tick's generation work has used up its budget; the first ask in a tick starts the clock.</summary>
    public bool GenerationSpent()
    {
        if (_timing.CurTick != _sliceTick)
        {
            _sliceTick = _timing.CurTick;
            _slice.Restart();
            return false;
        }

        return _slice.Elapsed.TotalMilliseconds >= GenerationBudget();
    }

    /// <summary>Carries on at once while the tick has budget left, and on the next tick once it is spent.</summary>
    public async Task Breathe()
    {
        if (!GenerationSpent())
            return;

        // Asked from a system that updates before this one, the wait would end later in the same tick.
        var tick = _timing.CurTick;
        do
        {
            await NextTick();
        } while (_timing.CurTick == tick);

        GenerationSpent();
    }

    /// <summary>A single generation step past this many milliseconds is logged, to find what a long forming tick spent.</summary>
    private const double SlowStepMs = 100;

    /// <summary>Times one generation step that can't be split; dispose it when the step is done.</summary>
    public StepTimer Time(string step) => new(this, step);

    /// <summary>Logs a generation step that ran long; see <see cref="Time"/>.</summary>
    public readonly struct StepTimer : IDisposable
    {
        private readonly WFPlanetPreloadSystem _system;
        private readonly string _step;
        private readonly long _start;

        public StepTimer(WFPlanetPreloadSystem system, string step)
        {
            _system = system;
            _step = step;
            _start = System.Diagnostics.Stopwatch.GetTimestamp();
        }

        public void Dispose()
        {
            var ms = System.Diagnostics.Stopwatch.GetElapsedTime(_start).TotalMilliseconds;
            if (ms >= SlowStepMs)
                _system.Log.Info($"Slow world generation step: {_step} took {ms:F0} ms.");
        }
    }

    /// <summary>Resumes whatever waited for this tick; what they wait for next goes to the tick after.</summary>
    private void ResumeWaiting()
    {
        if (_waiting.Count == 0)
            return;

        (_waiting, _resuming) = (_resuming, _waiting);
        foreach (var tcs in _resuming)
        {
            tcs.TrySetResult();
        }

        _resuming.Clear();
    }
}
