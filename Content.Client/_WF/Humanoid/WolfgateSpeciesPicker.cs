using System.Linq;
using System.Numerics;
using Content.Client._WF.Stylesheets;
using Content.Client.Lobby;
using Content.Shared.Guidebook;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Preferences;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;

namespace Content.Client._WF.Humanoid;

/// <summary>
/// Species browser: a grid of cards, each with a full-body preview of a default character of that species,
/// its name, a short summary and a link to its guidebook page. Species that vary another species are grouped
/// on their own translucent panel under the species they vary.
/// </summary>
public sealed class WolfgateSpeciesPicker : BoxContainer
{
    [Dependency] private IEntityManager _entManager = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;

    /// <summary>Raised with the species id when a card is clicked.</summary>
    public Action<string>? OnSpeciesSelected;

    /// <summary>Raised with the species id when a card's info button is clicked.</summary>
    public Action<string>? OnSpeciesInfoRequested;

    private readonly LobbyUIController _controller;
    private readonly ButtonGroup _group = new();
    private readonly Dictionary<string, WolfgateSpeciesCard> _cards = new();
    private readonly List<EntityUid> _dummies = new();
    private readonly List<GridContainer> _grids = new();

    private string? _selected;

    public WolfgateSpeciesPicker()
    {
        IoCManager.InjectDependencies(this);
        _controller = UserInterfaceManager.GetUIController<LobbyUIController>();
        Orientation = LayoutOrientation.Vertical;
        SeparationOverride = 10;
    }

    protected override void EnteredTree()
    {
        base.EnteredTree();
        if (_cards.Count == 0)
            Populate();
    }

    protected override void ExitedTree()
    {
        base.ExitedTree();
        Clear();
    }

    private void Clear()
    {
        foreach (var dummy in _dummies)
            _entManager.DeleteEntity(dummy);

        _dummies.Clear();
        _cards.Clear();
        _grids.Clear();
        DisposeAllChildren();
    }

    /// <summary>Marks a card as chosen without raising <see cref="OnSpeciesSelected"/>.</summary>
    public void SetSelected(string species)
    {
        _selected = species;
        if (_cards.TryGetValue(species, out var card))
            card.Pressed = true; // the group unpresses the others
    }

    /// <summary>
    /// Rebuilds every card, spawning one preview dummy per species. Not free, so it runs when the species
    /// list changes or the tab re-enters the tree, not on every selection.
    /// </summary>
    public void Populate()
    {
        Clear();

        var all = _prototypeManager.EnumeratePrototypes<SpeciesPrototype>().Where(s => s.RoundStart).ToList();

        // Species that vary another one get their own panel under its name; everything else shares a panel,
        // because 25 panels of one card each reads worse than one panel of 25.
        var families = all.Where(s => s.SubspeciesOf != null)
            .GroupBy(s => s.SubspeciesOf!.Value.Id)
            .OrderBy(g => GroupName(g.Key, g))
            .ToList();

        var parented = families.Select(g => g.Key).ToHashSet();

        // A species that has variants heads its own panel rather than sitting in the general list.
        var standalone = all
            .Where(s => s.SubspeciesOf == null && !parented.Contains(s.ID))
            .OrderBy(DisplayName)
            .ToList();
        if (standalone.Count > 0)
            AddChild(Group(null, standalone));

        foreach (var family in families)
        {
            var members = family.OrderBy(DisplayName).ToList();

            // The parent first, when it is playable itself.
            if (_prototypeManager.TryIndex<SpeciesPrototype>(family.Key, out var parent) && parent.RoundStart)
                members.Insert(0, parent);

            AddChild(Group(GroupName(family.Key, family), members));
        }

        if (_selected != null)
            SetSelected(_selected);
    }

    /// <summary>A translucent panel holding an optional heading and a grid of cards.</summary>
    private Control Group(string? heading, List<SpeciesPrototype> members)
    {
        var content = new BoxContainer { Orientation = LayoutOrientation.Vertical, SeparationOverride = 6 };

        if (heading != null)
        {
            content.AddChild(new Label
            {
                Text = heading,
                StyleClasses = { StyleWolfgate.StyleClassCreatorHeading },
            });
        }

        var grid = new GridContainer { Columns = Columns(), HSeparationOverride = 6, VSeparationOverride = 6 };
        foreach (var species in members)
            grid.AddChild(AddCard(species));

        _grids.Add(grid);
        content.AddChild(grid);

        return new PanelContainer
        {
            StyleClasses = { StyleWolfgate.StyleClassCreatorGroup },
            HorizontalExpand = true,
            Children = { content },
        };
    }

    /// <summary>Name a family panel is headed by: the species it varies, playable here or not.</summary>
    private string GroupName(string id, IEnumerable<SpeciesPrototype> members)
    {
        return _prototypeManager.TryIndex<SpeciesPrototype>(id, out var proto)
            ? DisplayName(proto)
            : DisplayName(members.First());
    }

    private string DisplayName(SpeciesPrototype species) => Loc.GetString(species.Name);

    private WolfgateSpeciesCard AddCard(SpeciesPrototype species)
    {
        var card = new WolfgateSpeciesCard(
            species.ID,
            DisplayName(species),
            Summary(species),
            Preview(species),
            _prototypeManager.HasIndex<GuideEntryPrototype>(species.ID))
        {
            Group = _group,
        };

        card.OnPressed += _ =>
        {
            _selected = species.ID;
            OnSpeciesSelected?.Invoke(species.ID);
        };
        card.OnInfoRequested += () => OnSpeciesInfoRequested?.Invoke(species.ID);

        _cards[species.ID] = card;
        return card;
    }

    /// <summary>
    /// Full-body sprite of a default character of this species: species skin tone, no hair or facial hair,
    /// no clothing, and only the markings the species gives itself.
    /// </summary>
    private Control Preview(SpeciesPrototype species)
    {
        var view = new SpriteView
        {
            // Fit scales the sprite down to the box using its rotated bounding box, so the drawn sprite is
            // about 0.7 of the box. Scale only has to be large enough for that clamp to bite.
            Scale = new Vector2(3, 3),
            OverrideDirection = Direction.South,
            SetSize = WolfgateSpeciesCard.PreviewBox,
            Stretch = SpriteView.StretchMode.Fit,
            VerticalAlignment = VAlignment.Center,
        };

        var profile = HumanoidCharacterProfile.DefaultWithSpecies(species.ID)
            .WithCharacterAppearance(HumanoidCharacterAppearance.DefaultWithSpecies(species.ID));

        // Species that do not offer the default sex would otherwise load a base sprite set they have no art for.
        if (species.Sexes.Count > 0 && !species.Sexes.Contains(profile.Sex))
            profile = profile.WithSex(species.Sexes[0]);

        var dummy = _controller.LoadProfileEntity(profile, null, false);
        _dummies.Add(dummy);
        view.SetEntity(dummy);
        return view;
    }

    private static string Summary(SpeciesPrototype species)
    {
        var key = $"species-summary-{species.ID.ToLowerInvariant()}";
        return Loc.TryGetString(key, out var summary) ? summary : string.Empty;
    }

    /// <summary>Cards per row, shared by every panel so they line up across groups.</summary>
    private int Columns()
    {
        const float gap = 6f;
        const float panelPadding = 24f; // the group panel's content margin, both sides, plus slack
        return Math.Max(1, (int) ((Width - panelPadding + gap) / (WolfgateSpeciesCard.CardWidth + gap)));
    }

    protected override void Resized()
    {
        base.Resized();
        var columns = Columns();
        foreach (var grid in _grids)
        {
            // Guarded: assigning Columns invalidates measure, so an unguarded write relayouts every frame.
            if (grid.Columns != columns)
                grid.Columns = columns;
        }
    }
}
