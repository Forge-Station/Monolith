using System.Numerics;
using Content.Client.UserInterface.Controls;
using Content.Shared._WF.Bench;
using Robust.Client.UserInterface;

namespace Content.Client._WF.Bench;

/// <summary>The Bench terminal's window. Its state comes as a message to this viewer alone.</summary>
public sealed class WFBenchTerminalBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    [ViewVariables]
    private WFBenchTerminalWindow? _window;

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<WFBenchTerminalWindow>();
        _window.Panel.OnAction += action => SendMessage(new WFBenchTerminalMessage(action));
        SendMessage(new WFBenchTerminalMessage(new WFBenchRefreshAction()));
    }

    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        base.ReceiveMessage(message);
        if (message is WFBenchTerminalStateMessage state)
            _window?.Panel.UpdateState(state.State);
    }
}

/// <summary>The terminal's window around the Bench panel.</summary>
public sealed class WFBenchTerminalWindow : FancyWindow
{
    public readonly WFBenchPanel Panel = new();

    public WFBenchTerminalWindow()
    {
        Title = Loc.GetString("wf-bench-terminal-title");
        SetSize = new Vector2(640, 680);
        MinSize = new Vector2(480, 420);
        ContentsContainer.AddChild(Panel);
    }
}
