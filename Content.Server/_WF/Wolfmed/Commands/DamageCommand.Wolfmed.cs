using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.Wolfmed.Body;
using Content.Shared._WF.Wolfmed.Targeting;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Robust.Shared.Console;

namespace Content.Server.Damage.Commands;

/// <summary>
/// P6: the optional 5th argument of <c>damage</c>, a body part, routed through Wolfmed so an admin or a test
/// can hit one limb directly instead of letting the router pick.
/// </summary>
/// <remarks>
/// The body lives here rather than in <c>HurtCommand.cs</c>, which keeps four marked hook lines. Only the
/// part form parses <c>ignoreResistances</c> at args[2]; the shorter forms keep upstream's behaviour, where a
/// 4-argument call silently ignores it.
/// </remarks>
sealed partial class DamageCommand
{
    private WoundTargetResolver TargetResolver => _entManager.System<WoundTargetResolver>();

    /// <summary>Body parts the target actually has, when the uid before them already named one.</summary>
    private CompletionResult WolfmedPartCompletion(string[] args)
    {
        var options = SharedTargetingSystem.GetValidParts().AsEnumerable();
        if (_entManager.TryParseNetEntity(args[3], out var target) && _entManager.EntityExists(target))
            options = options.Where(part => TargetResolver.TryResolveExact(target.Value, part, out _));

        return CompletionResult.FromHintOptions(
            options.Select(part => new CompletionOption(part.ToString())),
            Loc.GetString("damage-command-arg-body-part"));
    }

    /// <summary>Applies args[0]/args[1] damage to the named part of the target.</summary>
    private void WolfmedHurtPart(IConsoleShell shell, EntityUid target, string[] args)
    {
        if (!bool.TryParse(args[2], out var ignoreResistances))
        {
            shell.WriteLine(Loc.GetString("damage-command-error-bool", ("arg", args[2])));
            return;
        }

        if (!Enum.TryParse<TargetBodyPart>(args[4], ignoreCase: true, out var requested) ||
            !SharedTargetingSystem.IsSelectable(requested))
        {
            shell.WriteLine(Loc.GetString("damage-command-error-body-part", ("arg", args[4])));
            return;
        }

        // Exact, not TryResolveAvailable: an admin who asked for a hand and got the torso would be misled.
        if (!TargetResolver.TryResolveExact(target, requested, out var part))
        {
            shell.WriteLine(Loc.GetString("damage-command-error-missing-body-part",
                ("target", target),
                ("part", requested)));
            return;
        }

        if (!TryParseDamageSpecifier(args[0], args[1], shell, out var damage))
            return;

        // M1b (P31): an admin's hit has no origin, so the ambient ceilings would stop it short of destroying the
        // part. The command lands in full.
        var applied = false;
        _entManager.System<WolfmedBodyPartSystem>().WithCeilingBypass(target, () =>
            applied = _entManager.System<WoundDamageRoutingSystem>()
                .TryApplyPartDamage(target, part, damage, ignoreResistances: ignoreResistances));
        if (!applied)
            shell.WriteLine(Loc.GetString("damage-command-error-part-damage", ("target", target)));
    }

    /// <summary>The part form needs the DamageSpecifier itself, not TryParseDamageArgs' apply delegate.</summary>
    private bool TryParseDamageSpecifier(
        string type,
        string quantity,
        IConsoleShell shell,
        [NotNullWhen(true)] out DamageSpecifier? damage)
    {
        damage = null;
        // NaN and the infinities parse, and compare false against every clamp downstream, which would leave
        // a part's damage corrupted until a full heal.
        if (!float.TryParse(quantity, out var amount) || !float.IsFinite(amount))
        {
            shell.WriteLine(Loc.GetString("damage-command-error-quantity", ("arg", quantity)));
            return false;
        }

        if (_prototypeManager.TryIndex<DamageGroupPrototype>(type, out var damageGroup))
        {
            damage = new DamageSpecifier(damageGroup, amount);
            return true;
        }

        if (_prototypeManager.TryIndex<DamageTypePrototype>(type, out var damageType))
        {
            damage = new DamageSpecifier(damageType, amount);
            return true;
        }

        shell.WriteLine(Loc.GetString("damage-command-error-type", ("arg", type)));
        return false;
    }
}
