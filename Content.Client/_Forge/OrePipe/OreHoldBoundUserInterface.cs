using System.Numerics;
using Content.Shared._Forge.OrePipe;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using static Robust.Client.UserInterface.Controls.BoxContainer;

namespace Content.Client._Forge.OrePipe;

public sealed class OreHoldBoundUserInterface : BoundUserInterface
{
    private OreHoldWindow? _window;

    public OreHoldBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _window = new OreHoldWindow();
        _window.OnClose += Close;
        _window.OnEject += (proto, amount) => SendMessage(new OreHoldEjectMessage(proto, amount));
        _window.OpenCentered();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is OreHoldBoundUserInterfaceState st)
            _window?.UpdateState(st);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
            _window?.Dispose();
    }
}

public sealed class OreHoldWindow : DefaultWindow
{
    public event Action<string, int>? OnEject;

    private readonly Label _capacityLabel;
    private readonly BoxContainer _list;

    public OreHoldWindow()
    {
        Title = Loc.GetString("ore-hold-ui-title");
        MinSize = new Vector2(420, 280);

        var root = new BoxContainer
        {
            Orientation = LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
            Margin = new Thickness(8),
        };

        _capacityLabel = new Label { Margin = new Thickness(0, 0, 0, 6) };
        root.AddChild(_capacityLabel);

        var scroll = new ScrollContainer
        {
            HorizontalExpand = true,
            VerticalExpand = true,
        };
        _list = new BoxContainer
        {
            Orientation = LayoutOrientation.Vertical,
            HorizontalExpand = true,
        };
        scroll.AddChild(_list);
        root.AddChild(scroll);
        Contents.AddChild(root);
    }

    public void UpdateState(OreHoldBoundUserInterfaceState state)
    {
        _capacityLabel.Text = Loc.GetString("ore-hold-ui-capacity",
            ("count", state.TotalCount),
            ("max", state.Capacity));

        _list.RemoveAllChildren();

        if (state.Entries.Count == 0)
        {
            _list.AddChild(new Label
            {
                Text = Loc.GetString("ore-hold-ui-empty"),
                HorizontalAlignment = HAlignment.Center,
            });
            return;
        }

        foreach (var entry in state.Entries)
        {
            var row = new BoxContainer
            {
                Orientation = LayoutOrientation.Horizontal,
                HorizontalExpand = true,
                Margin = new Thickness(0, 2),
            };

            row.AddChild(new Label
            {
                Text = Loc.GetString("ore-hold-ui-entry", ("name", entry.Name), ("count", entry.Count)),
                HorizontalExpand = true,
                MinWidth = 180,
            });

            foreach (var amount in new[] { 1, 5, 10, 30 })
            {
                var btn = new Button
                {
                    Text = amount.ToString(),
                    Disabled = entry.Count < amount,
                    MinWidth = 40,
                };
                var proto = entry.PrototypeId;
                var amt = amount;
                btn.OnPressed += _ => OnEject?.Invoke(proto, amt);
                row.AddChild(btn);
            }

            var allBtn = new Button
            {
                Text = Loc.GetString("ore-hold-ui-eject-all-type"),
                Disabled = entry.Count <= 0,
                MinWidth = 50,
            };
            var allProto = entry.PrototypeId;
            var allCount = entry.Count;
            allBtn.OnPressed += _ => OnEject?.Invoke(allProto, allCount);
            row.AddChild(allBtn);

            _list.AddChild(row);
        }
    }
}
