using Content.Server._WF.RoundSetup;
using Content.Server.Shuttles.Components;

namespace Content.Server.Shuttles.Systems;

public sealed partial class ThrusterSystem
{
    /// <summary>Scenario flight failure disables the engine through its owning system.</summary>
    public void WfDisableCrashThruster(EntityUid uid, ThrusterComponent component)
    {
        component.Enabled = false;
        if (component.IsOn)
            DisableThruster(uid, component);
    }
}

public sealed partial class ShuttleConsoleSystem
{
    /// <summary>Also gates fresh and remote helms before the scenario's machinery sweep sees them.</summary>
    private bool WfCrashAllowsPilot(EntityUid console)
    {
        return Transform(console).GridUid is not { } grid
            || !TryComp<WFCrashScenarioComponent>(grid, out var crash) || crash.Crashed;
    }
}
