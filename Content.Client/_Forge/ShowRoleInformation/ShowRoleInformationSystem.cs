using Content.Shared._Forge.ShowRoleInformation;
using Robust.Client.Player;
using Robust.Shared.Prototypes;

namespace Content.Client._Forge.ShowRoleInformation;

public sealed class RoleDescriptionSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;

    private readonly HashSet<ProtoId<ShowRoleInformationWindowData>> _skipWindows = [];
    private readonly HashSet<EntityUid> _visitedEntities = [];
    private ShowRoleInformationWindow? _currentWindow;
    private EntityUid? _previousEntityUid;

    public override void Initialize()
    {
        SubscribeLocalEvent<ShowRoleInformationComponent, ShowRoleInformationAddSkipWindowLocalEvent>(OnAddSkipWindow);
        SubscribeNetworkEvent<ShowRoleInformationFromServerEvent>(OnOpenRoleInfoFromServer);
    }

    public override void Update(float frameTime)
    {
        var currentEntityUid = _playerManager.LocalEntity;
        if (currentEntityUid == _previousEntityUid)
            return;

        if (currentEntityUid == null)
        {
            _skipWindows.Clear();
            _visitedEntities.Clear();
            _previousEntityUid = null;
            return;
        }

        if (!TryComp<ShowRoleInformationComponent>(currentEntityUid, out var currentShowRoleInformationComponent)
            || string.IsNullOrEmpty(currentShowRoleInformationComponent.Window.Id)
            || _skipWindows.Contains(currentShowRoleInformationComponent.Window)
            || TryComp<ShowRoleInformationComponent>(_previousEntityUid, out var previousShowRoleInformationComponent)
            && previousShowRoleInformationComponent.SkipWindows.Contains(currentShowRoleInformationComponent.Window))
        {
            _previousEntityUid = currentEntityUid;
            return;
        }

        _previousEntityUid = currentEntityUid;

        if (_visitedEntities.Add(currentEntityUid.Value))
            OpenWindow(currentShowRoleInformationComponent.Window, currentShowRoleInformationComponent.Duration);
    }

    private void OnAddSkipWindow(EntityUid uid, ShowRoleInformationComponent showRoleInformationComponent, ShowRoleInformationAddSkipWindowLocalEvent args)
    {
        _skipWindows.Add(args.Window);
    }

    private void OnOpenRoleInfoFromServer(ShowRoleInformationFromServerEvent msg, EntitySessionEventArgs args)
    {
        OpenWindow(msg.Window, msg.Duration);
    }

    private void OpenWindow(ProtoId<ShowRoleInformationWindowData> windowProto, float duration)
    {
        if (_currentWindow is { IsOpen: true })
        {
            _currentWindow.SetDuration(0);
            _currentWindow.Close();
        }

        var windowData = _prototypeManager.Index(windowProto);

        _currentWindow = new();
        _currentWindow.SetRoleInfo(windowData.Description, windowData.RoleName, windowData.ID);
        _currentWindow.SetDuration(duration);
        _currentWindow.OpenCentered();
    }
}

