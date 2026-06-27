// Forge-Change
using Content.Shared.SecApartment;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._Wega.Security.SecApartment;

[UsedImplicitly]
public sealed class SecApartmentBoundUserInterface : BoundUserInterface
{
    private SecApartmentWindow? _window;
    private SecApartmentUpdateState? _pendingUpdateState;
    private SensorStatusUpdateState? _pendingSensorState;
    private TimerUpdateState? _pendingTimerState;

    public SecApartmentBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey) { }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<SecApartmentWindow>();
        if (EntMan.TryGetComponent<SecApartmentComponent>(Owner, out var component))
            _window.ApplyUiTheme(component.UiTheme);

        _window.OnCreateSquad += (squadName, department) =>
            SendMessage(new CreateSquadMessage(squadName, department));

        _window.OnChangeSquadIcon += (squadId, iconId) =>
            SendMessage(new ChangeSquadIconMessage(squadId, iconId));

        _window.OnRenameSquad += (squadId, newName) =>
            SendMessage(new RenameSquadMessage(squadId, newName));

        _window.OnDeleteSquad += squadId =>
            SendMessage(new DeleteSquadMessage(squadId));

        _window.OnUpdateSquadDescription += (squadId, description) =>
            SendMessage(new UpdateSquadDescriptionMessage(squadId, description));

        _window.OnChangeSquadStatus += (squadId, status) =>
            SendMessage(new ChangeSquadStatusMessage(squadId, status));

        _window.OnAddMemberToSquad += (squadId, memberId) =>
            SendMessage(new AddMemberToSquadMessage(squadId, memberId));

        _window.OnRemoveMemberFromSquad += (squadId, memberId) =>
            SendMessage(new RemoveMemberFromSquadMessage(squadId, memberId));

        _window.OnRemoveTimer += timerUid =>
            SendMessage(new RemoveTimerMessage(timerUid));

        ApplyPendingState();

        _window.OnClose += Close;
        _window.OpenCentered();
        SendMessage(new RefreshSecApartmentMessage());
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        switch (state)
        {
            case SecApartmentUpdateState updateState:
                _pendingUpdateState = updateState;
                break;
        }

        ApplyPendingState();
    }

    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        base.ReceiveMessage(message);

        switch (message)
        {
            case SensorStatusUpdateState sensorState:
                _pendingSensorState = sensorState;
                break;

            case TimerUpdateState timerState:
                _pendingTimerState = timerState;
                break;
        }

        ApplyPendingState();
    }

    private void ApplyPendingState()
    {
        if (_window == null)
            return;

        if (_pendingUpdateState != null)
        {
            _window.UpdateState(_pendingUpdateState);
            _pendingUpdateState = null;
        }

        if (_pendingSensorState != null)
        {
            _window.UpdateSensorStatuses(_pendingSensorState);
            _pendingSensorState = null;
        }

        if (_pendingTimerState != null)
        {
            _window.UpdateTimerState(_pendingTimerState);
            _pendingTimerState = null;
        }
    }
}
