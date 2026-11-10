using Content.Shared.Chat;

namespace Content.Client.UserInterface.Systems.Chat.Controls;

// The filter checkboxes mirror the selected chat tab; see Content.Client/_WF/ChatTabs.
public sealed partial class ChannelFilterPopup
{
    /// <summary>
    /// Every channel a chat box can show, in display order.
    /// </summary>
    public static IReadOnlyList<ChatChannel> WfChannels => ChannelFilterOrder;

    /// <summary>
    /// Sets the checkboxes to a chat tab's channels without raising <see cref="OnChannelFilter"/>.
    /// </summary>
    public void WfSetActive(ChatChannel channels)
    {
        foreach (var (channel, checkbox) in _filterStates)
        {
            checkbox.Pressed = (channels & channel) != 0;
        }
    }
}
