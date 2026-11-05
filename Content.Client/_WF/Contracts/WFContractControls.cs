using System.Linq;
using System.Numerics;
using Content.Shared._WF.Contracts;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._WF.Contracts;

/// <summary>Colours and wording shared by the board and the PDA app.</summary>
public static class WFContractStyle
{
    public static readonly Color Panel = Color.FromHex("#14191f");
    public static readonly Color Card = Color.FromHex("#1b222a");
    public static readonly Color CardSelected = Color.FromHex("#23344a");
    public static readonly Color Accent = Color.FromHex("#5fc3e4");
    public static readonly Color Bright = Color.FromHex("#e8ecef");
    public static readonly Color Dim = Color.FromHex("#8b98a5");
    public static readonly Color Good = Color.FromHex("#7fd18b");
    public static readonly Color Bad = Color.FromHex("#e07070");
    public static readonly Color Warn = Color.FromHex("#e0b050");

    /// <summary>Tier colours, 1 to 5, from safe to deadly.</summary>
    private static readonly Color[] TierColors =
    [
        Color.FromHex("#7fd18b"), Color.FromHex("#b5d97a"), Color.FromHex("#e0c060"), Color.FromHex("#e09050"),
        Color.FromHex("#e06060"),
    ];

    public static Color TierColor(int tier) => TierColors[Math.Clamp(tier - 1, 0, TierColors.Length - 1)];

    public static string Category(WFContractCategory category) =>
        Loc.GetString($"wf-contract-category-{category.ToString().ToLowerInvariant()}");

    public static string Money(int amount) => Loc.GetString("wf-contract-thalers", ("amount", amount));

    /// <summary>A span as m:ss, or h:mm:ss over an hour.</summary>
    public static string Clock(TimeSpan span)
    {
        if (span < TimeSpan.Zero)
            span = TimeSpan.Zero;

        return span.TotalHours >= 1 ? span.ToString(@"h\:mm\:ss") : span.ToString(@"m\:ss");
    }

    public static PanelContainer Background(Color color, Control child)
    {
        var panel = new PanelContainer { PanelOverride = new StyleBoxFlat { BackgroundColor = color } };
        panel.AddChild(child);
        return panel;
    }

    public static Label Label(string text, Color color, string? style = null)
    {
        var label = new Label { Text = text, FontColorOverride = color, ClipText = true };
        if (style != null)
            label.StyleClasses.Add(style);
        return label;
    }
}

/// <summary>One contract in a list: tier, title, contractor, pay and time.</summary>
public sealed class WFContractRow : ContainerButton
{
    public readonly WFContractView Contract;
    private readonly PanelContainer _panel;
    private readonly Label _time;
    private readonly IGameTiming _timing;

    public WFContractRow(WFContractView contract, IGameTiming timing)
    {
        Contract = contract;
        _timing = timing;
        HorizontalExpand = true;
        Margin = new Thickness(0, 0, 0, 3);

        _panel = new PanelContainer { PanelOverride = new StyleBoxFlat { BackgroundColor = WFContractStyle.Card } };
        AddChild(_panel);

        var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, Margin = new Thickness(6, 4) };
        _panel.AddChild(row);

        var tier = new PanelContainer
        {
            PanelOverride = new StyleBoxFlat { BackgroundColor = WFContractStyle.TierColor(contract.Tier).WithAlpha(0.25f) },
            MinSize = new Vector2(30, 0),
            Margin = new Thickness(0, 0, 6, 0),
        };
        tier.AddChild(new Label
        {
            Text = Loc.GetString("wf-contract-tier-short", ("tier", contract.Tier)),
            FontColorOverride = WFContractStyle.TierColor(contract.Tier),
            HorizontalAlignment = HAlignment.Center,
            VerticalAlignment = VAlignment.Center,
        });
        row.AddChild(tier);

        var text = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true };
        var title = WFContractStyle.Label(contract.Title, contract.Locked == null ? WFContractStyle.Bright : WFContractStyle.Dim);
        text.AddChild(title);
        var sub = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };
        sub.AddChild(WFContractStyle.Label(contract.Contractor, contract.ContractorColor));
        sub.AddChild(WFContractStyle.Label("  ·  " + WFContractStyle.Category(contract.Category), WFContractStyle.Dim));
        if (contract.MinCrew > 1)
            sub.AddChild(WFContractStyle.Label("  ·  " + Loc.GetString("wf-contract-row-crew", ("crew", contract.MinCrew)), WFContractStyle.Warn));
        if (contract.Holder is { } holder)
            sub.AddChild(WFContractStyle.Label("  ·  " + Loc.GetString("wf-contract-row-holder", ("holder", holder)), WFContractStyle.Accent));
        text.AddChild(sub);
        row.AddChild(text);

        var right = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, MinWidth = 110 };
        right.AddChild(new Label
        {
            Text = contract.Status is WFContractStatus.Offered or WFContractStatus.Active
                ? WFContractStyle.Money(contract.Reward)
                : Loc.GetString($"wf-contract-status-{contract.Status.ToString().ToLowerInvariant()}"),
            FontColorOverride = contract.Status switch
            {
                WFContractStatus.Completed => WFContractStyle.Good,
                WFContractStatus.Failed or WFContractStatus.Abandoned => WFContractStyle.Bad,
                WFContractStatus.Void => WFContractStyle.Dim,
                _ => WFContractStyle.Accent,
            },
            HorizontalAlignment = HAlignment.Right,
        });
        _time = new Label { FontColorOverride = WFContractStyle.Dim, HorizontalAlignment = HAlignment.Right };
        right.AddChild(_time);
        row.AddChild(right);
        UpdateTime();
    }

    public void SetSelected(bool selected)
    {
        _panel.PanelOverride = new StyleBoxFlat { BackgroundColor = selected ? WFContractStyle.CardSelected : WFContractStyle.Card };
    }

    /// <summary>The time left for a live contract; the time allowed for an offer.</summary>
    public void UpdateTime()
    {
        _time.Text = Contract.Status switch
        {
            WFContractStatus.Active when Contract.Deadline is { } deadline =>
                Loc.GetString("wf-contract-time-left", ("time", WFContractStyle.Clock(deadline - _timing.CurTime))),
            WFContractStatus.Offered => Loc.GetString("wf-contract-time-allowed", ("minutes", (int) Contract.Duration.TotalMinutes)),
            _ => string.Empty,
        };
    }
}

/// <summary>A contract's full details, with one action button underneath.</summary>
public sealed class WFContractDetails : BoxContainer
{
    private readonly IGameTiming _timing;
    private readonly BoxContainer _body;
    private readonly Button _action;
    private readonly Label _note;
    private Label? _clock;
    private WFContractView? _contract;

    public event Action<WFContractView>? OnAction;

    public WFContractDetails(IGameTiming timing)
    {
        _timing = timing;
        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = true;
        VerticalExpand = true;

        var scroll = new ScrollContainer { VerticalExpand = true, HScrollEnabled = false };
        _body = new BoxContainer { Orientation = LayoutOrientation.Vertical, Margin = new Thickness(8), HorizontalExpand = true };
        scroll.AddChild(_body);
        AddChild(WFContractStyle.Background(WFContractStyle.Panel, scroll));
        ((PanelContainer) Children.Last()).VerticalExpand = true;

        _note = new Label { FontColorOverride = WFContractStyle.Warn, Visible = false, Margin = new Thickness(0, 4, 0, 0) };
        AddChild(_note);
        _action = new Button { Margin = new Thickness(0, 4, 0, 0), MinHeight = 32 };
        _action.OnPressed += _ =>
        {
            if (_contract != null)
                OnAction?.Invoke(_contract);
        };
        AddChild(_action);
        Clear();
    }

    public void Clear()
    {
        _contract = null;
        _clock = null;
        _body.RemoveAllChildren();
        _body.AddChild(new Label
        {
            Text = Loc.GetString("wf-contract-details-none"),
            FontColorOverride = WFContractStyle.Dim,
            HorizontalAlignment = HAlignment.Center,
        });
        _action.Visible = false;
        _note.Visible = false;
    }

    /// <summary>Shows a contract. <paramref name="action"/> labels the button; null hides it.</summary>
    public void Show(WFContractView contract, string? action, string? blocked = null)
    {
        _contract = contract;
        _body.RemoveAllChildren();

        var title = new RichTextLabel();
        title.SetMessage(FormattedMessage.FromMarkupPermissive(Loc.GetString("wf-contract-details-title",
            ("title", FormattedMessage.EscapeText(contract.Title)))));
        _body.AddChild(title);

        var header = new BoxContainer { Orientation = LayoutOrientation.Horizontal, Margin = new Thickness(0, 2, 0, 6) };
        header.AddChild(WFContractStyle.Label(contract.Contractor, contract.ContractorColor));
        header.AddChild(WFContractStyle.Label("  ·  " + WFContractStyle.Category(contract.Category), WFContractStyle.Dim));
        header.AddChild(WFContractStyle.Label("  ·  " + Loc.GetString("wf-contract-tier", ("tier", contract.Tier)),
            WFContractStyle.TierColor(contract.Tier)));
        _body.AddChild(header);

        _body.AddChild(new Label
        {
            Text = contract.Employer,
            FontColorOverride = contract.ContractorColor,
            Margin = new Thickness(0, 0, 0, 6),
        });

        var briefing = new RichTextLabel { Margin = new Thickness(0, 0, 0, 8) };
        briefing.SetMessage(FormattedMessage.FromMarkupPermissive(contract.Briefing));
        _body.AddChild(briefing);

        _body.AddChild(Section("wf-contract-details-objectives"));
        foreach (var objective in contract.Objectives)
        {
            var line = new RichTextLabel { Margin = new Thickness(6, 0, 0, 0) };
            line.SetMessage(FormattedMessage.FromMarkupPermissive(Loc.GetString("wf-contract-details-objective",
                ("done", objective.Done), ("text", objective.Text))));
            _body.AddChild(line);
        }

        _body.AddChild(Section("wf-contract-details-location"));
        _body.AddChild(new Label
        {
            Text = contract.Location,
            FontColorOverride = WFContractStyle.Bright,
            Margin = new Thickness(6, 0, 0, 0),
        });
        if (contract.Coordinates is { } coords)
        {
            _body.AddChild(new Label
            {
                Text = Loc.GetString("wf-contract-details-coordinates", ("x", (int) coords.X), ("y", (int) coords.Y)),
                FontColorOverride = WFContractStyle.Accent,
                Margin = new Thickness(6, 0, 0, 0),
            });
        }

        _body.AddChild(Section("wf-contract-details-terms"));
        _body.AddChild(Term("wf-contract-details-reward", WFContractStyle.Money(contract.Reward), WFContractStyle.Good));
        foreach (var item in contract.Requires)
        {
            _body.AddChild(Term("wf-contract-details-requires", item, WFContractStyle.Warn));
        }
        if (contract.MinCrew > 1)
            _body.AddChild(Term("wf-contract-details-crew", Loc.GetString("wf-contract-row-crew", ("crew", contract.MinCrew)), WFContractStyle.Warn));
        if (contract.CrewBonus is { } bonus)
            _body.AddChild(Term("wf-contract-details-crew-bonus", bonus, WFContractStyle.Good));
        if (contract.Share is { } share)
            _body.AddChild(Term("wf-contract-details-share", share, WFContractStyle.Good));
        if (contract.Credits is { } credits)
            _body.AddChild(Term("wf-contract-details-bonus", credits, WFContractStyle.Good));
        if (contract.Holder is { } holder)
            _body.AddChild(Term("wf-contract-details-holder", holder, WFContractStyle.Accent));
        foreach (var harm in contract.Harms)
        {
            _body.AddChild(Term("wf-contract-details-harms", harm, WFContractStyle.Bad));
        }
        _body.AddChild(Term("wf-contract-details-deposit",
            contract.Deposit > 0 ? WFContractStyle.Money(contract.Deposit) : Loc.GetString("wf-contract-details-deposit-none"),
            contract.Deposit > 0 ? WFContractStyle.Warn : WFContractStyle.Dim));
        _body.AddChild(Term("wf-contract-details-time",
            Loc.GetString("wf-contract-time-allowed", ("minutes", (int) contract.Duration.TotalMinutes)), WFContractStyle.Bright));
        if (contract.Deadline != null)
        {
            var row = Term(contract.Status == WFContractStatus.Active ? "wf-contract-details-deadline" : "wf-contract-details-offer-ends",
                string.Empty, contract.Status == WFContractStatus.Active ? WFContractStyle.Warn : WFContractStyle.Dim);
            _clock = (Label) row.Children.Last();
            _body.AddChild(row);
        }
        else
        {
            _clock = null;
        }

        _note.Text = blocked ?? string.Empty;
        _note.Visible = blocked != null;
        _action.Visible = action != null;
        _action.Text = action ?? string.Empty;
        _action.Disabled = blocked != null;
        UpdateTime();
    }

    public void UpdateTime()
    {
        if (_clock != null && _contract?.Deadline is { } deadline)
            _clock.Text = WFContractStyle.Clock(deadline - _timing.CurTime);
    }

    private static Label Section(string key)
    {
        return new Label
        {
            Text = Loc.GetString(key),
            FontColorOverride = WFContractStyle.Accent,
            Margin = new Thickness(0, 6, 0, 2),
        };
    }

    private static BoxContainer Term(string key, string value, Color color)
    {
        var row = new BoxContainer { Orientation = LayoutOrientation.Horizontal, Margin = new Thickness(6, 0, 0, 0) };
        row.AddChild(new Label { Text = Loc.GetString(key), FontColorOverride = WFContractStyle.Dim, MinWidth = 110 });
        row.AddChild(new Label { Text = value, FontColorOverride = color });
        return row;
    }
}

/// <summary>Standing with each contractor: rank and progress to the next.</summary>
public sealed class WFContractStandingList : BoxContainer
{
    public WFContractStandingList()
    {
        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = true;
    }

    public void Update(List<WFContractStandingView> standing)
    {
        RemoveAllChildren();
        AddChild(new Label
        {
            Text = Loc.GetString("wf-contract-standing-help"),
            FontColorOverride = WFContractStyle.Dim,
            Margin = new Thickness(0, 0, 0, 6),
        });

        foreach (var entry in standing)
        {
            var card = new BoxContainer { Orientation = LayoutOrientation.Vertical, Margin = new Thickness(6, 4) };
            var top = new BoxContainer { Orientation = LayoutOrientation.Horizontal };
            top.AddChild(new Label { Text = entry.Contractor, FontColorOverride = entry.Color, HorizontalExpand = true });
            top.AddChild(new Label { Text = entry.Rank, FontColorOverride = WFContractStyle.Bright });
            card.AddChild(top);
            card.AddChild(new Label
            {
                Text = entry.Present ? entry.Description : Loc.GetString("wf-contract-standing-absent", ("description", entry.Description)),
                FontColorOverride = WFContractStyle.Dim,
                ClipText = true,
            });

            var floor = Math.Min(0, entry.Points);
            var bar = new ProgressBar
            {
                MinValue = entry.Points < 0 ? floor - 1 : 0,
                MaxValue = entry.NextRankAt ?? Math.Max(1, entry.Points),
                Value = entry.Points,
                MinHeight = 6,
                Margin = new Thickness(0, 3, 0, 0),
            };
            card.AddChild(bar);
            card.AddChild(new Label
            {
                Text = entry.NextRankAt is { } next
                    ? Loc.GetString("wf-contract-standing-progress", ("points", entry.Points), ("next", next))
                    : Loc.GetString("wf-contract-standing-max", ("points", entry.Points)),
                FontColorOverride = WFContractStyle.Dim,
            });
            AddChild(WFContractStyle.Background(WFContractStyle.Card, card));
            ((Control) Children.Last()).Margin = new Thickness(0, 0, 0, 4);
        }
    }
}
