using Robust.Client.UserInterface;
using Robust.Shared.Console;

namespace Content.Client._WF.SectorControl;

/// <summary>Opens or closes the sector map window. Client-side, for everyone.</summary>
public sealed class WFSectorMapCommand : LocalizedCommands
{
    [Dependency] private IUserInterfaceManager _ui = default!;

    public override string Command => "wf_sectormap";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        _ui.GetUIController<WFSectorMapUIController>().ToggleWindow();
    }
}
