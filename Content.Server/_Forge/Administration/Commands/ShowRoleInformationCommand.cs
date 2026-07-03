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
    public string Description => "Принудительно открывает окно с информацией о роли на клиенте.";
    public string Help => "Использование: openroleinformation <ник_или_uid> <продолжительность>?";
    private float _duration;

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length is < 1 or > 2)
        {
            shell.WriteError("Использование: openroleinformation <ник/uid> <продолжительность>?");
            return;
        }

        ICommonSession? targetSession;

        if (EntityUid.TryParse(args[0], out var targetUid))
            _playerManager.TryGetSessionByEntity(targetUid, out targetSession);
        else
            _playerManager.TryGetSessionByUsername(args[0], out targetSession);

        if (targetSession?.AttachedEntity == null)
        {
            shell.WriteError($"Не удалось найти игрока '{args[0]}' или у него нет тела.");
            return;
        }

        if (args.Length > 1)
        {
            if (float.TryParse(args[1], out var duration) || duration > 0)
                _duration = duration;
            else
                shell.WriteError($"Неверный формат продолжительности: '{args[1]}'.");
        }

        if (!_entManager.TryGetComponent<ShowRoleInformationComponent>(targetSession.AttachedEntity.Value, out var showRoleInformationComponent))
        {
            shell.WriteError($"У {targetSession.Name} отсутствует ShowRoleInformationComponent");
            return;
        }

        if (args.Length == 1)
            _duration = showRoleInformationComponent.Duration;

        var evt = new ShowRoleInformationEvent
        {
            RoleName = showRoleInformationComponent.RoleName,
            Description = showRoleInformationComponent.Description,
            Duration = _duration,
        };

        _entManager.EntityNetManager.SendSystemNetworkMessage(evt, targetSession.Channel);
        shell.WriteLine($"Окно отправлено для {targetSession.Name}. Таймер: {_duration} сек.");
    }
}
