using System.Linq;
using Content.Shared._Onyx.Medical.Tourniquet;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Medical.Surgery;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared.Damage;
using Content.Shared.DoAfter;
using Content.Shared.FixedPoint;
using Content.Shared.Medical;
using Robust.Shared.Configuration;

namespace Content.Server._WF.Wolfmed.Wounds;

/// <summary>Cancels a body's do-afters on a part hit of wolfmed.doafter_interrupt_damage or more.</summary>
// Measured after armour. Treatment and surgery break although upstream never breaks them on damage, and anything
// that asks to break on damage breaks too. Ticks that pass interruptsDoAfters false (fire, bleeding, temperature,
// the overheat pulse) and systemic damage never count.
public sealed class WolfmedDoAfterInterruptSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;

    /// <summary>Called once per hit from <see cref="WolfmedPartHitSystem.OnHit"/>. Returns how many it cancelled.</summary>
    public int OnHit(PartDamageAppliedEvent hit)
    {
        if (!hit.InterruptsDoAfters || !TryComp(hit.Body, out DoAfterComponent? doAfters) || doAfters.DoAfters.Count == 0)
            return 0;

        var line = _config.GetCVar(WolfmedCVars.DoAfterInterruptDamage);
        var total = DamageSpecifier.GetPositive(hit.Total).GetTotal();
        if (line <= 0f || total < FixedPoint2.New(line))
            return 0;

        var cancelled = 0;
        foreach (var doAfter in doAfters.DoAfters.Values.ToArray())
        {
            if (doAfter.Cancelled || doAfter.Completed || !Interrupts(doAfter.Args, total))
                continue;

            _doAfter.Cancel(hit.Body, doAfter.Index, doAfters);
            cancelled++;
        }

        return cancelled;
    }

    /// <summary>Treatment and surgery always; anything else only when it asks to break on damage this size.</summary>
    public static bool Interrupts(DoAfterArgs args, FixedPoint2 hit) =>
        args.Event is HealingDoAfterEvent or SurgeryDoAfterEvent or TourniquetDoAfterEvent ||
        args.BreakOnDamage && hit >= args.DamageThreshold;
}
