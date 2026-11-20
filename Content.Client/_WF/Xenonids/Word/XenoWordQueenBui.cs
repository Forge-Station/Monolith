using Content.Shared._WF.Xenonids.Word;
using JetBrains.Annotations;
using Robust.Client.UserInterface;
using Robust.Shared.Utility;
using static Robust.Client.UserInterface.Controls.BaseButton;

namespace Content.Client._WF.Xenonids.Word;

[UsedImplicitly]
public sealed class XenoWordQueenBui(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    [ViewVariables]
    private XenoWordQueenWindow? _window;

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<XenoWordQueenWindow>();
        _window.SendButton.OnPressed += Send;
    }

    private void Send(ButtonEventArgs args)
    {
        if (_window == null)
            return;

        var text = Rope.Collapse(_window.Text.TextRope);
        if (string.IsNullOrWhiteSpace(text))
            return;

        var msg = new XenoWordQueenBuiMsg(text);
        SendPredictedMessage(msg);
        _window.Close();
    }
}
