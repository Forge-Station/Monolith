using Content.Client.UserInterface.Fragments;
using Content.Shared._WF.Bench;
using Content.Shared.CartridgeLoader;
using Robust.Client.UserInterface;

namespace Content.Client._WF.Bench;

/// <summary>The Bench PDA app.</summary>
public sealed partial class BenchUi : UIFragment
{
    private WFBenchPanel? _panel;

    public override Control GetUIFragmentRoot()
    {
        return _panel!;
    }

    public override void Setup(BoundUserInterface userInterface, EntityUid? fragmentOwner)
    {
        _panel = new WFBenchPanel();
        _panel.OnAction += action => userInterface.SendMessage(new CartridgeUiMessage(new WFBenchCartridgeMessage(action)));
    }

    public override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is WFBenchUiState cast)
            _panel?.UpdateState(cast);
    }
}
