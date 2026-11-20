#nullable enable

using System.Collections.Generic;
using System.Linq;
using Content.Client._WF.CombatConsole;
using Content.Shared._Mono.FireControl;
using Robust.Shared.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.IntegrationTests.Tests._WF.CombatConsole;

/// <summary>Checks that a weapon bank is one sorted, two-column list that scrolls and never pages.</summary>
internal static class WFWeaponListChecks
{
    private const float Tolerance = 0.6f;

    /// <summary>The order the list must show: name without regard to case, then entity id.</summary>
    public static List<Button> Expected(IEnumerable<FireControllableEntry> entries, IReadOnlyDictionary<NetEntity, Button> buttons,
        ISet<NetEntity>? excluded = null) =>
        entries.Where(entry => excluded == null || !excluded.Contains(entry.NetEntity))
            .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.NetEntity.Id)
            .Select(entry => buttons[entry.NetEntity])
            .ToList();

    /// <summary>The control tree below <paramref name="root"/>, root first.</summary>
    public static IEnumerable<Control> Tree(Control root)
    {
        yield return root;
        foreach (var child in root.Children)
        foreach (var nested in Tree(child))
            yield return nested;
    }

    /// <summary>Asserts there is no pager and no paging state, only weapon buttons in the list.</summary>
    public static void NoPaging(Control root, string context)
    {
        var pager = new[] { "WfWeaponPager", "WfWeaponPage", "WfWeaponPrevious", "WfWeaponNext" };
        foreach (var control in Tree(root))
            Assert.That(control.Name != null && pager.Contains(control.Name), Is.False, $"{context}: {control.Name} is a page control.");
        var grid = Tree(root).OfType<WFWeaponGrid>().Single();
        Assert.That(grid.Children.All(child => child is Button), Is.True, $"{context}: the list holds only weapon buttons.");
        foreach (var member in new[] { "PageIndex", "PageCount", "ItemsPerPage", "SetPage" })
            Assert.That(typeof(WFWeaponGrid).GetMember(member), Is.Empty, $"{context}: {member} is paging state.");
    }

    /// <summary>Finds the one scrolling region, which must hold the weapon list and nothing else scrolls.</summary>
    public static (ScrollContainer Scroll, WFWeaponGrid Grid) OneList(Control root, string context)
    {
        var tree = Tree(root).ToArray();
        var grid = tree.OfType<WFWeaponGrid>().Single();
        var scroll = tree.OfType<ScrollContainer>().Single();
        Assert.That(scroll.Name, Is.EqualTo("WfWeaponScroll"), $"{context}: the weapon list is the only scrolling region.");
        Assert.That(grid.Parent, Is.SameAs(scroll), context);
        Assert.That(scroll.HScrollEnabled, Is.False, $"{context}: the list scrolls vertically only.");
        return (scroll, grid);
    }

    /// <summary>Whether the list is taller than its window onto it.</summary>
    public static bool Overflows(ScrollContainer scroll, WFWeaponGrid grid) => grid.ContentHeight > scroll.Height + Tolerance;

    /// <summary>
    /// Asserts the listed order, two columns filled left to right then down, equal cells and no overlap or clipping
    /// inside the list.
    /// </summary>
    public static void Layout(WFWeaponGrid grid, IReadOnlyList<Button> expected, string context)
    {
        Assert.That(grid.Entries(), Is.EqualTo(expected), $"{context}: alphabetical order, ties by entity id.");
        Assert.That(grid.Children.OfType<Button>().Where(button => button.Visible), Is.EquivalentTo(expected),
            $"{context}: every listed weapon is shown and nothing else is.");
        if (expected.Count == 0)
            return;
        Assert.That(expected.Select(button => button.Position.X).Distinct().Count(), Is.EqualTo(Math.Min(2, expected.Count)),
            $"{context}: two columns.");
        Assert.That(expected[0].Position, Is.EqualTo(System.Numerics.Vector2.Zero), context);
        for (var i = 0; i < expected.Count; i++)
        {
            var button = expected[i];
            var line = i / WFWeaponGrid.Columns;
            Assert.That(button.Height, Is.EqualTo(grid.EntryHeight).Within(Tolerance), $"{context}: entry {i} height.");
            Assert.That(button.Position.Y, Is.EqualTo(line * (grid.EntryHeight + WFWeaponGrid.Gap)).Within(Tolerance),
                $"{context}: entry {i} sits on line {line}.");
            if (i % WFWeaponGrid.Columns == 1)
            {
                var left = expected[i - 1];
                Assert.That(button.Position.Y, Is.EqualTo(left.Position.Y), $"{context}: entries {i - 1} and {i} share a line.");
                Assert.That(button.Position.X, Is.GreaterThanOrEqualTo(left.Position.X + left.Width + WFWeaponGrid.Gap - Tolerance), context);
                Assert.That(button.Width, Is.EqualTo(left.Width).Within(1 + Tolerance), $"{context}: columns are equally wide.");
            }
            Assert.That(button.Position.X, Is.GreaterThanOrEqualTo(-Tolerance), context);
            Assert.That(button.Position.Y, Is.GreaterThanOrEqualTo(-Tolerance), context);
            Assert.That(button.Position.X + button.Width, Is.LessThanOrEqualTo(grid.Width + Tolerance), $"{context}: entry {i} is clipped at the side.");
            Assert.That(button.Position.Y + button.Height, Is.LessThanOrEqualTo(grid.Height + Tolerance), $"{context}: entry {i} is clipped at the end.");
        }
        for (var i = 0; i < expected.Count; i++)
        for (var j = i + 1; j < expected.Count; j++)
        {
            var a = expected[i];
            var b = expected[j];
            var overlap = a.Position.X < b.Position.X + b.Width - 0.01f && b.Position.X < a.Position.X + a.Width - 0.01f &&
                          a.Position.Y < b.Position.Y + b.Height - 0.01f && b.Position.Y < a.Position.Y + a.Height - 0.01f;
            Assert.That(overlap, Is.False, $"{context}: entries {i} and {j} overlap.");
        }
    }

    /// <summary>Asserts an entry is no taller than its two text lines plus the ammunition bar, and well under the old 52-unit row.</summary>
    public static void Compact(WFWeaponGrid grid, IReadOnlyList<Button> entries, string context)
    {
        foreach (var button in entries)
        {
            var row = button.Children.OfType<WFWeaponRow>().Single();
            var text = row.NameLabel.DesiredSize.Y + row.SupplyLabel.DesiredSize.Y;
            Assert.That(grid.EntryHeight, Is.LessThanOrEqualTo(text + 4), $"{context}: {button.Text} has more chrome than the text needs.");
            Assert.That(grid.EntryHeight, Is.LessThanOrEqualTo(40), $"{context}: {button.Text} is not compact.");
            Assert.That(grid.EntryHeight, Is.GreaterThan(text), $"{context}: {button.Text} is shorter than its text.");
        }
    }

    /// <summary>Asserts a weapon's name and supply reading fit their labels in both directions.</summary>
    public static void Readable(Button button, IUserInterfaceManager ui, string context)
    {
        var row = button.Children.OfType<WFWeaponRow>().Single();
        foreach (var label in new[] { row.NameLabel, row.SupplyLabel })
        {
            var font = FontOf(label, ui);
            var width = 0f;
            foreach (var rune in label.Text!.EnumerateRunes())
                width += font.GetCharMetrics(rune, label.UIScale)?.Advance ?? 0;
            Assert.That(label.PixelWidth + 1, Is.GreaterThanOrEqualTo(width), $"{context}: {label.Text} must fit across.");
            Assert.That(label.PixelHeight + 1, Is.GreaterThanOrEqualTo(font.GetHeight(label.UIScale)), $"{context}: {label.Text} must fit down.");
        }
    }

    /// <summary>The part of a weapon's name its label shows before the text is clipped.</summary>
    public static string VisiblePrefix(Button button, IUserInterfaceManager ui)
    {
        var label = button.Children.OfType<WFWeaponRow>().Single().NameLabel;
        var font = FontOf(label, ui);
        var shown = new System.Text.StringBuilder();
        var width = 0f;
        foreach (var rune in label.Text!.EnumerateRunes())
        {
            width += font.GetCharMetrics(rune, label.UIScale)?.Advance ?? 0;
            if (width > label.PixelWidth)
                break;
            shown.Append(rune.ToString());
        }
        return shown.ToString();
    }

    private static Font FontOf(Label label, IUserInterfaceManager ui) =>
        label.FontOverride ?? (label.TryGetStyleProperty<Font>(Label.StylePropertyFont, out var styled) ? styled : ui.ThemeDefaults.LabelFont);

    /// <summary>
    /// Scrolls to each weapon in turn and asserts it lands wholly inside the window onto the list, then puts the
    /// list back where it was. <paramref name="layout"/> must run a full layout of the tree.
    /// </summary>
    public static void Reachable(ScrollContainer scroll, IReadOnlyList<Button> expected, Action layout, string context)
    {
        var original = scroll.VScroll;
        try
        {
            foreach (var button in expected)
            {
                scroll.VScroll = button.Position.Y;
                layout();
                var view = scroll.GlobalPosition;
                Assert.That(button.GlobalPosition.Y, Is.GreaterThanOrEqualTo(view.Y - Tolerance), $"{context}: {button.Text} is above the list window.");
                Assert.That(button.GlobalPosition.Y + button.Height, Is.LessThanOrEqualTo(view.Y + scroll.Height + Tolerance),
                    $"{context}: {button.Text} is below the list window.");
                Assert.That(button.GlobalPosition.X, Is.GreaterThanOrEqualTo(view.X - Tolerance), $"{context}: {button.Text} is left of the list window.");
                Assert.That(button.GlobalPosition.X + button.Width, Is.LessThanOrEqualTo(view.X + scroll.Width + Tolerance),
                    $"{context}: {button.Text} is right of the list window.");
            }
        }
        finally
        {
            scroll.VScroll = original;
            layout();
        }
    }
}
