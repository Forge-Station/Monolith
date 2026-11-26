using System.Linq;
using System.Text;
using Content.Client.UserInterface.Systems.Chat.Controls;
using Content.Shared.Chat;

namespace Content.Client._WF.ChatTabs;

/// <summary>
/// One chat tab: a name and the channels it shows.
/// </summary>
public sealed class ChatTab
{
    public const int MaxTabs = 10;
    public const int MaxNameLength = 16;

    /// <summary>
    /// Every channel, including ones added later. Saved as <c>*</c>.
    /// </summary>
    public const ChatChannel AllChannels = (ChatChannel) uint.MaxValue;

    private const char TabSeparator = '|';
    private const char NameSeparator = '=';
    private const char ChannelSeparator = ',';
    private const string AllChannelsToken = "*";

    public string Name;
    public ChatChannel Channels;

    public ChatTab(string name, ChatChannel channels)
    {
        Name = name;
        Channels = channels;
    }

    /// <summary>
    /// The channels a chat box can show at all.
    /// </summary>
    public static ChatChannel KnownChannels
    {
        get
        {
            ChatChannel known = 0;
            foreach (var channel in ChannelFilterPopup.WfChannels)
            {
                known |= channel;
            }

            return known;
        }
    }

    /// <summary>
    /// Strips the save format's separators and control characters, and trims to <see cref="MaxNameLength"/>.
    /// </summary>
    public static string SanitizeName(string name)
    {
        var builder = new StringBuilder(MaxNameLength);
        foreach (var c in name.Trim())
        {
            if (c == TabSeparator || c == NameSeparator || char.IsControl(c))
                continue;

            builder.Append(c);
            if (builder.Length >= MaxNameLength)
                break;
        }

        return builder.ToString().Trim();
    }

    /// <summary>
    /// Reads tabs saved by <see cref="Format"/>, skipping entries it can't read.
    /// </summary>
    public static List<ChatTab> Parse(string value)
    {
        var tabs = new List<ChatTab>();
        foreach (var entry in value.Split(TabSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var split = entry.IndexOf(NameSeparator);
            if (split <= 0)
                continue;

            var name = SanitizeName(entry[..split]);
            if (name.Length == 0)
                continue;

            tabs.Add(new ChatTab(name, ParseChannels(entry[(split + 1)..])));
            if (tabs.Count >= MaxTabs)
                break;
        }

        return tabs;
    }

    public static string Format(IEnumerable<ChatTab> tabs)
    {
        return string.Join(TabSeparator, tabs.Select(tab => $"{tab.Name}{NameSeparator}{FormatChannels(tab.Channels)}"));
    }

    private static ChatChannel ParseChannels(string value)
    {
        if (value.Trim() == AllChannelsToken)
            return AllChannels;

        ChatChannel channels = 0;
        foreach (var name in value.Split(ChannelSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var channel in ChannelFilterPopup.WfChannels)
            {
                if (channel.ToString() == name.Trim())
                    channels |= channel;
            }
        }

        return channels;
    }

    private static string FormatChannels(ChatChannel channels)
    {
        if (channels == AllChannels)
            return AllChannelsToken;

        return string.Join(ChannelSeparator, ChannelFilterPopup.WfChannels.Where(c => (channels & c) != 0));
    }
}
