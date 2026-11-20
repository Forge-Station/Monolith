using System.Linq;
using System.Numerics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.CombatConsole;

/// <summary>Lays a battery's weapon buttons out as one list, two to a line, sorted by name.</summary>
public sealed class WFWeaponGrid : Control
{
    /// <summary>Weapons side by side on each line.</summary>
    public const int Columns = 2;
    /// <summary>Space between neighbouring entries.</summary>
    public const float Gap = 2;

    private readonly Dictionary<Button, (int Key, string Name)> _known = new();
    private readonly HashSet<Button> _unavailable = new();

    /// <summary>Height of one entry, taken from the tallest.</summary>
    public float EntryHeight { get; private set; }
    /// <summary>Height of the whole list at its current contents.</summary>
    public float ContentHeight { get; private set; }

    /// <summary>Raised when a measure changes the height of the list or of an entry, for hosts that size themselves to it.</summary>
    public event Action? ContentChanged;

    /// <summary>Records a weapon's tie-break key and name, and whether it belongs in the list; flare launchers do not.</summary>
    public void SetWeapon(Button button, NetEntity weapon, bool available)
    {
        var now = (weapon.Id, button.Text ?? string.Empty);
        var changed = !_known.TryGetValue(button, out var before) || before != now;
        _known[button] = now;
        changed |= available ? _unavailable.Remove(button) : _unavailable.Add(button);
        button.Visible = available;
        if (changed)
            InvalidateMeasure();
    }

    /// <summary>The listed weapon buttons in display order: by name without regard to case, then by entity id.</summary>
    public List<Button> Entries() => Children.OfType<Button>()
        .Where(button => !_unavailable.Contains(button))
        .OrderBy(button => button.Text ?? string.Empty, StringComparer.OrdinalIgnoreCase)
        .ThenBy(button => _known.TryGetValue(button, out var known) ? known.Key : 0)
        .ToList();

    protected override void ChildRemoved(Control child)
    {
        base.ChildRemoved(child);
        if (child is not Button button)
            return;
        _known.Remove(button);
        _unavailable.Remove(button);
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        var entries = Entries();
        var cell = new Vector2(CellWidth(availableSize.X), float.PositiveInfinity);
        var (content, entry) = (ContentHeight, EntryHeight);
        EntryHeight = 0;
        foreach (var button in entries)
        {
            button.Measure(cell);
            EntryHeight = Math.Max(EntryHeight, button.DesiredSize.Y);
        }
        var lines = (entries.Count + Columns - 1) / Columns;
        ContentHeight = lines == 0 ? 0 : lines * EntryHeight + (lines - 1) * Gap;
        if (content != ContentHeight || entry != EntryHeight)
            ContentChanged?.Invoke();
        return new Vector2(0, ContentHeight);
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        var first = CellWidth(finalSize.X);
        var entries = Entries();
        for (var i = 0; i < entries.Count; i++)
        {
            var x = i % Columns == 0 ? 0 : first + Gap;
            var width = i % Columns == 0 ? first : Math.Max(0, finalSize.X - x);
            var y = i / Columns * (EntryHeight + Gap);
            entries[i].Arrange(UIBox2.FromDimensions(new Vector2(x, y), new Vector2(width, EntryHeight)));
        }
        return finalSize;
    }

    private static float CellWidth(float width) =>
        float.IsFinite(width) ? MathF.Floor(Math.Max(0, width - Gap * (Columns - 1)) / Columns) : 0;
}
