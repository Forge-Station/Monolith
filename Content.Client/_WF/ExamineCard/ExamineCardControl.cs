using System.Linq;
using Robust.Shared.Timing;
using System.Numerics;
using Content.Client.Resources;
using Content.Shared._WF.ExamineCard;
using Content.Shared.Mobs;
using Content.Shared.StatusIcon;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;
using Robust.Shared.Input;
using Robust.Shared.Utility;

namespace Content.Client._WF.ExamineCard;

/// <summary>A character's examine card: photo, ID details, a verdict stamp and the worn items.</summary>
public sealed class ExamineCardControl : PanelContainer
{
    public const float CardWidth = 680f;

    /// <summary>Width of the status column on the card's right.</summary>
    public const float SideWidth = 150f;

    /// <summary>Tallest the status list and the wound list grow before they scroll.</summary>
    public const float StatusMaxHeight = 128f;
    public const float ConditionMaxHeight = 150f;
    public const float RemarksMaxHeight = 90f;
    public const int WornVisibleRows = 3;

    /// <summary>Fixed width of a wound row's part name, so its icons always wrap in the same space.</summary>
    private const float WoundNameWidth = 58f;

    /// <summary>Room for a scroll bar when a list overflows.</summary>
    private const float ScrollGutter = 8f;

    /// <summary>Room in the card's left column, for rows that wrap.</summary>
    public const float ContentWidth = CardWidth - SideWidth - 32f;

    /// <summary>How much of the description shows on the card before DETAILS.</summary>
    private const float DescriptionPreviewHeight = 44f;

    private const string FontBold = "/Fonts/RobotoMono/RobotoMono-Bold.ttf";
    private const string FontRegular = "/Fonts/RobotoMono/RobotoMono-Regular.ttf";
    private const string Logo = "/Textures/_WF/Branding/Logo/logo.png";

    /// <summary>The opaque part of the 800x266 logo.</summary>
    private static readonly UIBox2 LogoRegion = new(48, 67, 754, 199);

    public static readonly Color Paper = Color.FromHex("#161A18");
    public static readonly Color PaperShade = Color.FromHex("#262C29");
    public static readonly Color Ink = Color.FromHex("#E4DECD");
    public static readonly Color InkFaded = Color.FromHex("#9C9684");
    public static readonly Color Band = Color.FromHex("#45574D");
    public static readonly Color BandText = Color.FromHex("#E8DFC8");
    public static readonly Color PhotoBackdrop = Color.FromHex("#3A433E");
    public static readonly Color Inset = Color.FromHex("#0C0F0E");

    /// <summary>How opaque the card's fills are; text stays solid so it reads over the game.</summary>
    public const float PaperAlpha = 0.94f;
    public const float FillAlpha = 0.45f;
    public const float BandAlpha = 0.88f;
    public static readonly Color InsetText = Color.FromHex("#C9D3C6");

    /// <summary>The photo frame; the headshot replaces its sprite when there is one.</summary>
    public readonly PanelContainer Photo;

    private readonly SpriteView _photoSprite;
    private readonly BoxContainer _status;
    private readonly BoxContainer _condition;
    private readonly BoxContainer _description;
    private readonly BoxContainer _remarks;
    private readonly Font _regular;
    private readonly BoxContainer _footer;
    private readonly Font _small;
    private readonly Content.Client._WF.Companies.WFCompanyClientSystem _companies;
    private (TextureRect Rect, int Rank)? _waitingRankIcon;
    private (TextureRect Rect, int Company, int Version)? _waitingLogo;

    /// <summary>Fills in company art that was still downloading when the card was built.</summary>
    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        if (_waitingRankIcon is { } rank && _companies.GetRankIconLatest(rank.Rank) is { } rankTexture)
        {
            rank.Rect.Texture = rankTexture;
            _waitingRankIcon = null;
        }
        if (_waitingLogo is { } waiting && _companies.GetLogo(waiting.Company, waiting.Version) is { } logoTexture)
        {
            waiting.Rect.Texture = logoTexture;
            _waitingLogo = null;
        }
    }

    public ExamineCardControl(ExamineCardEvent card, EntityUid target, IEntityManager entities,
        IResourceCache resources, IPrototypeManager prototypes)
    {
        var sprites = entities.System<SpriteSystem>();
        var bold = resources.GetFont(FontBold, 10);
        var small = resources.GetFont(FontBold, 8);
        var regular = resources.GetFont(FontRegular, 10);

        _small = small;
        _regular = regular;
        MinWidth = CardWidth;
        HorizontalExpand = true;
        // Its own height only, never stretched to match tall side panels.
        VerticalAlignment = VAlignment.Top;
        Margin = new Thickness(2, 2, 2, 4);
        // Slightly see-through, so the card sits over the game rather than blocking it out.
        PanelOverride = Box(Paper.WithAlpha(PaperAlpha), null, 0);

        var root = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
        AddChild(root);

        // Header band: the title says how much the examiner can make out, the colour how the character is doing.
        var band = BandColour(card);
        var header = new PanelContainer { PanelOverride = Box(band.WithAlpha(BandAlpha), null, 0) };
        var headerRow = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            Margin = new Thickness(8, 3, 8, 3),
        };
        headerRow.AddChild(Text(Loc.GetString(TitleKey(card)), bold, BandText, true));
        var tag = card.State switch
        {
            MobState.Critical => Loc.GetString("wf-examine-card-state-critical"),
            MobState.Dead => Loc.GetString("wf-examine-card-state-dead"),
            _ => card.Range == ExamineCardRange.Unseen ? string.Empty : Serial(card.IdName ?? card.Name ?? string.Empty),
        };
        headerRow.AddChild(Text(tag, small, BandText.WithAlpha(card.State == MobState.Alive ? 0.7f : 1f)));
        header.AddChild(headerRow);
        root.AddChild(header);

        // Body: photo, then the fields with the stamp laid over them, then the status column behind a rule.
        var bodyRow = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 10,
            Margin = new Thickness(8, 8, 4, 6),
        };

        Photo = new PanelContainer
        {
            PanelOverride = Box(PhotoBackdrop, Ink, 1),
            SetSize = new Vector2(84, 104),
            RectClipContent = true,
            VerticalAlignment = VAlignment.Top,
        };
        _photoSprite = new SpriteView
        {
            OverrideDirection = Direction.South,
            Scale = new Vector2(2, 2),
            Stretch = SpriteView.StretchMode.None,
            SetSize = new Vector2(80, 100),
            HorizontalAlignment = HAlignment.Center,
            VerticalAlignment = VAlignment.Center,
        };
        Photo.AddChild(_photoSprite);
        if (card.Range == ExamineCardRange.Unseen)
        {
            Photo.PanelOverride = Box(Color.FromHex("#4E5257"), Ink, 1);
            Photo.AddChild(new Label
            {
                Text = "?",
                FontOverride = resources.GetFont(FontBold, 28),
                FontColorOverride = Ink.WithAlpha(0.6f),
                HorizontalAlignment = HAlignment.Center,
                VerticalAlignment = VAlignment.Center,
            });
        }
        else
        {
            _photoSprite.SetEntity(target);
        }
        bodyRow.AddChild(Photo);

        var fields = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            SeparationOverride = 2,
        };
        // Out of sight nothing is known; from afar the face and build show but the ID can't be read.
        var unseen = card.Range == ExamineCardRange.Unseen;
        var unknown = Loc.GetString("wf-examine-card-unknown");
        var illegible = Loc.GetString(unseen ? "wf-examine-card-unknown" : "wf-examine-card-illegible");
        var idBlank = card.Range == ExamineCardRange.Detailed ? null : illegible;
        fields.AddChild(Field("name", card.Name, small, bold, regular, unseen ? unknown : null, wrap: true));
        fields.AddChild(Field("appearance", card.Appearance, small, bold, regular, unseen ? unknown : null));
        fields.AddChild(Field("species", card.Species, small, bold, regular, unseen ? unknown : null));
        fields.AddChild(Rule(InkFaded.WithAlpha(0.5f), new Thickness(0, 3)));
        fields.AddChild(Field("id-name", card.IdName, small, bold, regular, idBlank, wrap: true));

        var job = Field("occupation", card.IdJob, small, bold, regular, idBlank);
        var companies = entities.System<Content.Client._WF.Companies.WFCompanyClientSystem>();
        // A company rank's own icon is the job icon its ID shows.
        _companies = companies;
        var rankIcon = card.CompanyRankId == 0 ? null : card.CompanyIconVersion > 0
            ? companies.GetRankIcon(card.CompanyRankId, card.CompanyIconVersion) : companies.GetRankIconLatest(card.CompanyRankId);
        if (rankIcon != null || card.JobIcon is { } icon && prototypes.TryIndex<JobIconPrototype>(icon, out _))
        {
            var value = job.GetChild(1);
            job.RemoveChild(value);
            var row = new BoxContainer
            {
                Orientation = BoxContainer.LayoutOrientation.Horizontal,
                HorizontalExpand = true,
                SeparationOverride = 4,
            };
            var jobIcon = new TextureRect
            {
                Texture = rankIcon ?? sprites.Frame0(prototypes.Index<JobIconPrototype>(card.JobIcon!.Value).Icon),
                TextureScale = new Vector2(2, 2),
                VerticalAlignment = VAlignment.Center,
            };
            row.AddChild(jobIcon);
            // A rank icon this client hasn't fetched yet swaps in when it arrives.
            if (rankIcon == null && card.CompanyRankId != 0)
                _waitingRankIcon = (jobIcon, card.CompanyRankId);
            value.HorizontalExpand = true;
            row.AddChild(value);
            job.AddChild(row);
        }
        fields.AddChild(job);
        if (card.Company != null && card.Range == ExamineCardRange.Detailed)
        {
            var company = Field("company", card.CompanyRank is { Length: > 0 } rank ? $"{card.Company} · {rank}" : card.Company,
                small, bold, regular, null, wrap: true);
            // A predicted card has no logo version; the company list usually knows it.
            var logoVersion = card.CompanyLogoVersion != 0 ? card.CompanyLogoVersion
                : companies.State?.Directory.FirstOrDefault(entry => entry.Id == card.CompanyId)?.LogoVersion ?? 0;
            var logo = companies.GetLogo(card.CompanyId, logoVersion);
            if (logoVersion != 0)
            {
                var value = company.GetChild(1);
                company.RemoveChild(value);
                var row = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, HorizontalExpand = true, SeparationOverride = 6 };
                var logoRect = new TextureRect { Texture = logo, SetSize = new Vector2(32, 32), Stretch = TextureRect.StretchMode.Scale,
                    VerticalAlignment = VAlignment.Center };
                row.AddChild(logoRect);
                if (logo == null)
                    _waitingLogo = (logoRect, card.CompanyId, logoVersion);
                value.HorizontalExpand = true;
                row.AddChild(value);
                company.AddChild(row);
            }
            fields.AddChild(company);
        }
        var fieldsHolder = new Control { HorizontalExpand = true, Children = { fields } };
        bodyRow.AddChild(fieldsHolder);

        var (stampText, stampInk) = card.Stamp switch
        {
            ExamineCardStamp.Match => ("wf-examine-card-stamp-match", Color.FromHex("#6DBE66")),
            ExamineCardStamp.Mismatch => ("wf-examine-card-stamp-mismatch", Color.FromHex("#E8584F")),
            ExamineCardStamp.Unverified => ("wf-examine-card-stamp-unverified", Color.FromHex("#E0A445")),
            ExamineCardStamp.TooFar => ("wf-examine-card-stamp-too-far", Color.FromHex("#A39C8C")),
            ExamineCardStamp.NoVisual => ("wf-examine-card-stamp-no-visual", Color.FromHex("#8C949C")),
            _ => ("wf-examine-card-stamp-no-id", Color.FromHex("#A39C8C")),
        };
        // The stamp sits at the foot of the status column, clear of the fields.
        var stamp = new ExamineCardStampControl(resources.GetFont(FontBold, 12), Loc.GetString(stampText),
            stampInk.WithAlpha(0.8f))
        {
            HorizontalAlignment = HAlignment.Right,
            Margin = new Thickness(0, 6, -4, -4),
        };

        // Left column: identity, description, worn items and remarks. Right column, full height: status and wounds.
        var left = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            Children = { bodyRow },
        };

        // Status chips as a list, then wounds, filled in once the examine text has been read. The column keeps its
        // width while empty, so the card doesn't jump when the server's chips arrive.
        _status = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 3,
        };
        _condition = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 3,
            Margin = new Thickness(0, 6, 0, 0),
            Visible = false,
        };
        var side = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SetWidth = SideWidth,
            Margin = new Thickness(8, 8, 8, 8),
            Children = { _status, _condition, new Control { VerticalExpand = true }, stamp },
        };
        SetStatus(Array.Empty<ExamineCardChip>());

        // The character's description, cut off, with a toggle for the whole of it beside the card.
        _description = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            Margin = new Thickness(8, 0, 4, 6),
            Visible = false,
        };
        left.AddChild(_description);

        // Worn and held items.
        left.AddChild(Rule(InkFaded.WithAlpha(0.6f), new Thickness(8, 0, 4, 0)));
        left.AddChild(Text(Loc.GetString("wf-examine-card-worn"), small, InkFaded, margin: new Thickness(8, 4, 4, 2)));

        var notice = card.Range switch
        {
            ExamineCardRange.Unseen => "wf-examine-card-worn-unseen",
            ExamineCardRange.Distant => "wf-examine-card-worn-distant",
            _ => card.Worn.Count == 0 ? "wf-examine-card-worn-none" : null,
        };
        if (notice != null)
        {
            left.AddChild(Text(Loc.GetString(notice), regular, InkFaded, margin: new Thickness(8, 0, 4, 6)));
        }

        if (card.Worn.Count > 0)
        {
            var grid = new GridContainer
            {
                Columns = 3,
                HSeparationOverride = 4,
                VSeparationOverride = 4,
                Margin = new Thickness(0, 0, ScrollGutter, 2),
            };
            foreach (var slot in card.Worn)
            {
                grid.AddChild(WornTile(slot, entities, small, resources.GetFont(FontRegular, 9)));
            }

            // Up to three whole rows show before it scrolls, never a partial one; the 2px bottom margin keeps the
            // last row's border clear of the clip edge.
            var tile = grid.GetChild(0);
            tile.Measure(new Vector2(float.PositiveInfinity, float.PositiveInfinity));
            var rows = Math.Min(WornVisibleRows, (card.Worn.Count + grid.Columns - 1) / grid.Columns);
            var wornHeight = rows * tile.DesiredSize.Y + (rows - 1) * grid.VSeparationOverride!.Value + 2;
            var worn = ScrollList(grid, MathF.Ceiling(wornHeight) + 1);
            worn.Margin = new Thickness(8, 0, 4, 8);
            left.AddChild(worn);
        }

        // Whatever examine text no rule folded away.
        _remarks = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            Margin = new Thickness(8, 0, 4, 8),
            Visible = false,
        };
        left.AddChild(_remarks);

        root.AddChild(new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            Children =
            {
                left,
                new PanelContainer
                {
                    PanelOverride = Box(InkFaded.WithAlpha(0.5f), null, 0),
                    MinWidth = 1,
                    Margin = new Thickness(0, 8),
                },
                side,
            },
        });

        // Footer band, the same colour as the header: the Wolfgate mark on the left, verb buttons on the right.
        _footer = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalAlignment = HAlignment.Right,
            HorizontalExpand = true,
        };
        var footerRow = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, MinHeight = 24 };
        footerRow.AddChild(new TextureRect
        {
            // The logo file has wide transparent margins; draw only the mark itself.
            Texture = new AtlasTexture(resources.GetResource<TextureResource>(Logo).Texture, LogoRegion),
            Stretch = TextureRect.StretchMode.KeepAspectCentered,
            SetSize = new Vector2(117, 22),
            Modulate = BandText.WithAlpha(0.85f),
            VerticalAlignment = VAlignment.Center,
            Margin = new Thickness(8, 3, 0, 3),
        });
        footerRow.AddChild(_footer);
        root.AddChild(new PanelContainer { PanelOverride = Box(band.WithAlpha(BandAlpha), null, 0), Children = { footerRow } });
    }

    private static string TitleKey(ExamineCardEvent card)
    {
        return card.Range switch
        {
            ExamineCardRange.Unseen => "wf-examine-card-title-unseen",
            ExamineCardRange.Distant => "wf-examine-card-title-distant",
            _ => card.Self ? "wf-examine-card-title-self" : "wf-examine-card-title",
        };
    }

    /// <summary>Red for the dying and the dead, otherwise a colour per range.</summary>
    public static Color BandColour(ExamineCardEvent card)
    {
        return card.State switch
        {
            MobState.Dead => Color.FromHex("#5E1F1B"),
            MobState.Critical => Color.FromHex("#8A3F1E"),
            _ => card.Range switch
            {
                ExamineCardRange.Unseen => Color.FromHex("#4E5257"),
                ExamineCardRange.Distant => Color.FromHex("#7A6233"),
                _ => card.Self ? Color.FromHex("#3F5266") : Band,
            },
        };
    }

    /// <summary>Shows the chips folded out of the examine text.</summary>
    public void SetStatus(IReadOnlyList<ExamineCardChip> chips)
    {
        _status.RemoveAllChildren();
        _status.AddChild(Text(Loc.GetString("wf-examine-card-status"), _small, InkFaded));
        if (chips.Count == 0)
        {
            _status.AddChild(Text(Loc.GetString("wf-examine-card-status-none"), _regular, InkFaded));
            return;
        }

        var list = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 3,
            Margin = new Thickness(0, 0, ScrollGutter, 0),
        };
        foreach (var chip in chips)
        {
            list.AddChild(Chip(chip, _small));
        }
        _status.AddChild(ScrollList(list, StatusMaxHeight));
    }

    /// <summary>A list that keeps its size and scrolls once it would grow past the height given.</summary>
    private static ScrollContainer ScrollList(Control list, float maxHeight)
    {
        return new ScrollContainer
        {
            HScrollEnabled = false,
            ReturnMeasure = true,
            MaxHeight = maxHeight,
            HorizontalExpand = true,
            Children = { list },
        };
    }

    /// <summary>
    /// Shows each wounded part as a row of icons, with the sentence on hover, and a button that opens the full
    /// look beside the card.
    /// </summary>
    public void SetCondition(IReadOnlyList<ExamineCardWound> wounds, bool hasDetail, bool open, Action<bool> toggled)
    {
        _condition.RemoveAllChildren();
        _condition.Visible = wounds.Count > 0 || hasDetail;
        if (!_condition.Visible)
            return;

        _condition.AddChild(Rule(InkFaded.WithAlpha(0.5f), new Thickness(0, 0, 0, 5)));
        _condition.AddChild(SectionHeader("wf-examine-card-condition", hasDetail, open, toggled));

        var list = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 3,
            Margin = new Thickness(0, 0, ScrollGutter, 0),
        };

        // Name in a fixed column, icons wrapping in the fixed width left beside it, so a row is always as tall as
        // its icons need and never spills into the next.
        var iconWidth = SideWidth - ScrollGutter - 13 - WoundNameWidth - 2;
        foreach (var wound in wounds)
        {
            var group = new BoxContainer
            {
                Orientation = BoxContainer.LayoutOrientation.Horizontal,
                SeparationOverride = 2,
            };
            group.AddChild(new Label
            {
                Text = wound.Part.ToUpperInvariant(),
                FontOverride = _small,
                FontColorOverride = InsetText,
                ClipText = true,
                SetWidth = WoundNameWidth,
                VerticalAlignment = VAlignment.Top,
                Margin = new Thickness(0, 2, 0, 0),
            });
            var icons = new ExamineChipFlow { Gap = 2, MaxRowWidth = iconWidth, HorizontalExpand = true };
            group.AddChild(icons);
            foreach (var finding in wound.Findings)
            {
                icons.AddChild(new TextureRect
                {
                    Texture = finding.Icon,
                    Modulate = finding.Colour,
                    Stretch = TextureRect.StretchMode.KeepAspectCentered,
                    SetSize = new Vector2(16, 16),
                });
            }

            list.AddChild(new PanelContainer
            {
                PanelOverride = new StyleBoxFlat
                {
                    BackgroundColor = Inset.WithAlpha(0.8f),
                    BorderColor = wound.Accent,
                    BorderThickness = new Thickness(3, 0, 0, 0),
                    ContentMarginLeftOverride = 5,
                    ContentMarginRightOverride = 5,
                    ContentMarginTopOverride = 2,
                    ContentMarginBottomOverride = 2,
                },
                ToolTip = wound.Summary,
                MouseFilter = MouseFilterMode.Stop,
                Children = { group },
            });
        }

        if (wounds.Count > 0)
            _condition.AddChild(ScrollList(list, ConditionMaxHeight));
    }

    /// <summary>Shows the start of the character's description, with a toggle for the rest beside the card.</summary>
    public void SetDescription(string? markup, bool open, Action<bool> toggled, bool hasNsfw = false,
        bool nsfwOpen = false, Action<bool>? nsfwToggled = null)
    {
        _description.RemoveAllChildren();
        var hasText = !string.IsNullOrWhiteSpace(markup);
        _description.Visible = hasText || hasNsfw;
        if (!_description.Visible)
            return;

        _description.AddChild(Rule(InkFaded.WithAlpha(0.5f), new Thickness(0, 0, 0, 5)));
        var header = SectionHeader("wf-examine-card-description", hasText, open, toggled);
        if (hasNsfw)
        {
            var heart = new ExamineCardHeartToggle(nsfwOpen) { VerticalAlignment = VAlignment.Center };
            if (nsfwToggled != null)
                heart.Toggled += nsfwToggled;
            header.AddChild(heart);
            heart.SetPositionInParent(1);
        }
        _description.AddChild(header);

        if (!hasText)
            return;

        var label = new RichTextLabel { HorizontalExpand = true };
        label.SetMessage(InInk(FormattedMessage.FromMarkupPermissive(markup!)), defaultColor: Ink);
        var preview = new Control();
        preview.AddChild(new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            MaxHeight = DescriptionPreviewHeight,
            MaxWidth = ContentWidth,
            RectClipContent = true,
            Children = { label },
        });

        // A cut-off preview ends in an ellipsis, laid over its last line.
        label.Measure(new Vector2(ContentWidth, float.PositiveInfinity));
        if (label.DesiredSize.Y > DescriptionPreviewHeight)
        {
            preview.AddChild(new PanelContainer
            {
                PanelOverride = Box(Paper, null, 0),
                HorizontalAlignment = HAlignment.Right,
                VerticalAlignment = VAlignment.Bottom,
                Children = { new Label { Text = "...", FontOverride = _regular, FontColorOverride = Ink, Margin = new Thickness(4, 0, 0, 0) } },
            });
        }
        _description.AddChild(preview);
    }

    private Control SectionHeader(string title, bool toggle, bool open, Action<bool> toggled)
    {
        var header = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, Margin = new Thickness(0, 0, 0, 3) };
        header.AddChild(Text(Loc.GetString(title), _small, InkFaded, true));
        if (toggle)
        {
            var button = new ExamineCardToggle(_small, open);
            button.Toggled += toggled;
            header.AddChild(button);
        }
        return header;
    }

    /// <summary>Markup with its colours stripped, since they were picked for a dark tooltip, not paper.</summary>
    public static FormattedMessage InInk(FormattedMessage message)
    {
        var ink = new FormattedMessage();
        foreach (var node in message.Nodes)
        {
            if (node.Name == null)
                ink.AddText(node.Value.StringValue ?? string.Empty);
            else if (node.Name is "bold" or "italic" or "bolditalic")
                ink.PushTag(node);
        }
        ink.TrimEnd();
        return ink;
    }

    /// <summary>Prints leftover examine text in the card's ink, without its own colours, which were picked for a dark tooltip.</summary>
    public void SetRemarks(FormattedMessage? message)
    {
        _remarks.RemoveAllChildren();
        if (message == null || string.IsNullOrWhiteSpace(message.ToString()))
        {
            _remarks.Visible = false;
            return;
        }

        var ink = InInk(message);

        _remarks.Visible = true;
        _remarks.AddChild(Rule(InkFaded.WithAlpha(0.6f), new Thickness(0, 0, 0, 4)));
        _remarks.AddChild(Text(Loc.GetString("wf-examine-card-remarks"), _small, InkFaded, margin: new Thickness(0, 0, 0, 2)));
        var label = new RichTextLabel { HorizontalExpand = true };
        label.SetMessage(ink, defaultColor: Ink);
        _remarks.AddChild(ScrollList(label, RemarksMaxHeight));
    }

    /// <summary>Moves the tooltip's verb buttons into the footer band.</summary>
    public void SetButtons(IEnumerable<Control> buttons)
    {
        foreach (var button in buttons)
        {
            button.Orphan();
            _footer.AddChild(button);
        }
    }

    public static Color ToneInk(ExamineChipTone tone)
    {
        return tone switch
        {
            ExamineChipTone.Good => Color.FromHex("#7CC47F"),
            ExamineChipTone.Calm => Color.FromHex("#7BAFE0"),
            ExamineChipTone.Warn => Color.FromHex("#E3A84E"),
            ExamineChipTone.Danger => Color.FromHex("#E8635C"),
            ExamineChipTone.Mind => Color.FromHex("#B68FE3"),
            _ => Color.FromHex("#A39C8C"),
        };
    }

    private static Control Chip(ExamineCardChip chip, Font font)
    {
        var ink = ToneInk(chip.Tone);
        return new PanelContainer
        {
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = ink.WithAlpha(0.16f),
                BorderColor = ink,
                BorderThickness = new Thickness(3, 0, 0, 0),
                ContentMarginLeftOverride = 6,
                ContentMarginRightOverride = 6,
                ContentMarginTopOverride = 1,
                ContentMarginBottomOverride = 1,
            },
            ToolTip = chip.Text,
            MouseFilter = MouseFilterMode.Stop,
            Children =
            {
                new Label { Text = chip.Label, FontOverride = font, FontColorOverride = ink },
            },
        };
    }

    /// <summary>Puts the headshot in the photo frame in place of the sprite.</summary>
    public void SetPhoto(Control portrait)
    {
        _photoSprite.Visible = false;
        portrait.Orphan();
        portrait.SetSize = new Vector2(80, 100);
        portrait.Margin = new Thickness(0);
        portrait.HorizontalAlignment = HAlignment.Center;
        portrait.VerticalAlignment = VAlignment.Center;
        Photo.AddChild(portrait);
    }

    /// <summary>Room for a field's value beside its label: a full 32-character name fits on one line.</summary>
    public const float ValueWidth = 290f;

    private static BoxContainer Field(string key, string? value, Font labelFont, Font valueFont, Font emptyFont,
        string? blank = null, bool wrap = false)
    {
        var row = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 6,
        };
        row.AddChild(new Label
        {
            Text = Loc.GetString($"wf-examine-card-field-{key}"),
            FontOverride = labelFont,
            FontColorOverride = InkFaded,
            MinWidth = 76,
            VerticalAlignment = VAlignment.Center,
        });

        var empty = blank != null || string.IsNullOrWhiteSpace(value);
        var text = !empty ? value! : blank ?? Loc.GetString(key == "name" ? "wf-examine-card-face-hidden" : "wf-examine-card-blank");

        // Names are never cut off: a long one wraps onto more lines instead.
        if (wrap && !empty && WrapLines(valueFont, text, ValueWidth) is { Count: > 1 } lines)
        {
            row.GetChild(0).VerticalAlignment = VAlignment.Top;
            var column = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true };
            foreach (var line in lines)
            {
                column.AddChild(new Label { Text = line, FontOverride = valueFont, FontColorOverride = Ink });
            }
            row.AddChild(column);
            return row;
        }

        row.AddChild(new Label
        {
            Text = text,
            ToolTip = empty ? null : text,
            FontOverride = empty ? emptyFont : valueFont,
            FontColorOverride = empty ? InkFaded : Ink,
            ClipText = true,
            HorizontalExpand = true,
            VerticalAlignment = VAlignment.Center,
        });
        return row;
    }

    /// <summary>A field, not a literal, so the compiler can't turn the concat into a char span the sandbox forbids.</summary>
    private static readonly string WordGap = " ";

    /// <summary>Greedy word wrap by the font's advances; a word too long for a line is broken inside.</summary>
    public static List<string> WrapLines(Font font, string text, float maxWidth)
    {
        float Width(string s)
        {
            var width = 0f;
            foreach (var rune in s.EnumerateRunes())
            {
                width += font.GetCharMetrics(rune, 1f)?.Advance ?? 0;
            }
            return width;
        }

        var lines = new List<string>();
        var line = string.Empty;
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = line.Length == 0 ? word : string.Concat(line, WordGap, word);
            if (Width(candidate) <= maxWidth)
            {
                line = candidate;
                continue;
            }

            if (line.Length > 0)
                lines.Add(line);

            // Break a word wider than a whole line.
            line = string.Empty;
            for (var i = 0; i < word.Length; i++)
            {
                var piece = word.Substring(i, 1);
                var next = string.Concat(line, piece);
                if (line.Length > 0 && Width(next) > maxWidth)
                {
                    lines.Add(line);
                    line = piece;
                }
                else
                {
                    line = next;
                }
            }
        }

        if (line.Length > 0)
            lines.Add(line);
        return lines;
    }

    private static Control WornTile(ExamineCardSlot slot, IEntityManager entities, Font labelFont, Font nameFont)
    {
        var tile = new PanelContainer
        {
            PanelOverride = Box(PaperShade.WithAlpha(FillAlpha), InkFaded.WithAlpha(0.6f), 1),
            HorizontalExpand = true,
            ToolTip = slot.Name,
        };
        var row = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 4,
            Margin = new Thickness(2),
        };

        var icon = new SpriteView
        {
            OverrideDirection = Direction.South,
            SetSize = new Vector2(24, 24),
        };
        if (entities.TryGetEntity(slot.Item, out var item) && entities.HasComponent<SpriteComponent>(item))
            icon.SetEntity(item);
        row.AddChild(icon);

        var text = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalAlignment = VAlignment.Center,
        };
        text.AddChild(new Label
        {
            Text = Loc.GetString($"wf-examine-card-slot-{slot.Slot.ToLowerInvariant()}"),
            FontOverride = labelFont,
            FontColorOverride = InkFaded,
        });
        text.AddChild(new Label
        {
            Text = slot.Name,
            FontOverride = nameFont,
            FontColorOverride = Ink,
            ClipText = true,
            HorizontalExpand = true,
        });
        row.AddChild(text);
        tile.AddChild(row);
        tile.MouseFilter = MouseFilterMode.Stop;
        return tile;
    }

    private static Label Text(string text, Font font, Color color, bool expand = false, Thickness? margin = null)
    {
        return new Label
        {
            Text = text,
            FontOverride = font,
            FontColorOverride = color,
            HorizontalExpand = expand,
            VerticalAlignment = VAlignment.Center,
            Margin = margin ?? default,
        };
    }

    private static PanelContainer Rule(Color color, Thickness margin)
    {
        return new PanelContainer { PanelOverride = Box(color, null, 0), MinHeight = 1, Margin = margin };
    }

    private static StyleBoxFlat Box(Color background, Color? border, float thickness)
    {
        return new StyleBoxFlat
        {
            BackgroundColor = background,
            BorderColor = border ?? Color.Transparent,
            BorderThickness = new Thickness(thickness),
        };
    }

    /// <summary>A stable document number, so the same name always prints the same serial.</summary>
    public static string Serial(string name)
    {
        // FNV-1a, since string.GetHashCode changes between runs.
        var hash = 2166136261u;
        foreach (var c in name.Trim().ToUpperInvariant())
        {
            hash = (hash ^ c) * 16777619u;
        }
        return Loc.GetString("wf-examine-card-serial", ("serial", $"{hash >> 16:X4}-{hash & 0xFFFF:X4}"));
    }
}

/// <summary>One wounded body part on the card: its name and an icon per finding.</summary>
public sealed class ExamineCardWound(string part, Color accent, string summary)
{
    public readonly string Part = part;
    public readonly Color Accent = accent;

    /// <summary>The part's findings as a sentence, shown on hover.</summary>
    public readonly string Summary = summary;

    public readonly List<(Texture Icon, Color Colour)> Findings = new();
}

/// <summary>A small paper button that flips between showing and hiding the detailed look.</summary>
public sealed class ExamineCardToggle : PanelContainer
{
    private readonly Label _label;
    private bool _pressed;

    public event Action<bool>? Toggled;

    public ExamineCardToggle(Font font, bool pressed)
    {
        _pressed = pressed;
        MouseFilter = MouseFilterMode.Stop;
        DefaultCursorShape = CursorShape.Hand;
        _label = new Label { FontOverride = font, FontColorOverride = ExamineCardControl.Ink, Margin = new Thickness(6, 0) };
        AddChild(_label);
        Refresh(false);
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);
        if (args.Function != EngineKeyFunctions.UIClick)
            return;

        Toggle();
        args.Handle();
    }

    public void Toggle()
    {
        _pressed = !_pressed;
        Refresh(true);
        Toggled?.Invoke(_pressed);
    }

    protected override void MouseEntered()
    {
        base.MouseEntered();
        Refresh(true);
    }

    protected override void MouseExited()
    {
        base.MouseExited();
        Refresh(false);
    }

    private void Refresh(bool hovered)
    {
        _label.Text = Loc.GetString(_pressed ? "wf-examine-card-details-hide" : "wf-examine-card-details-show");
        PanelOverride = new StyleBoxFlat
        {
            BackgroundColor = _pressed || hovered ? ExamineCardControl.Ink : ExamineCardControl.PaperShade.WithAlpha(ExamineCardControl.FillAlpha),
            BorderColor = ExamineCardControl.Ink,
            BorderThickness = new Thickness(1),
        };
        _label.FontColorOverride = _pressed || hovered ? ExamineCardControl.Paper : ExamineCardControl.Ink;
    }
}
