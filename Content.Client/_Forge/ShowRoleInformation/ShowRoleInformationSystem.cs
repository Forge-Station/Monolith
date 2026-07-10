using Content.Shared._Forge.ShowRoleInformation;
using Robust.Shared.Player;

namespace Content.Client._Forge.ShowRoleInformation;

public sealed class RoleDescriptionSystem : EntitySystem
{
    private ShowRoleInformationWindow? _currentWindow;
    private readonly HashSet<string> _skipWindows = [];

    public override void Initialize()
    {
        SubscribeLocalEvent<PlayerAttachedEvent>(OnPlayerAttached);
        SubscribeLocalEvent<ShowRoleInformationAddSkipWindowLocalEvent>(OnAddSkipWindow);
        SubscribeNetworkEvent<ShowRoleInformationFromServerEvent>(OnOpenRoleInfoFromServer);
    }

    private void OnAddSkipWindow(ShowRoleInformationAddSkipWindowLocalEvent msg)
    {
        _skipWindows.Add(msg.KeyWindow);
    }

    private void OnOpenRoleInfoFromServer(ShowRoleInformationFromServerEvent msg, EntitySessionEventArgs args)
    {
        OpenWindow(msg.RoleName, msg.Description, msg.Duration);
    }

    private void OnPlayerAttached(PlayerAttachedEvent args)
    {
        if (!TryComp<ShowRoleInformationComponent>(args.Entity, out var showRoleInformationComponent) ||
            _skipWindows.Contains(string.Concat(showRoleInformationComponent.RoleName, showRoleInformationComponent.Description)))
            return;

        OpenWindow(showRoleInformationComponent.RoleName, showRoleInformationComponent.Description, showRoleInformationComponent.Duration);
    }

    private void OpenWindow(string roleName, string description, float duration)
    {
        if (_currentWindow is { IsOpen: true })
        {
            _currentWindow.SetDuration(0);
            _currentWindow.Close();
        }

        _currentWindow = new();
        _currentWindow.SetRoleInfo(description, roleName);
        _currentWindow.SetDuration(duration);
        _currentWindow.OpenCentered();
    }
}

