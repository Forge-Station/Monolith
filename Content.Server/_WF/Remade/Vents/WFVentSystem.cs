using Content.Server._WF.Remade.Director;
using Content.Server._WF.Remade.Monolith;
using Content.Shared._WF.Remade;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._WF.Remade.Vents;

/// <summary>
/// Keeps the vents busy. At Growth and above each Monolith sends a runner through the ducts every few minutes, sooner as it climbs,
/// and some of them lie in wait in the exit vent for a crewman to pass. The trip and the ambush themselves are in the Director.
/// Waves used to be the only thing that sent anyone through the vents, and they came by a vent seldom, so the crew never heard it.
/// </summary>
public sealed class WFVentSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private WFRemadeDirectorSystem _director = default!;

    /// <summary>The share of runs that lie in wait instead of bursting out.</summary>
    public const float AmbushShare = 0.45f;

    private static readonly TimeSpan LairRate = TimeSpan.FromSeconds(0.25);
    private static readonly TimeSpan RunRate = TimeSpan.FromSeconds(1);
    private TimeSpan _nextLair;
    private TimeSpan _nextRun;

    /// <summary>Seconds between runs for one Monolith, by stage.</summary>
    public static (float Min, float Max) RunEvery(WFMonolithStage stage)
    {
        return stage switch
        {
            WFMonolithStage.Growth => (200f, 320f),
            WFMonolithStage.Harvest => (90f, 150f),
            WFMonolithStage.Rapture => (55f, 95f),
            _ => (35f, 60f),
        };
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        if (now >= _nextLair)
        {
            _nextLair = now + LairRate;
            _director.UpdateAmbushes(now);
            _director.UpdateHauls(now);
        }

        if (now < _nextRun)
            return;

        _nextRun = now + RunRate;
        var query = EntityQueryEnumerator<WFRemadeDirectorComponent, WFMonolithComponent>();
        while (query.MoveNext(out var uid, out var director, out var monolith))
        {
            if (monolith.Stage < WFMonolithStage.Growth)
                continue;

            var state = EnsureComp<WFVentStateComponent>(uid);
            var (min, max) = RunEvery(monolith.Stage);
            if (state.NextRun == TimeSpan.Zero)
                state.NextRun = now + TimeSpan.FromSeconds(_random.NextFloat(min * 0.4f, min));

            var (haulMin, haulMax) = WFRemadeDirectorSystem.HaulEvery(monolith.Stage);
            if (haulMax > 0f)
            {
                if (state.NextHaul == TimeSpan.Zero)
                    state.NextHaul = now + TimeSpan.FromSeconds(_random.NextFloat(haulMin * 0.5f, haulMin));

                if (now >= state.NextHaul)
                {
                    var hauling = _director.TryHaul((uid, director, monolith), now);
                    state.NextHaul = now + TimeSpan.FromSeconds(hauling ? _random.NextFloat(haulMin, haulMax) : 20f);
                }
            }

            if (now < state.NextRun)
                continue;

            // A run that couldn't go (no crew near, no vent, no biomass) is tried again soon, not a whole interval later.
            var sent = _director.TryVentRun((uid, director, monolith), _random.Prob(AmbushShare), now);
            state.NextRun = now + TimeSpan.FromSeconds(sent ? _random.NextFloat(min, max) : 12f);
        }
    }
}
