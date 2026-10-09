using Content.Shared._Forge.Turrets;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._Forge.Turrets;

[UsedImplicitly]
public sealed class TurretCommandBoundUserInterface : BoundUserInterface
{
    private TurretCommandWindow? _window;

    public TurretCommandBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<TurretCommandWindow>();
        _window.OnTurretToggled += (turret, enabled) =>
            SendMessage(new TurretCommandToggleTurretMessage { Turret = turret, Enabled = enabled });
        _window.OnMagazineLockToggled += (turret, locked) =>
            SendMessage(new TurretCommandSetMagazineLockMessage { Turret = turret, Locked = locked });
        _window.OnNameAdded += (name, side) =>
            SendMessage(new TurretCommandAddNameMessage { Name = name, Side = side });
        _window.OnPersonCleared += key =>
            SendMessage(new TurretCommandClearPersonMessage { Key = key });
        _window.OnDoctrineSelected += doctrine =>
            SendMessage(new TurretCommandSetDoctrineMessage { Doctrine = doctrine });
        _window.OnTurretDoctrineSelected += (turret, doctrine) =>
            SendMessage(new TurretCommandSetTurretDoctrineMessage { Turret = turret, Doctrine = doctrine });
        _window.OnRefreshRequested += () =>
            SendMessage(new TurretCommandRefreshMessage());
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is TurretCommandConsoleState uiState)
            _window?.UpdateState(uiState);
    }
}
