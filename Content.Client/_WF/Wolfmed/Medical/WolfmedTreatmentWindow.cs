using System.Numerics;
using Content.Client.UserInterface.Controls;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Guidebook;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client._WF.Wolfmed.Medical;

/// <summary>What the procedure window is open for. Stable across scan updates, so the window keeps its place.</summary>
public readonly record struct WolfmedTreatmentSubject(
    TargetBodyPart? Part,
    string Wound,
    string Condition,
    bool Mechanical);

/// <summary>
/// UI4: the "what do I do about this" window. One row per procedure step, each with the sprite of the item
/// or reagent it is done with, greyed and ticked once the analyzer says that step has been done. One
/// instance per analyzer panel, refilled on each click.
/// </summary>
/// <remarks>
/// <para>
/// Built in code rather than XAML: a <see cref="FancyWindow"/> subclass with generated name references
/// cannot use the names the base window already owns.
/// </para>
/// <para>
/// Rows are built once per subject and only mutated afterwards. The analyzer repopulates its panel on every
/// scan tick, and rebuilding here would throw away the scroll position while the medic is reading.
/// </para>
/// </remarks>
public sealed class WolfmedTreatmentWindow : FancyWindow
{
    private const string TreatmentGuide = "WFWoundTreatment";
    private const float StepIconSize = 32f;
    private const float MarkIconSize = 14f;

    /// <summary>Done rows keep their shape but drop back out of the reading order.</summary>
    private static readonly Color DoneTint = new(0.45f, 0.47f, 0.5f);

    private static readonly Color RowBackground = Color.FromHex("#141419");
    private static readonly Color CurrentBorder = Color.FromHex("#ffcf6b");
    private static readonly Color Warning = Color.FromHex("#e06c5a");
    private static readonly Color Resolved = Color.FromHex("#86b23c");

    private readonly IPrototypeManager _prototypes;
    private readonly SpriteSystem _sprites;
    private readonly WolfmedAnalyzerIcons _icons;

    private readonly RichTextLabel _summary;
    private readonly Label _resolved;
    private readonly BoxContainer _steps;
    private readonly BoxContainer _avoid;
    private readonly List<StepRow> _rows = new();

    /// <summary>The finding this window is showing, so a repopulate can re-evaluate it in place.</summary>
    public WolfmedTreatmentSubject Subject { get; private set; }

    public WolfmedTreatmentWindow(IPrototypeManager prototypes, SpriteSystem sprites, WolfmedAnalyzerIcons icons)
    {
        // FancyWindow declares [Dependency] fields but never injects them; without this Help() dereferences null.
        IoCManager.InjectDependencies(this);

        _prototypes = prototypes;
        _sprites = sprites;
        _icons = icons;

        MinSize = new Vector2(360, 260);
        SetSize = new Vector2(440, 420);
        Resizable = true;
        HelpGuidebookIds = new List<ProtoId<GuideEntryPrototype>> { new(TreatmentGuide) }; // not [..]: sandbox rejects CollectionsMarshal

        var root = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
            Margin = new Thickness(6, 4, 6, 6),
        };

        _summary = new RichTextLabel { HorizontalExpand = true, Margin = new Thickness(0, 0, 0, 3) };
        root.AddChild(_summary);

        _resolved = new Label
        {
            FontColorOverride = Resolved,
            Visible = false,
            Margin = new Thickness(0, 0, 0, 3),
        };
        root.AddChild(_resolved);
        root.AddChild(new PanelContainer { StyleClasses = { "LowDivider" }, Margin = new Thickness(0, 0, 0, 4) });

        var scrolled = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
        };
        _steps = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
        };
        _avoid = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            Margin = new Thickness(0, 5, 0, 0),
        };
        scrolled.AddChild(_steps);
        scrolled.AddChild(_avoid);

        var scroll = new ScrollContainer
        {
            VerticalExpand = true,
            HorizontalExpand = true,
            HScrollEnabled = false,
        };
        scroll.AddChild(scrolled);
        root.AddChild(scroll);

        var guidebook = new Button
        {
            Text = Loc.GetString("wolfmed-treatment-guidebook-button"),
            HorizontalAlignment = HAlignment.Right,
            Margin = new Thickness(0, 6, 0, 0),
        };
        guidebook.OnPressed += _ => Help();
        root.AddChild(guidebook);

        // Into the contents container, not the window: a direct child is laid over the header and its title.
        ContentsContainer.AddChild(root);
    }

    /// <summary>Opens or refills the window for one finding. Rebuilds the rows only when the subject changes.</summary>
    public void Show(
        WolfmedTreatmentSubject subject,
        string title,
        string summary,
        WolfmedTreatmentProcedurePrototype procedure,
        in WolfmedProcedureState state)
    {
        if (Subject != subject || _rows.Count != procedure.Steps.Count)
        {
            Subject = subject;
            Build(procedure);
        }

        Title = title;
        _summary.SetMessage(FormattedMessage.FromMarkupPermissive(summary));
        SetState(state);

        if (!IsOpen)
            OpenCentered();
        else
            MoveToFront();
    }

    /// <summary>
    /// Re-evaluates every row against a fresh scan. Called on each panel repopulate, so it must not add or
    /// remove controls, take focus, or move the scroll.
    /// </summary>
    public void SetState(in WolfmedProcedureState state)
    {
        var current = true;
        foreach (var row in _rows)
        {
            var done = !state.SubjectPresent || WolfmedStepChecks.IsDone(row.Checks, state);
            var isCurrent = current && !done;
            if (!done)
                current = false;

            row.Content.Modulate = done ? DoneTint : Color.White;
            row.Tick.Visible = done;
            row.Style.BorderColor = isCurrent ? CurrentBorder : RowBackground;
        }

        _resolved.Visible = !state.SubjectPresent;
        if (!state.SubjectPresent)
            _resolved.Text = Loc.GetString("wolfmed-treatment-resolved");
    }

    private void Build(WolfmedTreatmentProcedurePrototype procedure)
    {
        _rows.Clear();
        _steps.RemoveAllChildren();
        _avoid.RemoveAllChildren();

        var number = 0;
        foreach (var step in procedure.Steps)
        {
            number++;
            var row = BuildRow(number, step);
            _rows.Add(row);
            _steps.AddChild(row.Panel);
        }

        foreach (var line in procedure.Avoid)
            _avoid.AddChild(BuildAvoidRow(line));
    }

    private StepRow BuildRow(int number, WolfmedTreatmentStep step)
    {
        var style = new StyleBoxFlat
        {
            BackgroundColor = RowBackground,
            BorderColor = RowBackground,
            BorderThickness = new Thickness(2),
        };
        var panel = new PanelContainer
        {
            PanelOverride = style,
            HorizontalExpand = true,
            Margin = new Thickness(0, 0, 0, 3),
        };

        var content = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 6,
            HorizontalExpand = true,
            Margin = new Thickness(5, 4, 5, 4),
        };

        content.AddChild(new Label
        {
            Text = number.ToString(),
            StyleClasses = { "LabelSubText" },
            MinWidth = 12,
            VerticalAlignment = VAlignment.Center,
        });
        content.AddChild(StepIcon(step));

        var text = new RichTextLabel { HorizontalExpand = true, VerticalAlignment = VAlignment.Center };
        text.SetMessage(FormattedMessage.FromMarkupPermissive(Loc.GetString(step.Text)));
        content.AddChild(text);

        var tick = new TextureRect
        {
            Texture = _icons.Get("done"),
            SetSize = new Vector2(MarkIconSize, MarkIconSize),
            Stretch = TextureRect.StretchMode.KeepAspectCentered,
            Modulate = Resolved,
            VerticalAlignment = VAlignment.Center,
            ToolTip = Loc.GetString("wolfmed-treatment-step-done"),
            Visible = false,
        };
        content.AddChild(tick);

        panel.AddChild(content);
        return new StepRow(panel, style, content, tick, step.Done);
    }

    /// <summary>The step's own item sprite where it has one, else the glyph for the kind of step it is.</summary>
    private Control StepIcon(WolfmedTreatmentStep step)
    {
        Texture texture;
        var tint = Color.White;
        var tooltip = string.Empty;

        if (step.Tool is { } tool && _prototypes.TryIndex<EntityPrototype>(tool, out var item))
        {
            texture = _sprites.GetPrototypeIcon(item).Default;
            tooltip = item.Name;
        }
        else if (step.Reagent is { } reagent && _prototypes.TryIndex<ReagentPrototype>(reagent, out var substance))
        {
            texture = _icons.Get("reagent");
            tint = substance.SubstanceColor;
            tooltip = substance.LocalizedName;
        }
        else if (step.Surgery)
        {
            texture = _icons.Get("surgery");
            tint = WolfmedWoundStyle.Internal;
            tooltip = Loc.GetString("wolfmed-treatment-step-surgery");
        }
        else
        {
            texture = _icons.Get("step");
            tint = WolfmedWoundStyle.StageText;
        }

        return new TextureRect
        {
            Texture = texture,
            SetSize = new Vector2(StepIconSize, StepIconSize),
            Stretch = TextureRect.StretchMode.KeepAspectCentered,
            Modulate = tint,
            VerticalAlignment = VAlignment.Center,
            ToolTip = string.IsNullOrEmpty(tooltip) ? null : tooltip,
        };
    }

    private Control BuildAvoidRow(LocId line)
    {
        var row = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 6,
            HorizontalExpand = true,
            Margin = new Thickness(5, 0, 5, 3),
        };

        row.AddChild(new TextureRect
        {
            Texture = _icons.Get("warning"),
            SetSize = new Vector2(MarkIconSize, MarkIconSize),
            Stretch = TextureRect.StretchMode.KeepAspectCentered,
            Modulate = Warning,
            VerticalAlignment = VAlignment.Center,
            ToolTip = Loc.GetString("wolfmed-treatment-avoid-heading"),
        });

        var text = new RichTextLabel { HorizontalExpand = true };
        text.SetMessage(FormattedMessage.FromMarkupPermissive(Loc.GetString(line)));
        text.Modulate = Warning;
        row.AddChild(text);
        return row;
    }

    /// <summary>One built step row, kept so a scan update can re-tint it instead of rebuilding the list.</summary>
    private sealed class StepRow
    {
        public readonly PanelContainer Panel;
        public readonly StyleBoxFlat Style;
        public readonly Control Content;
        public readonly TextureRect Tick;
        public readonly List<WolfmedStepCheck> Checks;

        public StepRow(
            PanelContainer panel,
            StyleBoxFlat style,
            Control content,
            TextureRect tick,
            List<WolfmedStepCheck> checks)
        {
            Panel = panel;
            Style = style;
            Content = content;
            Tick = tick;
            Checks = checks;
        }
    }
}
