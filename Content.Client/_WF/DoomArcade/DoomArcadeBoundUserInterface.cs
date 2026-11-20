using Content.Client._WF.GameHub;
using Content.Shared._WF.GameHub;
using Robust.Client.UserInterface;

namespace Content.Client._WF.DoomArcade;

/// <summary>Opens Doom when a player uses the cabinet. Only sound and finished levels reach the server.</summary>
public sealed class DoomArcadeBoundUserInterface : BoundUserInterface
{
    private DoomArcadeWindow? _window;

    public DoomArcadeBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<DoomArcadeWindow>();
        var cabinet = EntMan.GetComponentOrNull<WFGameCabinetComponent>(Owner);
        _window.Begin(Owner, cabinet?.Game ?? WFGameHubSystem.DoomGame);
    }
}
