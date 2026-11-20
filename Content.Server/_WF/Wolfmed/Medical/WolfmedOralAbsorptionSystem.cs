using Content.Server.Body.Components;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared._WF.Wolfmed.Reagents;
using Content.Shared.Chemistry.Reagent;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Wolfmed.Medical;

/// <summary>
/// Swallowed painkillers reach the blood in <see cref="WolfmedCVars.PainkillerAbsorbSeconds"/> instead of the
/// stomach's 20 s (playtest 1), so a pill or a swig does something the patient can feel within about ten seconds.
/// The stomach asks through its one marked line; everything else it digests keeps its own delay.
/// </summary>
public sealed class WolfmedOralAbsorptionSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;

    private readonly Dictionary<string, bool> _painkillers = new();

    public override void Initialize()
    {
        base.Initialize();
        _prototypes.PrototypesReloaded += OnPrototypesReloaded;
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _prototypes.PrototypesReloaded -= OnPrototypesReloaded;
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (args.WasModified<ReagentPrototype>())
            _painkillers.Clear();
    }

    /// <summary>How long this reagent waits in this stomach before it moves to the blood.</summary>
    public TimeSpan GetDigestionDelay(StomachComponent stomach, string reagent)
    {
        if (!IsPainkiller(reagent))
            return stomach.DigestionDelay;

        var fast = TimeSpan.FromSeconds(MathF.Max(0f, _cfg.GetCVar(WolfmedCVars.PainkillerAbsorbSeconds)));
        return fast < stomach.DigestionDelay ? fast : stomach.DigestionDelay;
    }

    /// <summary>A reagent with a Wolfmed pain relief effect in any of its metabolisms.</summary>
    public bool IsPainkiller(string reagent)
    {
        if (_painkillers.TryGetValue(reagent, out var cached))
            return cached;

        var found = false;
        if (_prototypes.TryIndex<ReagentPrototype>(reagent, out var proto) && proto.Metabolisms is { } metabolisms)
        {
            foreach (var entry in metabolisms.Values)
            {
                foreach (var effect in entry.Effects)
                {
                    if (effect is WolfmedPainRelief)
                        found = true;
                }
            }
        }

        _painkillers[reagent] = found;
        return found;
    }
}
