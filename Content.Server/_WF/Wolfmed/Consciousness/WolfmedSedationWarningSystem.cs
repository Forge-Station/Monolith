using Content.Server._WF.Wolfmed.Life;
using Content.Shared._WF.Wolfmed.Consciousness;
using Content.Shared._WF.Wolfmed.Reagents;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;

namespace Content.Server._WF.Wolfmed.Consciousness;

/// <summary>
/// M2 (plan §3.4): the patient is told as sedation climbs, once per line: drowsy at <c>wolfmed.sedation_warn</c>,
/// breathing slowing at the depression line, barely awake at <c>wolfmed.sedation_warn_heavy</c>. Nothing ships in M1a.
/// </summary>
public sealed class WolfmedSedationWarningSystem : EntitySystem
{
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private WolfmedConditionAlertSystem _conditionAlerts = default!;
    [Dependency] private WolfmedShutdownSystem _shutdown = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WolfmedSedationWarningEvent>(OnWarning);
    }

    private void OnWarning(ref WolfmedSedationWarningEvent args)
    {
        if (!TryComp(args.Body, out WolfmedConsciousnessComponent? comp) || _mobState.IsDead(args.Body) ||
            _shutdown.IsMechanical(args.Body) || args.Level is < 1 or > 3)
            return;

        _conditionAlerts.Tell((args.Body, comp), Loc.GetString($"wolfmed-sedation-warn-{args.Level}"),
            args.Level >= 2 ? PopupType.MediumCaution : PopupType.Medium);
    }
}
