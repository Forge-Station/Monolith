using System.Numerics;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.UserInterface.Systems.Chat.Widgets;

// The floating chat box shrinks to its input line while its log is popped out; see Content.Client/_WF/ChatTabs.
public sealed partial class ResizableChatBox
{
    private Vector2 _wfDockedMinSize;
    private float _wfDockedMarginBottom;
    private bool _wfShrunk;

    protected override void WfLogMoved(bool poppedOut)
    {
        // In the cockpit the box fills a panel, so it keeps its size and shows the notice like any docked chat box.
        if (poppedOut ? WfCockpitDocked : !_wfShrunk)
        {
            base.WfLogMoved(poppedOut);
            return;
        }

        _wfShrunk = poppedOut;
        if (!poppedOut)
        {
            MinSize = _wfDockedMinSize;
            LayoutContainer.SetMarginBottom(this, _wfDockedMarginBottom);
            return;
        }

        _wfDockedMinSize = MinSize;
        _wfDockedMarginBottom = GetValue<float>(LayoutContainer.MarginBottomProperty);

        // Anchored by its top edge, so the bottom margin is where the box ends.
        MinSize = new Vector2(MinSize.X, 0);
        Measure(new Vector2(Size.X, float.PositiveInfinity));
        LayoutContainer.SetMarginBottom(this, GetValue<float>(LayoutContainer.MarginTopProperty) + DesiredSize.Y);

        // A clamp already waiting would read the box's old height and undo this; it starts over.
        if (_clampIn > 0)
            ClampAfterDelay();
    }
}
