using Content.Server._WF.Wolfmed.Life;
using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared._WF.Wolfmed.Consciousness;
using Content.Shared._WF.Wolfmed.Reagents;
using Content.Shared.Alert;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Wolfmed.Consciousness;

/// <summary>
/// VISUALS: the pain HUD. One alert whose severity is the body's effective pain against its soft cap, 0 to 7, and 8
/// while a pain faint holds the body. Hidden at or under wolfmed.pain_hud_from and on the dead.
/// </summary>
/// <remarks>
/// Refreshed by <see cref="WolfmedConsciousnessSystem"/> every time it applies a body's state, which already follows
/// every pain change, relief and faint, so this takes no subscription. A machine shows the same icons under its own
/// alert, whose text reads as sensor overload; it never faints. The Downed alerts keep their meaning.
/// </remarks>
public sealed class WolfmedPainAlertSystem : EntitySystem
{
    public static readonly ProtoId<AlertPrototype> Alert = "WFWolfmedPain";
    public static readonly ProtoId<AlertPrototype> MechanicalAlert = "WFWolfmedPainMechanical";
    public static readonly ProtoId<AlertCategoryPrototype> Category = "WFWolfmedPain";

    /// <summary>The top of the pain scale: effective pain at the soft cap.</summary>
    public const short TopSeverity = 7;

    /// <summary>The paindd icon, shown while a pain faint holds the body.</summary>
    /// <summary>The paindowned icon, the top glyph with its border flashing red, while pain holds the body Downed.</summary>
    public const short DownedSeverity = 8;

    /// <summary>The paindd icon, its border flashing faster, while a pain faint holds the body.</summary>
    public const short FaintSeverity = 9;

    [Dependency] private AlertsSystem _alerts = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private PainSystem _pain = default!;
    [Dependency] private WolfmedConsciousnessSystem _consciousness = default!;
    [Dependency] private WolfmedPainReliefSystem _relief = default!;
    [Dependency] private WolfmedShutdownSystem _shutdown = default!;

    /// <summary>Shows, moves or clears the body's pain alert.</summary>
    public void Refresh(EntityUid body)
    {
        if (!HasComp<AlertsComponent>(body))
            return;

        if (GetSeverity(body) is not { } severity)
        {
            _alerts.ClearAlertCategory(body, Category);
            return;
        }

        // Both alerts share the category, so showing one replaces the other.
        _alerts.ShowAlert(body, _shutdown.IsMechanical(body) ? MechanicalAlert : Alert, severity);
    }

    /// <summary>The severity the alert should show, or null when it should be hidden.</summary>
    public short? GetSeverity(EntityUid body)
    {
        if (TerminatingOrDeleted(body) || _mobState.IsDead(body) ||
            !TryComp(body, out PainComponent? pain) || pain.SoftPainCap <= FixedPoint2.Zero ||
            !TryComp(body, out WolfmedConsciousnessComponent? consciousness))
            return null;

        if (consciousness.State == WolfmedConsciousness.Unconscious && _consciousness.IsFainted(body))
            return FaintSeverity;

        // The effective pain the Downed line reads: the body's value, min(soft cap, sum of the parts), less relief.
        var effective = MathF.Max(0f, _pain.GetPain((body, pain)).Float() - _relief.GetRelief(body));
        if (effective <= _cfg.GetCVar(WolfmedCVars.PainHudFrom))
            return null;

        // Playtest 4: Downed by pain flashes the border; the faint above flashes it faster. Read after the
        // threshold: Downed outlives the pain that caused it by a moment, and the HUD follows the pain.
        if (consciousness.State == WolfmedConsciousness.Downed && consciousness.Cause == WolfmedCause.Pain)
            return DownedSeverity;

        var level = MathF.Round(effective / pain.SoftPainCap.Float() * TopSeverity);
        return (short) Math.Clamp(level, 0f, TopSeverity);
    }
}
