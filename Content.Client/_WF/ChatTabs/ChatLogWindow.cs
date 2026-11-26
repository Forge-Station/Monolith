using System.Numerics;
using Content.Client.Stylesheets;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;

namespace Content.Client._WF.ChatTabs;

/// <summary>
/// OS window holding a chat box's tab bar and log while they are popped out. The input line stays in the game
/// window, where typed text goes.
/// </summary>
public sealed class ChatLogWindow : OSWindow
{
    private static readonly Vector2 AutoSize = new(float.NaN, float.NaN);

    /// <summary>
    /// The tab bar and the log go in here.
    /// </summary>
    public readonly BoxContainer Body;

    public ChatLogWindow()
    {
        Title = Loc.GetString("wf-chat-tabs-window-title");

        Body = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 4,
        };

        AddChild(new PanelContainer
        {
            StyleClasses = { StyleNano.StyleClassChatPanel },
            Children = { Body },
        });
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        // OSWindow pins SetSize to the window size in UI units, which goes stale when the DPI scale changes
        // (dragging onto a monitor with other scaling). Stretching to the root follows the real window instead.
        if (!float.IsNaN(SetWidth) || !float.IsNaN(SetHeight))
            SetSize = AutoSize;
    }
}
