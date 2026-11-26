using Content.Shared._Mono.FireControl;
using Content.Shared._WF.Xenonids.ConsoleUse;
using Content.Shared.Shuttles.Components;
using Content.Shared.UserInterface;
using Content.Shared.Whitelist;

namespace Content.Shared._WF.Xenonids;

public sealed partial class XenoSystem
{
    [Dependency] private EntityWhitelistSystem _consoleWhitelist = default!;

    /// <summary>Whether a xeno may open a ship console: only whitelisted piloting consoles, never gunnery.</summary>
    private bool CanXenoUseConsole(EntityUid xeno, EntityUid target)
    {
        if (HasComp<FireControlConsoleComponent>(target))
            return false;

        // The shared console base isn't a registered component, so a shuttle console is told by its UI.
        if (!TryComp(target, out ActivatableUIComponent? ui) || !Equals(ui.Key, ShuttleConsoleUiKey.Key))
            return true;

        return TryComp(xeno, out XenoConsoleUserComponent? user) &&
               _consoleWhitelist.IsWhitelistPass(user.Whitelist, target);
    }
}
