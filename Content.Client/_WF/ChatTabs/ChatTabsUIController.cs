using Content.Client.UserInterface.Systems.Chat;
using Content.Client.UserInterface.Systems.Chat.Controls;
using Content.Shared._WF.CCVar;
using Content.Shared.Chat;
using Robust.Client.Audio;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controllers;
using Robust.Shared.Audio;
using Robust.Shared.Configuration;
using Robust.Shared.Player;

namespace Content.Client._WF.ChatTabs;

/// <summary>
/// The player's chat tabs and which one is selected. Every chat box shows the same tabs, saved in
/// <see cref="WolfgateCVars.ChatTabs"/>.
/// </summary>
public sealed partial class ChatTabsUIController : UIController
{
    private const int UnreadCap = 99;

    [Dependency] private IConfigurationManager _cfg = default!;

    [UISystemDependency] private readonly AudioSystem? _audio = default;

    private readonly List<ChatTab> _tabs = new();

    // Messages per channel that arrived while the selected tab hid them, until a tab shows them.
    private readonly Dictionary<ChatChannel, int> _unseen = new();

    private ChatUIController _chat = default!;

    public IReadOnlyList<ChatTab> Tabs => _tabs;
    public int Selected { get; private set; }
    public ChatTab SelectedTab => _tabs[Selected];

    /// <summary>
    /// A tab was added, removed, renamed or moved.
    /// </summary>
    public event Action? TabsChanged;

    /// <summary>
    /// The channels on show changed: another tab was selected or the selected tab was edited.
    /// </summary>
    public event Action? FilterChanged;

    public event Action? UnreadChanged;

    public override void Initialize()
    {
        base.Initialize();

        _chat = UIManager.GetUIController<ChatUIController>();
        _chat.MessageAdded += OnMessageAdded;
        Load(_cfg.GetCVar(WolfgateCVars.ChatTabs));
    }

    /// <summary>
    /// Replaces the tabs with saved ones, or with the defaults if there are none.
    /// </summary>
    public void Load(string saved)
    {
        _tabs.Clear();
        _tabs.AddRange(ChatTab.Parse(saved));

        // The default Local tab as saved before subtle emotes had a channel of their own, once: a player who then
        // takes Subtle off it keeps it off.
        if (!_cfg.GetCVar(WolfgateCVars.ChatTabsSubtleAdded))
        {
            const ChatChannel oldLocal = ChatChannel.Local | ChatChannel.Whisper | ChatChannel.Emotes;
            var added = false;
            foreach (var tab in _tabs)
            {
                if (tab.Channels != oldLocal)
                    continue;

                tab.Channels |= ChatChannel.Subtle;
                added = true;
            }

            _cfg.SetCVar(WolfgateCVars.ChatTabsSubtleAdded, true);
            if (added)
                _cfg.SetCVar(WolfgateCVars.ChatTabs, ChatTab.Format(_tabs));
        }

        // Every saved tab showing LOOC gets SOOC too, once.
        if (!_cfg.GetCVar(WolfgateCVars.ChatTabsSubtleOocAdded))
        {
            var added = false;
            foreach (var tab in _tabs)
            {
                if ((tab.Channels & ChatChannel.LOOC) == 0 || (tab.Channels & ChatChannel.SubtleOOC) != 0)
                    continue;

                tab.Channels |= ChatChannel.SubtleOOC;
                added = true;
            }

            _cfg.SetCVar(WolfgateCVars.ChatTabsSubtleOocAdded, true);
            if (added)
                _cfg.SetCVar(WolfgateCVars.ChatTabs, ChatTab.Format(_tabs));
        }

        if (_tabs.Count == 0)
        {
            _tabs.Add(new ChatTab(Loc.GetString("wf-chat-tabs-default-all"), ChatTab.AllChannels));
            _tabs.Add(new ChatTab(Loc.GetString("wf-chat-tabs-default-local"),
                ChatChannel.Local | ChatChannel.Whisper | ChatChannel.Emotes | ChatChannel.Subtle));
            _tabs.Add(new ChatTab(Loc.GetString("wf-chat-tabs-default-radio"), ChatChannel.Radio));
            _tabs.Add(new ChatTab(Loc.GetString("wf-chat-tabs-default-ooc"), ChatChannel.OOC | ChatChannel.LOOC | ChatChannel.SubtleOOC));
        }

        Selected = 0;
        TabsChanged?.Invoke();
        OnFilterChanged();
    }

    public void Select(int index)
    {
        if (index == Selected || index < 0 || index >= _tabs.Count)
            return;

        Selected = index;
        OnFilterChanged();
    }

    /// <summary>
    /// Adds an empty tab and selects it. Returns its index, or null at <see cref="ChatTab.MaxTabs"/>.
    /// </summary>
    public int? Add()
    {
        if (_tabs.Count >= ChatTab.MaxTabs)
            return null;

        _tabs.Add(new ChatTab(Loc.GetString("wf-chat-tabs-new-name"), ChatChannel.None));
        Save();
        TabsChanged?.Invoke();
        Select(_tabs.Count - 1);
        return _tabs.Count - 1;
    }

    public void Remove(int index)
    {
        if (_tabs.Count <= 1 || index < 0 || index >= _tabs.Count)
            return;

        var wasSelected = index == Selected;
        _tabs.RemoveAt(index);
        if (index < Selected || Selected >= _tabs.Count)
            Selected--;

        Save();
        TabsChanged?.Invoke();

        if (wasSelected)
            OnFilterChanged();
    }

    public void Rename(int index, string name)
    {
        name = ChatTab.SanitizeName(name);
        if (index < 0 || index >= _tabs.Count || name.Length == 0 || _tabs[index].Name == name)
            return;

        _tabs[index].Name = name;
        Save();
        TabsChanged?.Invoke();
    }

    public void SetChannels(int index, ChatChannel channels)
    {
        if (index < 0 || index >= _tabs.Count || _tabs[index].Channels == channels)
            return;

        _tabs[index].Channels = channels;
        Save();

        if (index == Selected)
            OnFilterChanged();
        else
            UnreadChanged?.Invoke();
    }

    /// <summary>
    /// Swaps a tab with its left (-1) or right (1) neighbour. Returns the tab's new index.
    /// </summary>
    public int Move(int index, int direction)
    {
        var target = index + direction;
        if (index < 0 || index >= _tabs.Count || target < 0 || target >= _tabs.Count)
            return index;

        (_tabs[index], _tabs[target]) = (_tabs[target], _tabs[index]);
        if (Selected == index)
            Selected = target;
        else if (Selected == target)
            Selected = index;

        Save();
        TabsChanged?.Invoke();
        return target;
    }

    /// <summary>
    /// Messages a tab shows that the player hasn't seen, because they arrived while another tab hid them.
    /// </summary>
    public int Unread(ChatTab tab)
    {
        var unread = 0;
        foreach (var (channel, count) in _unseen)
        {
            if ((tab.Channels & channel) != 0)
                unread += count;
        }

        return unread;
    }

    private void OnFilterChanged()
    {
        // Everything on the selected tab's channels is on show now.
        foreach (var channel in ChannelFilterPopup.WfChannels)
        {
            if ((SelectedTab.Channels & channel) != 0)
                _unseen.Remove(channel);
        }

        FilterChanged?.Invoke();
        UnreadChanged?.Invoke();
    }

    private void Save()
    {
        _cfg.SetCVar(WolfgateCVars.ChatTabs, ChatTab.Format(_tabs));
        _cfg.SaveToFile();
    }

    private void OnMessageAdded(ChatMessage msg)
    {
        var channel = msg.Channel & ChatTab.KnownChannels;
        if (channel == 0 || (SelectedTab.Channels & channel) != 0)
            return;

        _unseen[channel] = _unseen.GetValueOrDefault(channel) + 1;

        var shown = false;
        foreach (var tab in _tabs)
        {
            shown |= (tab.Channels & channel) != 0;
        }

        if (!shown)
            return;

        UnreadChanged?.Invoke();

        // The selected tab hides this message, so no chat box plays its sound cue. Another tab shows it, so play
        // the cue now; chat boxes mark it read when a tab first shows it.
        if (msg is { Read: false, AudioPath: { } path } && _chat.Chats.Count > 0)
        {
            _audio?.PlayGlobal(new SoundPathSpecifier(path), Filter.Local(), false,
                AudioParams.Default.WithVolume(msg.AudioVolume));
        }
    }

    /// <summary>
    /// A tab's button text: its name, plus its unread count if it has one.
    /// </summary>
    public string Label(ChatTab tab)
    {
        var unread = Unread(tab);
        if (unread <= 0)
            return tab.Name;

        var count = unread > UnreadCap ? $"{UnreadCap}+" : unread.ToString();
        return Loc.GetString("wf-chat-tabs-unread", ("name", tab.Name), ("count", count));
    }
}
