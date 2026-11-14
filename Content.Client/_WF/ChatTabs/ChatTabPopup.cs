using System.Linq;
using Content.Client.Stylesheets;
using Content.Client.UserInterface.Systems.Chat;
using Content.Client.UserInterface.Systems.Chat.Controls;
using Content.Shared.Chat;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.ChatTabs;

/// <summary>
/// Settings for one chat tab: its name, the channels it shows, its place in the row, and deleting it.
/// </summary>
public sealed class ChatTabPopup : Popup
{
    private readonly ChatTabsUIController _tabs;
    private readonly ChatUIController _chat;

    private readonly LineEdit _name;
    private readonly BoxContainer _leftChannels;
    private readonly BoxContainer _rightChannels;
    private readonly Button _moveLeft;
    private readonly Button _moveRight;
    private readonly Button _delete;
    private readonly Dictionary<ChatChannel, CheckBox> _checkboxes = new();

    private const int NoTab = -1;

    private int _index = NoTab;

    public ChatTabPopup()
    {
        _tabs = UserInterfaceManager.GetUIController<ChatTabsUIController>();
        _chat = UserInterfaceManager.GetUIController<ChatUIController>();

        _name = new LineEdit { MinWidth = 200 };
        _name.OnTextEntered += _ => ApplyName();
        _name.OnFocusExit += _ => ApplyName();
        _name.IsValid = text => text.Length <= ChatTab.MaxNameLength;

        _leftChannels = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true };
        _rightChannels = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true };
        foreach (var channel in ChannelFilterPopup.WfChannels)
        {
            var checkbox = new CheckBox { Text = Loc.GetString($"hud-chatbox-channel-{channel}") };
            checkbox.OnPressed += _ => ApplyChannels();
            _checkboxes.Add(channel, checkbox);
        }

        var all = new Button { Text = Loc.GetString("wf-chat-tabs-settings-all"), HorizontalExpand = true };
        all.OnPressed += _ => SetChannels(ChatTab.AllChannels);
        var none = new Button { Text = Loc.GetString("wf-chat-tabs-settings-none"), HorizontalExpand = true };
        none.OnPressed += _ => SetChannels(ChatChannel.None);

        _moveLeft = new Button { Text = "<", ToolTip = Loc.GetString("wf-chat-tabs-settings-move-left") };
        _moveLeft.OnPressed += _ => Move(-1);
        _moveRight = new Button { Text = ">", ToolTip = Loc.GetString("wf-chat-tabs-settings-move-right") };
        _moveRight.OnPressed += _ => Move(1);
        _delete = new Button
        {
            Text = Loc.GetString("wf-chat-tabs-settings-delete"),
            HorizontalExpand = true,
            StyleClasses = { StyleBase.ButtonCaution },
        };
        _delete.OnPressed += _ =>
        {
            var index = _index;
            _index = NoTab;
            Close();
            _tabs.Remove(index);
        };

        AddChild(new PanelContainer
        {
            StyleClasses = { "BorderedWindowPanel" },
            Children =
            {
                new BoxContainer
                {
                    Orientation = BoxContainer.LayoutOrientation.Vertical,
                    SeparationOverride = 4,
                    Margin = new Thickness(10),
                    Children =
                    {
                        new Label { Text = Loc.GetString("wf-chat-tabs-settings-name") },
                        _name,
                        new Label { Text = Loc.GetString("wf-chat-tabs-settings-channels") },
                        new BoxContainer
                        {
                            Orientation = BoxContainer.LayoutOrientation.Horizontal,
                            SeparationOverride = 12,
                            Children = { _leftChannels, _rightChannels },
                        },
                        new BoxContainer
                        {
                            Orientation = BoxContainer.LayoutOrientation.Horizontal,
                            SeparationOverride = 4,
                            Children = { all, none },
                        },
                        new BoxContainer
                        {
                            Orientation = BoxContainer.LayoutOrientation.Horizontal,
                            SeparationOverride = 4,
                            Children = { _moveLeft, _moveRight, _delete },
                        },
                    },
                },
            },
        });

        OnPopupHide += ApplyName;
    }

    /// <summary>
    /// Points the popup at a tab and fills it in.
    /// </summary>
    public void SetTab(int index)
    {
        _index = index;
        _name.Text = _tabs.Tabs[index].Name;
        Refresh();
    }

    private void Refresh()
    {
        var tab = _tabs.Tabs[_index];
        var listed = ListedChannels();

        // Two columns, filled top to bottom.
        _leftChannels.RemoveAllChildren();
        _rightChannels.RemoveAllChildren();
        var shown = ChannelFilterPopup.WfChannels.Where(channel => (listed & channel) != 0).ToList();
        for (var i = 0; i < shown.Count; i++)
        {
            var column = i < (shown.Count + 1) / 2 ? _leftChannels : _rightChannels;
            column.AddChild(_checkboxes[shown[i]]);
        }

        foreach (var (channel, checkbox) in _checkboxes)
        {
            checkbox.Pressed = (tab.Channels & channel) != 0;
        }

        _moveLeft.Disabled = _index == 0;
        _moveRight.Disabled = _index >= _tabs.Tabs.Count - 1;
        _delete.Disabled = _tabs.Tabs.Count <= 1;
    }

    /// <summary>
    /// Channels the player can receive now, plus the ones they may get later. Admin channels are left out for
    /// players who can't see them.
    /// </summary>
    private ChatChannel ListedChannels()
    {
        return _chat.FilterableChannels | (ChatTab.KnownChannels & ~ChatChannel.AdminRelated);
    }

    private void ApplyName()
    {
        if (_index != NoTab && _index < _tabs.Tabs.Count)
            _tabs.Rename(_index, _name.Text);
    }

    private void ApplyChannels()
    {
        // Channels with no checkbox on show keep their setting.
        var listed = ListedChannels();
        var channels = _tabs.Tabs[_index].Channels & ~listed;
        foreach (var (channel, checkbox) in _checkboxes)
        {
            if (checkbox.Pressed && (listed & channel) != 0)
                channels |= channel;
        }

        _tabs.SetChannels(_index, channels);
    }

    private void SetChannels(ChatChannel channels)
    {
        _tabs.SetChannels(_index, channels);
        Refresh();
    }

    private void Move(int direction)
    {
        _index = _tabs.Move(_index, direction);
        Refresh();
    }
}
