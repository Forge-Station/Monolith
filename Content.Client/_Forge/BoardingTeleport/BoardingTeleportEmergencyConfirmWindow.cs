using Content.Client.UserInterface.Controls;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Utility;

namespace Content.Client._Forge.BoardingTeleport;

public sealed class BoardingTeleportEmergencyConfirmWindow : FancyWindow
{
    public event Action? Confirmed;
    public event Action? Cancelled;

    private bool _finished;
    private readonly RichTextLabel _messageLabel;
    private readonly ConfirmButton _confirmButton;

    public BoardingTeleportEmergencyConfirmWindow(string title, string message, string confirmButtonText)
    {
        Title = title;
        MinWidth = 420;

        var root = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 12,
        };

        _messageLabel = new RichTextLabel();
        _messageLabel.SetMessage(FormattedMessage.FromMarkupOrThrow(message));
        _messageLabel.MaxWidth = 480;
        root.AddChild(_messageLabel);

        var buttons = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalAlignment = HAlignment.Right,
            SeparationOverride = 8,
        };

        var cancelButton = new Button { Text = Loc.GetString("quick-dialog-ui-cancel") };
        _confirmButton = new ConfirmButton
        {
            Text = confirmButtonText,
        };

        cancelButton.OnPressed += _ => Finish(cancelled: true);
        _confirmButton.OnPressed += _ => Finish(cancelled: false);

        OnClose += () =>
        {
            if (!_finished)
                Finish(cancelled: true);
        };

        buttons.AddChild(cancelButton);
        buttons.AddChild(_confirmButton);
        root.AddChild(buttons);

        ContentsContainer.AddChild(root);
    }

    public void SetContent(string title, string message, string confirmButtonText)
    {
        _finished = false;
        Title = title;
        _messageLabel.SetMessage(FormattedMessage.FromMarkupOrThrow(message));
        _confirmButton.Text = confirmButtonText;
    }

    private void Finish(bool cancelled)
    {
        if (_finished)
            return;

        _finished = true;

        if (cancelled)
            Cancelled?.Invoke();
        else
            Confirmed?.Invoke();

        Close();
    }
}
