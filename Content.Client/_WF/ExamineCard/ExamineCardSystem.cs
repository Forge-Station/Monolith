using Content.Client._WF.Wolfmed.Medical;
using Content.Client.Examine;
using Content.Shared._WF.ExamineCard;
using Content.Shared._WF.Wolfmed.Examine;
using Content.Shared.Humanoid;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._WF.ExamineCard;

/// <summary>Rebuilds a character's examine tooltip around their ID card.</summary>
public sealed partial class ExamineCardSystem : SharedExamineCardSystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private IResourceCache _resources = default!;
    [Dependency] private ExamineSystem _examine = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private SpriteSystem _sprites = default!;

    /// <summary>Widest the tooltip may grow with the detailed look open beside the card.</summary>
    private const float MaxTooltipWidth = 980f;

    private WolfmedAnalyzerIcons? _icons;

    /// <summary>Whether the detailed look is open; kept between examines so a medic only opens it once.</summary>
    private bool _detailsOpen;

    /// <summary>Whether the full description is open beside the card; kept between examines.</summary>
    private bool _descriptionOpen;

    /// <summary>The card comes just before the examine response, so an older one is stale.</summary>
    private static readonly TimeSpan PendingTimeout = TimeSpan.FromSeconds(5);

    private (ExamineCardEvent Card, TimeSpan Time)? _pending;

    /// <summary>The last card received and not yet shown.</summary>
    public ExamineCardEvent? Pending => _pending?.Card;

    /// <summary>How many cards have been put into a tooltip.</summary>
    public int Shown { get; private set; }

    /// <summary>The last server card put into a tooltip.</summary>
    public ExamineCardEvent? LastShown { get; private set; }

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<ExamineCardEvent>(OnCard);
        SubscribeLocalEvent<HumanoidAppearanceComponent, ClientExaminedEvent>(OnClientExamined);
        SubscribeLocalEvent<ExamineServerInfoShownEvent>(OnServerInfoShown);
    }

    /// <summary>
    /// Turns the client's own guess at a character's tooltip into a card straight away, from what the client
    /// knows, so nothing plain flashes up and nothing waits on the server. The server's card replaces it.
    /// </summary>
    private void OnClientExamined(Entity<HumanoidAppearanceComponent> ent, ref ClientExaminedEvent args)
    {
        if (args.Examiner != _player.LocalEntity || _examine.TooltipBox is not { } box)
            return;

        var detailed = _examine.IsInDetailsRange(args.Examiner, ent);
        Build(box, ent, BuildCard(ent, args.Examiner, detailed, predicted: true), new FormattedMessage());
    }

    private void OnCard(ExamineCardEvent ev)
    {
        _pending = (ev, _timing.RealTime);
    }

    private void OnServerInfoShown(ExamineServerInfoShownEvent ev)
    {
        if (_pending is not { } pending || GetEntity(pending.Card.Target) != ev.Target)
        {
            // Refused outright, such as out of sight: an empty card says so instead of a bare line.
            if (!ev.KnowTarget && HasComp<HumanoidAppearanceComponent>(ev.Target))
            {
                var unseen = new ExamineCardEvent
                {
                    Target = GetNetEntity(ev.Target),
                    Range = ExamineCardRange.Unseen,
                    Stamp = ExamineCardStamp.NoVisual,
                };
                Build(ev.Box, ev.Target, unseen, new FormattedMessage());
                Shown++;
            }
            return;
        }

        _pending = null;
        if (_timing.RealTime - pending.Time > PendingTimeout)
            return;

        Build(ev.Box, ev.Target, pending.Card, ev.Message);
        LastShown = pending.Card;
        Shown++;
    }

    /// <summary>
    /// Replaces the tooltip's rows with the card, folding status lines into chips and the health look into the
    /// tooltip, so nothing needs a second click. The header row stays, hidden, since the tooltip finds rows by index.
    /// </summary>
    public ExamineCardControl Build(BoxContainer box, EntityUid target, ExamineCardEvent data, FormattedMessage message)
    {
        var card = new ExamineCardControl(data, target, EntityManager, _resources, _prototype);
        var buttons = new List<Control>();

        foreach (var child in box.Children)
        {
            switch (child)
            {
                case BoxContainer { Name: "ExamineButtonsHBox" } row:
                    foreach (var button in row.Children)
                    {
                        if (button is ExamineButton examine && Folded(examine, data))
                            continue;
                        buttons.Add(button);
                    }
                    break;
            }
        }

        card.SetButtons(buttons);

        for (var i = box.ChildCount - 1; i >= 1; i--)
        {
            box.GetChild(i).Orphan();
        }

        if (box.ChildCount > 0)
            box.GetChild(0).Visible = false;

        // The card is the frame now, so the tooltip's own panel goes.
        if (box.Parent is PanelContainer panel)
        {
            panel.PanelOverride = new StyleBoxEmpty();
            panel.ModulateSelfOverride = null;
        }
        box.MaxWidth = MaxTooltipWidth;
        if (box.Parent?.Parent is Popup popup)
            popup.MaxWidth = MaxTooltipWidth;

        var side = new ExamineSideColumn
        {
            Card = card,
            Margin = new Thickness(4, 2, 2, 4),
            VerticalAlignment = Control.VAlignment.Top,
        };
        var detail = new PanelContainer
        {
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = ExamineCardControl.Inset.WithAlpha(0.95f),
                ContentMarginLeftOverride = 8,
                ContentMarginRightOverride = 8,
                ContentMarginTopOverride = 6,
                ContentMarginBottomOverride = 6,
            },
            Visible = _detailsOpen,
        };
        var description = SidePanel(_descriptionOpen && data.Description != null);
        side.AddChild(description);
        side.AddChild(detail);
        box.AddChild(new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            Children = { card, side },
        });

        var chips = new List<ExamineCardChip>();
        var text = ExamineCardText.Fold(message, data.Patterns, chips);
        var health = data.Health == null ? null : ExamineCardText.Fold(data.Health, data.Patterns, chips);
        card.SetStatus(chips);
        card.SetRemarks(text);

        var wounds = health == null ? new List<ExamineCardWound>() : ReadWounds(health);
        var hasDetail = health != null && (wounds.Count > 0 || !string.IsNullOrWhiteSpace(health.ToString()));
        if (hasDetail)
            FillDetail(detail, health!);

        card.SetCondition(wounds, hasDetail, _detailsOpen, open =>
        {
            _detailsOpen = open;
            detail.Visible = open;
        });

        if (data.Description != null)
            FillDescription(description, FormattedMessage.FromMarkupPermissive(data.Description), "wf-examine-card-description-title");

        if (data.Description != null)
        {
            card.SetDescription(data.Description, _descriptionOpen, open =>
                {
                    _descriptionOpen = open;
                    description.Visible = open;
                });
        }
        return card;
    }

    /// <summary>A dark panel beside the card; an accent draws a stripe down its left edge.</summary>
    private static PanelContainer SidePanel(bool visible, Color? accent = null)
    {
        return new PanelContainer
        {
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = ExamineCardControl.Inset.WithAlpha(0.95f),
                BorderColor = accent ?? Color.Transparent,
                BorderThickness = new Thickness(accent == null ? 0 : 3, 0, 0, 0),
                ContentMarginLeftOverride = 8,
                ContentMarginRightOverride = 8,
                ContentMarginTopOverride = 6,
                ContentMarginBottomOverride = 6,
            },
            Visible = visible,
        };
    }

    /// <summary>The whole description, scrolling if it runs long.</summary>
    private void FillDescription(PanelContainer panel, FormattedMessage text, string title)
    {
        var label = new RichTextLabel { MaxWidth = 380 };
        label.SetMessage(text);
        panel.AddChild(new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            Children =
            {
                new Label
                {
                    Text = Loc.GetString(title),
                    FontColorOverride = ExamineCardControl.InsetText,
                    Margin = new Thickness(0, 0, 0, 4),
                },
                new ScrollContainer
                {
                    MaxHeight = 260,
                    MaxWidth = 390,
                    HScrollEnabled = false,
                    ReturnMeasure = true,
                    Children = { label },
                },
            },
        });
    }

    /// <summary>Reads each wounded part and its findings out of the health look's tags.</summary>
    private List<ExamineCardWound> ReadWounds(FormattedMessage health)
    {
        _icons ??= new WolfmedAnalyzerIcons(_resources, _sprites);
        var wounds = new List<ExamineCardWound>();
        string? part = null;
        var accent = string.Empty;
        var findings = new List<WolfmedLookObservation>();

        void Flush()
        {
            if (part == null || findings.Count == 0)
                return;

            var texts = new List<string>();
            foreach (var finding in findings)
            {
                texts.Add(finding.Text);
            }

            var summary = Loc.GetString("wf-examine-card-wound-summary", ("part", part), ("findings", string.Join(", ", texts)));
            var wound = new ExamineCardWound(part, WolfmedWoundStyle.Look(accent), summary);
            foreach (var finding in findings)
            {
                wound.Findings.Add((_icons!.Get(finding.Icon), WolfmedWoundStyle.Look(finding.Colour)));
            }
            wounds.Add(wound);
        }

        foreach (var node in health.Nodes)
        {
            if (node.Closing)
                continue;

            if (node.Name == WolfmedLookTag.Part && WolfmedLookTag.TryReadPart(node, out var name, out var nodeAccent))
            {
                Flush();
                part = name;
                accent = nodeAccent;
                findings.Clear();
            }
            else if (node.Name == WolfmedLookTag.Finding && WolfmedLookTag.TryReadFinding(node, out var finding))
            {
                findings.Add(finding);
            }
        }

        Flush();
        return wounds;
    }

    /// <summary>The full health look, rows and all, for the panel beside the card.</summary>
    private void FillDetail(PanelContainer detail, FormattedMessage health)
    {
        var column = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
        column.AddChild(new Label
        {
            Text = Loc.GetString("wf-examine-card-details-title"),
            FontColorOverride = ExamineCardControl.InsetText,
            Margin = new Thickness(0, 0, 0, 4),
        });
        detail.AddChild(column);

        if (_examine.TryAddWolfmedLookMessage(column, health))
            return;

        var label = new RichTextLabel { MaxWidth = 420 };
        label.SetMessage(health);
        column.AddChild(label);
    }

    /// <summary>The ID and health buttons only repeat what the card now shows.</summary>
    private bool Folded(ExamineButton button, ExamineCardEvent data)
    {
        var text = button.Verb.Text;
        return text == Loc.GetString("id-examinable-component-verb-text")
               || data.HasHealth && text == Loc.GetString("health-examinable-verb-text")
               || data.Description != null && text == Loc.GetString("detail-examinable-verb-text");
    }
}
