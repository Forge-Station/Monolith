using System.Numerics;
using Content.Shared.Research.Components;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.GameObjects;
using static Robust.Client.UserInterface.Controls.BoxContainer;

namespace Content.Client.Research.UI;

public sealed class ResearchServerLinkBoundUserInterface : BoundUserInterface
{
    private ResearchServerLinkWindow? _window;

    public ResearchServerLinkBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _window = new ResearchServerLinkWindow();
        _window.OnClose += Close;
        _window.OnAuthorize += client => SendMessage(new ResearchServerAuthorizeClientMessage(client));
        _window.OnRevoke += client => SendMessage(new ResearchServerRevokeClientMessage(client));
        _window.OpenCentered();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is ResearchServerLinkBoundInterfaceState linkState)
            _window?.UpdateState(linkState);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
            _window?.Dispose();
    }
}

public sealed class ResearchServerLinkWindow : DefaultWindow
{
    public event Action<NetEntity>? OnAuthorize;
    public event Action<NetEntity>? OnRevoke;

    private readonly BoxContainer _list;
    private readonly Label _emptyLabel;

    public ResearchServerLinkWindow()
    {
        Title = Loc.GetString("research-server-link-menu-title");
        MinSize = new Vector2(480, 360);

        var root = new BoxContainer
        {
            Orientation = LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
            Margin = new Thickness(12),
            SeparationOverride = 8,
        };

        root.AddChild(new Label
        {
            Text = Loc.GetString("research-server-link-menu-description"),
            HorizontalExpand = true,
        });

        _emptyLabel = new Label
        {
            Text = Loc.GetString("research-server-link-menu-empty"),
            HorizontalExpand = true,
            Visible = false,
        };
        root.AddChild(_emptyLabel);

        var scroll = new ScrollContainer
        {
            HorizontalExpand = true,
            VerticalExpand = true,
        };

        _list = new BoxContainer
        {
            Orientation = LayoutOrientation.Vertical,
            HorizontalExpand = true,
            SeparationOverride = 4,
        };
        scroll.AddChild(_list);
        root.AddChild(scroll);

        Contents.AddChild(root);
    }

    public void UpdateState(ResearchServerLinkBoundInterfaceState state)
    {
        _list.RemoveAllChildren();
        _emptyLabel.Visible = state.Clients.Count == 0;

        foreach (var entry in state.Clients)
        {
            var row = new BoxContainer
            {
                Orientation = LayoutOrientation.Horizontal,
                HorizontalExpand = true,
                SeparationOverride = 8,
            };

            var status = entry.Authorized
                ? (entry.Connected
                    ? Loc.GetString("research-server-link-status-connected")
                    : Loc.GetString("research-server-link-status-authorized"))
                : Loc.GetString("research-server-link-status-unauthorized");

            row.AddChild(new Label
            {
                Text = Loc.GetString("research-server-link-entry", ("name", entry.Name), ("status", status)),
                HorizontalExpand = true,
            });

            if (entry.Authorized)
            {
                var revoke = new Button
                {
                    Text = Loc.GetString("research-server-link-revoke"),
                };
                var client = entry.Client;
                revoke.OnPressed += _ => OnRevoke?.Invoke(client);
                row.AddChild(revoke);
            }
            else
            {
                var authorize = new Button
                {
                    Text = Loc.GetString("research-server-link-authorize"),
                };
                var client = entry.Client;
                authorize.OnPressed += _ => OnAuthorize?.Invoke(client);
                row.AddChild(authorize);
            }

            _list.AddChild(row);
        }
    }
}
