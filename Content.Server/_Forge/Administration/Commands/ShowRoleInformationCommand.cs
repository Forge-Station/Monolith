using Content.Server.Administration;
using Content.Shared._Forge.ShowRoleInformation;
using Content.Shared.Administration;
using Robust.Server.Player;
using Robust.Shared.Console;
using Robust.Shared.Player;

namespace Content.Server._Forge.Administration.Commands;

[AdminCommand(AdminFlags.Moderator)]
public sealed partial class ShowRoleInformationCommand : IConsoleCommand
{
    [Dependency] private IEntityManager _entManager = default!;
    [Dependency] private IPlayerManager _playerManager = default!;

    public string Command => "openroleinformation";
    public string Description => Loc.GetString("show-role-information-command-description");
    public string Help => Loc.GetString("show-role-information-command-help");
    private float _duration;

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length is < 1 or > 2)
        {
            shell.WriteError(Loc.GetString("show-role-information-command-help"));
            return;
        }

        ICommonSession? targetSession;

        if (EntityUid.TryParse(args[0], out var targetUid))
            _playerManager.TryGetSessionByEntity(targetUid, out targetSession);
        else
            _playerManager.TryGetSessionByUsername(args[0], out targetSession);

        if (targetSession?.AttachedEntity == null)
        {
            shell.WriteError(Loc.GetString("show-role-information-command-err-player-not-found", ("player", args[0])));
            return;
        }

        if (!_entManager.TryGetComponent<ShowRoleInformationComponent>(targetSession.AttachedEntity.Value, out var showRoleInformationComponent))
        {
            shell.WriteError(Loc.GetString("show-role-information-command-err-no-component", ("player", targetSession.Name)));
            return;
        }

        _duration = showRoleInformationComponent.Duration;

        if (args.Length == 2)
        {
            if (float.TryParse(args[1], out var duration) && duration >= 0)
                _duration = duration;
            else
                shell.WriteError(Loc.GetString("show-role-information-command-err-duration", ("time", args[1])));
        }

        var evt = new ShowRoleInformationFromServerEvent
        {
            RoleName = showRoleInformationComponent.RoleName,
            Description = showRoleInformationComponent.Description,
            Duration = _duration,
        };

        _entManager.EntityNetManager.SendSystemNetworkMessage(evt, targetSession.Channel);
        shell.WriteLine(Loc.GetString("show-role-information-command-success", ("player", targetSession.Name), ("duration", (int)_duration)));
    }
}
