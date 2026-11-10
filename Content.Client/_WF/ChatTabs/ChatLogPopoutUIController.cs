using System.Numerics;
using Content.Client.UserInterface.Systems.Chat.Widgets;
using Content.Shared._WF.CCVar;
using Robust.Client.UserInterface.Controllers;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;

namespace Content.Client._WF.ChatTabs;

/// <summary>
/// Pops a chat box's tab bar and log out into a <see cref="ChatLogWindow"/> and docks them back. The window
/// outlives a change of screen (lobby to round and back), so it stays where the player put it.
/// </summary>
public sealed partial class ChatLogPopoutUIController : UIController
{
    // A screen change removes one chat box and adds the next within a frame; the window waits a little longer.
    private const int VacantFrameLimit = 5;

    private static readonly Vector2 MinWindowSize = new(480, 360);

    [Dependency] private IConfigurationManager _cfg = default!;

    private ChatLogWindow? _window;
    private ChatBox? _hosted;
    private ChatBox? _pending;
    private bool _wanted;
    private int _vacantFrames;

    /// <summary>
    /// Whether the pop-out button is on offer.
    /// </summary>
    public bool Enabled => _cfg.GetCVar(WolfgateCVars.WindowPopout);

    /// <summary>
    /// The open log window, if any.
    /// </summary>
    public ChatLogWindow? Window => _window;

    public void Toggle(ChatBox chat)
    {
        if (_hosted != chat)
        {
            PopOut(chat);
            return;
        }

        _wanted = false;
        Dock();
        CloseWindow();
    }

    /// <summary>
    /// Moves a chat box's tab bar and log into the log window, opening it if needed.
    /// </summary>
    public void PopOut(ChatBox chat)
    {
        if (_hosted == chat || !chat.IsInsideTree)
            return;

        Dock();

        if (_window == null)
        {
            var window = new ChatLogWindow { SetSize = Vector2.Max(chat.Size, MinWindowSize) };
            window.Closing += _ => OnWindowGone(window);
            window.Closed += () => OnWindowGone(window);
            _window = window;
        }

        _window.Show();
        _wanted = true;
        _hosted = chat;
        chat.WfPopOutLog(_window.Body);
    }

    private void Dock()
    {
        if (_hosted is not { } chat)
            return;

        _hosted = null;
        _vacantFrames = 0;
        chat.WfDockLog();
    }

    /// <summary>
    /// A chat box entered the tree. If the log was popped out, this box's log follows on the next frame, once its
    /// screen has finished setting it up.
    /// </summary>
    public void ChatEntered(ChatBox chat)
    {
        if (_wanted)
            _pending = chat;
    }

    /// <summary>
    /// A chat box left the tree. Its log goes back to it, and the window stays for the next chat box.
    /// </summary>
    public void ChatExited(ChatBox chat)
    {
        if (_pending == chat)
            _pending = null;

        if (_hosted == chat)
            Dock();
    }

    /// <summary>
    /// Text typed while the log window has OS focus goes to the chat input too.
    /// </summary>
    public void SetTextInput(bool active)
    {
        if (_window?.ClydeWindow is not { } window)
            return;

        if (active)
            window.TextInputStart();
        else
            window.TextInputStop();
    }

    public override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        if (_pending is { } chat)
        {
            _pending = null;
            if (_wanted && _hosted == null && chat.IsInsideTree)
                PopOut(chat);
        }

        if (_window == null || _hosted != null)
            return;

        _vacantFrames++;
        if (_vacantFrames > VacantFrameLimit)
            CloseWindow();
    }

    private void CloseWindow()
    {
        if (_window is not { } window)
            return;

        _window = null;
        window.Close();
    }

    /// <summary>
    /// The player closed the log window with its OS close button, or the OS destroyed it: the log docks.
    /// </summary>
    private void OnWindowGone(ChatLogWindow window)
    {
        if (_window != window)
            return;

        _window = null;
        _wanted = false;
        Dock();
    }
}
