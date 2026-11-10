using System.Numerics;
using Content.Client._WF.UserInterface.WindowPopout;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;
using Robust.Shared.Timing;

namespace Content.Client._WF.ChatTabs;

/// <summary>
/// The row of chat tabs above a chat log: click a tab to select it, right-click it to edit it.
/// </summary>
public sealed class ChatTabBar : BoxContainer
{
    public const string StyleClassChatTab = "wfChatTab";

    private readonly ChatTabsUIController _tabs;
    private readonly BoxContainer _buttons;
    private readonly ScrollContainer _scroll;
    private readonly Button _addButton;
    private readonly ChatTabPopup _settings = new();

    private int _shownSelected = -1;
    private bool _scrollToSelected;

    /// <summary>
    /// Pops the chat log out into its own window, or docks it back.
    /// </summary>
    public readonly WolfgatePopoutButton PopoutButton;

    public ChatTabBar()
    {
        _tabs = UserInterfaceManager.GetUIController<ChatTabsUIController>();

        Orientation = LayoutOrientation.Horizontal;
        SeparationOverride = 4;
        Margin = new Thickness(8, 6, 8, 0);

        _buttons = new BoxContainer
        {
            Orientation = LayoutOrientation.Horizontal,
            SeparationOverride = 2,
        };

        // Scrolls sideways when the tabs don't fit; the add button gives the row its height.
        _scroll = new ScrollContainer
        {
            HorizontalExpand = true,
            HScrollEnabled = true,
            VScrollEnabled = false,
            HScrollBarHidden = true,
            Children = { _buttons },
        };
        AddChild(_scroll);

        _addButton = new Button
        {
            Text = "+",
            ToolTip = Loc.GetString("wf-chat-tabs-add"),
            StyleClasses = { StyleClassChatTab },
        };
        _addButton.OnPressed += _ => AddTab();
        AddChild(_addButton);

        PopoutButton = new WolfgatePopoutButton();
        AddChild(PopoutButton);
    }

    protected override void EnteredTree()
    {
        base.EnteredTree();

        _tabs.TabsChanged += Rebuild;
        _tabs.FilterChanged += Refresh;
        _tabs.UnreadChanged += Refresh;
        Rebuild();
    }

    protected override void ExitedTree()
    {
        base.ExitedTree();

        _tabs.TabsChanged -= Rebuild;
        _tabs.FilterChanged -= Refresh;
        _tabs.UnreadChanged -= Refresh;
        _settings.Close();
        _settings.Orphan();
    }

    private void Rebuild()
    {
        _buttons.RemoveAllChildren();

        for (var i = 0; i < _tabs.Tabs.Count; i++)
        {
            var index = i;
            var button = new Button
            {
                ToggleMode = true,
                ToolTip = Loc.GetString("wf-chat-tabs-tab-tooltip"),
                StyleClasses = { StyleClassChatTab },
            };

            button.OnPressed += _ =>
            {
                _tabs.Select(index);
                Refresh();
            };
            button.OnKeyBindDown += args =>
            {
                if (args.Function != EngineKeyFunctions.UIRightClick)
                    return;

                OpenSettings(index);
                args.Handle();
            };

            _buttons.AddChild(button);
        }

        _addButton.Disabled = _tabs.Tabs.Count >= ChatTab.MaxTabs;
        _scrollToSelected = true;
        Refresh();
    }

    private void Refresh()
    {
        for (var i = 0; i < _buttons.ChildCount && i < _tabs.Tabs.Count; i++)
        {
            var button = (Button) _buttons.GetChild(i);
            button.Text = _tabs.Label(_tabs.Tabs[i]);
            button.Pressed = i == _tabs.Selected;
        }

        if (_shownSelected == _tabs.Selected)
            return;

        _shownSelected = _tabs.Selected;
        _scrollToSelected = true;
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        if (!_scrollToSelected || _tabs.Selected >= _buttons.ChildCount)
            return;

        // A new button has no size until the next layout.
        var button = _buttons.GetChild(_tabs.Selected);
        if (button.Size.X <= 0)
            return;

        _scrollToSelected = false;

        var left = button.Position.X;
        var right = left + button.Size.X;
        if (left < _scroll.HScrollTarget)
            _scroll.HScrollTarget = left;
        else if (right > _scroll.HScrollTarget + _scroll.Size.X)
            _scroll.HScrollTarget = right - _scroll.Size.X;
    }

    private void AddTab()
    {
        // The new tab's button has no position until the next layout, so the popup opens under the add button.
        if (_tabs.Add() is { } index)
            OpenSettings(index, _addButton);
    }

    /// <summary>
    /// Opens the settings popup for a tab, under its button or another control.
    /// </summary>
    public void OpenSettings(int index, Control? anchor = null)
    {
        if (Root == null || index < 0 || index >= _buttons.ChildCount)
            return;

        // The popup opens in whichever OS window the bar is in.
        if (_settings.Parent != Root.ModalRoot)
        {
            _settings.Orphan();
            Root.ModalRoot.AddChild(_settings);
        }

        anchor ??= _buttons.GetChild(index);
        _settings.SetTab(index);
        _settings.Open(UIBox2.FromDimensions(anchor.GlobalPosition + new Vector2(0, anchor.Size.Y), Vector2.One));
    }
}
