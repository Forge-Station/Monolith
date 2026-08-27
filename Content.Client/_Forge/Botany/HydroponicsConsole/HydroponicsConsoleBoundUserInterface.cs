using Content.Shared._Forge.Botany.HydroponicsConsole;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._Forge.Botany.HydroponicsConsole;

[UsedImplicitly]
public sealed class HydroponicsConsoleBoundUserInterface : BoundUserInterface
{
    private HydroponicsConsoleWindow? _window;

    public HydroponicsConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<HydroponicsConsoleWindow>();
        _window.OnSaveCultivar += (tray, name) => SendMessage(new HydroponicsConsoleSaveCultivarMessage(tray, name));
        _window.OnRenameCultivar += (index, name) => SendMessage(new HydroponicsConsoleRenameCultivarMessage(index, name));
        _window.OnDeleteCultivar += index => SendMessage(new HydroponicsConsoleDeleteCultivarMessage(index));
        _window.OnPrintPacket += index => SendMessage(new HydroponicsConsolePrintPacketMessage(index));
        _window.OnEjectDisk += index => SendMessage(new HydroponicsConsoleEjectDiskMessage(index));
        _window.OnCycleLight += tray => SendMessage(new HydroponicsConsoleCycleLightMessage(tray));
        _window.OnRenameTray += (tray, name) => SendMessage(new HydroponicsConsoleRenameTrayMessage(tray, name));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is HydroponicsConsoleBoundUserInterfaceState cast)
            _window?.Populate(cast);
    }
}
