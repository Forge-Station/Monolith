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

    private readonly Label _capacityPrefixLabel;
    private readonly Label _capacityCountLabel;
    private readonly Label _capacitySuffixLabel;
    private readonly ProgressBar _capacityBar;
    private readonly BoxContainer _list;

    public OreHoldWindow()
    {
        Title = Loc.GetString("ore-hold-ui-title");
        MinSize = new Vector2(550, 400);

        var root = new BoxContainer
        {
            Orientation = LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
            Margin = new Thickness(16),
            SeparationOverride = 12,
        };

        // Header section with capacity info
        var headerContainer = new BoxContainer
        {
            Orientation = LayoutOrientation.Vertical,
            HorizontalExpand = true,
            SeparationOverride = 8,
        };

        // Capacity label with colored count
        var capacityLabelContainer = new BoxContainer
        {
            Orientation = LayoutOrientation.Horizontal,
            HorizontalAlignment = HAlignment.Center,
            SeparationOverride = 4,
            Margin = new Thickness(0, 0, 0, 4),
        };

        _capacityPrefixLabel = new Label
        {
            Text = Loc.GetString("ore-hold-ui-capacity-prefix"),
        };
        _capacityPrefixLabel.StyleClasses.Add("LabelHeading");
        capacityLabelContainer.AddChild(_capacityPrefixLabel);

        _capacityCountLabel = new Label
        {
            Text = "0",
            Modulate = Color.Cyan,
        };
        _capacityCountLabel.StyleClasses.Add("LabelHeading");
        capacityLabelContainer.AddChild(_capacityCountLabel);

        _capacitySuffixLabel = new Label
        {
            Text = " / 0",
        };
        _capacitySuffixLabel.StyleClasses.Add("LabelHeading");
        capacityLabelContainer.AddChild(_capacitySuffixLabel);

        headerContainer.AddChild(capacityLabelContainer);

        _capacityBar = new ProgressBar
        {
            HorizontalExpand = true,
            MinHeight = 12,
            MinValue = 0,
            MaxValue = 100,
        };
        headerContainer.AddChild(_capacityBar);

        root.AddChild(headerContainer);

        // Scroll container for ore entries
        var scroll = new ScrollContainer
        {
            HorizontalExpand = true,
            VerticalExpand = true,
            ModulateSelfOverride = new Color(0.05f, 0.05f, 0.05f, 0.2f),
        };
        
        _list = new BoxContainer
        {
            Orientation = LayoutOrientation.Vertical,
            HorizontalExpand = true,
            SeparationOverride = 8,
        };
        scroll.AddChild(_list);
        root.AddChild(scroll);
        
        Contents.AddChild(root);
    }

    public void UpdateState(OreHoldBoundUserInterfaceState state)
    {
        _capacityCountLabel.Text = state.TotalCount.ToString();
        _capacitySuffixLabel.Text = $" / {state.Capacity}";

        // Update progress bar
        var fillPercentage = state.Capacity > 0 ? (float)state.TotalCount / state.Capacity * 100 : 0;
        _capacityBar.Value = fillPercentage;
        
        // Change color based on fill level
        _capacityBar.StyleClasses.Clear();
        if (fillPercentage >= 90)
            _capacityBar.StyleClasses.Add("ProgressBarDanger");
        else if (fillPercentage >= 70)
            _capacityBar.StyleClasses.Add("ProgressBarWarning");
        else
            _capacityBar.StyleClasses.Add("ProgressBarDefault");

        _list.RemoveAllChildren();

        if (state.Entries.Count == 0)
        {
            var emptyContainer = new BoxContainer
            {
                Orientation = LayoutOrientation.Vertical,
                HorizontalExpand = true,
                VerticalExpand = true,
                VerticalAlignment = VAlignment.Center,
                SeparationOverride = 12,
            };
            
            var emptyLabel = new Label
            {
                Text = Loc.GetString("ore-hold-ui-empty"),
                HorizontalAlignment = HAlignment.Center,
                Margin = new Thickness(0, 20),
            };
            emptyLabel.StyleClasses.Add("LabelSubText");
            emptyContainer.AddChild(emptyLabel);
            
            _list.AddChild(emptyContainer);
            return;
        }

        foreach (var entry in state.Entries)
        {
            var oreCard = CreateOreCard(entry);
            _list.AddChild(oreCard);
        }
    }

    private Control CreateOreCard(OreHoldEntry entry)
    {
        var card = new PanelContainer
        {
            HorizontalExpand = true,
            MinHeight = 80,
            Margin = new Thickness(0, 4),
        };
        card.StyleClasses.Add("PanelDefault");

        var cardContent = new BoxContainer
        {
            Orientation = LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            VerticalExpand = true,
            Margin = new Thickness(12),
            SeparationOverride = 16,
        };

        // Left side: Ore info with colored background
        var infoContainer = new PanelContainer
        {
            MinWidth = 200,
            Margin = new Thickness(0, 8, 8, 8),
            StyleClasses = { "PanelDark" },
        };

        var infoContent = new BoxContainer
        {
            Orientation = LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
            SeparationOverride = 6,
        };

        var nameLabel = new Label
        {
            Text = entry.Name,
            HorizontalExpand = true,
            StyleClasses = { "LabelHeading" },
        };
        infoContent.AddChild(nameLabel);

        var countLabel = new Label
        {
            Text = $"x{entry.Count}",
            HorizontalExpand = true,
            StyleClasses = { "LabelBig" },
        };
        infoContent.AddChild(countLabel);

        infoContainer.AddChild(infoContent);
        cardContent.AddChild(infoContainer);

        // Right side: Eject buttons
        var buttonsContainer = new BoxContainer
        {
            Orientation = LayoutOrientation.Vertical,
            VerticalAlignment = VAlignment.Center,
            HorizontalExpand = true,
            SeparationOverride = 8,
        };

        // Quick action buttons
        var quickActions = new BoxContainer
        {
            Orientation = LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            SeparationOverride = 6,
        };

        var amounts = new[] { (1, "1"), (10, "10"), (50, "50"), (100, "100") };
        foreach (var (amount, label) in amounts)
        {
            var btn = new Button
            {
                Text = label,
                Disabled = entry.Count < amount,
                MinWidth = 50,
                MinHeight = 36,
                ToolTip = Loc.GetString("ore-hold-ui-eject-tooltip", ("amount", amount)),
            };
            btn.StyleClasses.Add("OpenBoth");
            var proto = entry.PrototypeId;
            var amt = amount;
            btn.OnPressed += _ => OnEject?.Invoke(proto, amt);
            quickActions.AddChild(btn);
        }

        buttonsContainer.AddChild(quickActions);

        // Custom amount and all buttons
        var bottomActions = new BoxContainer
        {
            Orientation = LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            SeparationOverride = 6,
        };

        var customBtn = new Button
        {
            Text = Loc.GetString("ore-hold-ui-custom-amount"),
            Disabled = entry.Count <= 0,
            MinWidth = 80,
            MinHeight = 36,
            ToolTip = Loc.GetString("ore-hold-ui-custom-amount-tooltip"),
        };
        customBtn.StyleClasses.Add("OpenBoth");
        var customProto = entry.PrototypeId;
        customBtn.OnPressed += _ => ShowCustomAmountDialog(customProto, entry.Count);
        bottomActions.AddChild(customBtn);

        var allBtn = new Button
        {
            Text = Loc.GetString("ore-hold-ui-eject-all-type"),
            Disabled = entry.Count <= 0,
            MinWidth = 80,
            MinHeight = 36,
            ToolTip = Loc.GetString("ore-hold-ui-eject-all-tooltip"),
        };
        allBtn.StyleClasses.Add("OpenBoth");
        allBtn.StyleClasses.Add("Caution");
        var allProto = entry.PrototypeId;
        var allCount = entry.Count;
        allBtn.OnPressed += _ => OnEject?.Invoke(allProto, allCount);
        bottomActions.AddChild(allBtn);

        buttonsContainer.AddChild(bottomActions);
        cardContent.AddChild(buttonsContainer);
        card.AddChild(cardContent);

        return card;
    }

    private void ShowCustomAmountDialog(string protoId, int maxAmount)
    {
        var dialog = new CustomAmountDialog(protoId, maxAmount);
        dialog.OnConfirm += amount => OnEject?.Invoke(protoId, amount);
        dialog.OpenCentered();
    }

    private class CustomAmountDialog : DefaultWindow
    {
        public event Action<int>? OnConfirm;
        private readonly LineEdit _amountInput;
        private readonly Button _confirmButton;
        private readonly int _maxAmount;

        public CustomAmountDialog(string protoId, int maxAmount)
        {
            Title = Loc.GetString("ore-hold-ui-custom-amount-title");
            MinSize = new Vector2(300, 150);
            _maxAmount = maxAmount;

            var root = new BoxContainer
            {
                Orientation = LayoutOrientation.Vertical,
                HorizontalExpand = true,
                VerticalExpand = true,
                Margin = new Thickness(16),
                SeparationOverride = 12,
            };

            var instructionLabel = new Label
            {
                Text = Loc.GetString("ore-hold-ui-custom-amount-instruction", ("max", maxAmount)),
                HorizontalExpand = true,
            };
            root.AddChild(instructionLabel);

            _amountInput = new LineEdit
            {
                HorizontalExpand = true,
                MinHeight = 30,
                PlaceHolder = Loc.GetString("ore-hold-ui-custom-amount-placeholder"),
            };
            _amountInput.OnTextChanged += OnAmountChanged;
            root.AddChild(_amountInput);

            var buttonContainer = new BoxContainer
            {
                Orientation = LayoutOrientation.Horizontal,
                HorizontalExpand = true,
                SeparationOverride = 8,
            };

            var cancelButton = new Button
            {
                Text = Loc.GetString("ore-hold-ui-cancel"),
                MinWidth = 80,
            };
            cancelButton.OnPressed += _ => Close();
            buttonContainer.AddChild(cancelButton);

            _confirmButton = new Button
            {
                Text = Loc.GetString("ore-hold-ui-confirm"),
                MinWidth = 80,
                Disabled = true,
            };
            _confirmButton.OnPressed += OnConfirmClicked;
            buttonContainer.AddChild(_confirmButton);

            root.AddChild(buttonContainer);
            Contents.AddChild(root);
        }

        private void OnAmountChanged(LineEdit.LineEditEventArgs args)
        {
            if (int.TryParse(args.Text, out var amount) && amount > 0 && amount <= _maxAmount)
            {
                _confirmButton.Disabled = false;
            }
            else
            {
                _confirmButton.Disabled = true;
            }
        }

        private void OnConfirmClicked(Button.ButtonEventArgs args)
        {
            if (int.TryParse(_amountInput.Text, out var amount) && amount > 0 && amount <= _maxAmount)
            {
                OnConfirm?.Invoke(amount);
                Close();
            }
        }
    }
}
