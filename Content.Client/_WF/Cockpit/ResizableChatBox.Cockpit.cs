namespace Content.Client.UserInterface.Systems.Chat.Widgets;

public sealed partial class ResizableChatBox
{
    private bool _wfCockpitDocked;

    /// <summary>The cockpit owns this chat's bounds while retaining its input and history; undocking re-clamps it.</summary>
    public bool WfCockpitDocked
    {
        get => _wfCockpitDocked;
        set
        {
            if (_wfCockpitDocked == value)
                return;
            _wfCockpitDocked = value;
            if (!value)
                ClampAfterDelay();
        }
    }
}
