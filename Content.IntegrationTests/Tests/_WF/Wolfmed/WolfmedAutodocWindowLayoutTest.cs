#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Content.Client._WF.Wolfmed.Autodoc;
using Content.IntegrationTests.Fixtures;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.Wolfmed.Autodoc;
using NUnit.Framework;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared;
using Robust.Shared.Configuration;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// AUTODOC5: the pod window's bottom row has to be on screen at the size the window opens at.
/// </summary>
/// <remarks>
/// The test this replaces asked whether anything hung below the window, which a BoxContainer can never let
/// happen: when it runs out of room it clamps its last children to nothing rather than pushing them past
/// the edge. The controls row was therefore squashed to zero height while every assertion passed. This
/// measures the row itself, at UI scale 1 and at 1.25, where the fonts round up and the column is tighter.
/// </remarks>
[TestFixture]
[TestOf(typeof(AutodocWindow))]
public sealed class WolfmedAutodocWindowLayoutTest : GameTest
{
    [Test]
    [TestCase(1f)]
    [TestCase(1.25f)]
    public async Task ControlsFitTheWindowTest(float uiScale)
    {
        var client = Pair.Client;
        var config = client.ResolveDependency<IConfigurationManager>();
        var original = config.GetCVar(CVars.DisplayUIScale);
        AutodocWindow window = default!;

        await client.WaitPost(() =>
        {
            config.SetCVar(CVars.DisplayUIScale, uiScale);
            window = new AutodocWindow();
            window.Update(new AutodocBuiState());
            window.OpenCentered();
        });
        await Pair.RunTicksSync(10);

        await client.WaitAssertion(() =>
        {
            var contents = window.Contents;
            var bottom = contents.GlobalPosition.Y + contents.Size.Y;
            var report = Tree(window);

            var controls = window.ControlsRow;
            Assert.Multiple(() =>
            {
                Assert.That(controls.Size.Y, Is.GreaterThanOrEqualTo(controls.DesiredSize.Y - 0.5f),
                    $"the controls row was squeezed from {controls.DesiredSize.Y} to {controls.Size.Y}.\n{report}");

                foreach (var control in controls.Children)
                {
                    if (!control.Visible)
                        continue;

                    Assert.That(control.Size.Y, Is.GreaterThanOrEqualTo(control.DesiredSize.Y - 0.5f),
                        $"{Name(control)} was squeezed to {control.Size.Y}.\n{report}");
                    Assert.That(control.GlobalPosition.Y + control.Size.Y, Is.LessThanOrEqualTo(bottom + 0.5f),
                        $"{Name(control)} ends below the window's contents.\n{report}");
                }
            });

            // Nothing else may hang out of the window either, at any depth.
            Assert.Multiple(() => Walk(contents, child =>
            {
                if (child.Visible)
                    Assert.That(child.GlobalPosition.Y + child.Size.Y, Is.LessThanOrEqualTo(bottom + 0.5f),
                        $"{Name(child)} ends below the window's contents.\n{report}");
            }));
        });

        await client.WaitPost(() =>
        {
            window.Close();
            config.SetCVar(CVars.DisplayUIScale, original);
        });
        await Pair.RunTicksSync(5);
    }

    /// <summary>
    /// The reorder buttons. The window used to work out what could move from the row it was drawing rather
    /// than from the pod's state, so while a procedure ran it left the buttons around it live and every
    /// message they sent was thrown away by the server.
    /// </summary>
    [Test]
    [TestCase(AutodocState.Idle, 0)]
    [TestCase(AutodocState.Step, 1)]
    public async Task QueueButtonsFollowTheServersBoundTest(AutodocState state, int first)
    {
        var client = Pair.Client;
        AutodocWindow window = default!;

        var queue = new List<AutodocQueueEntry>
        {
            new("WFSurgeryMendFracture", TargetBodyPart.LeftLeg, new List<string>()),
            new("WFSurgeryMendFracture", TargetBodyPart.RightLeg, new List<string>()),
            new("WFSurgeryMendFracture", TargetBodyPart.LeftArm, new List<string>()),
        };

        // The window is built and filled but never opened: this measures what DrawQueue decided, and laying
        // a second window out alongside the one the test above opens races the engine's glyph cache.
        await client.WaitPost(() =>
        {
            window = new AutodocWindow();
            window.Update(new AutodocBuiState { State = state, Occupied = true, Queue = queue });
        });

        await client.WaitAssertion(() =>
        {
            Assert.That(AutodocQueueRules.FirstMovable(state), Is.EqualTo(first),
                "the shared rule and the test disagree about what may move.");

            var up = Buttons(window, "^");
            var down = Buttons(window, "v");
            Assert.Multiple(() =>
            {
                Assert.That(up, Has.Count.EqualTo(queue.Count), "a queue row is missing its move buttons.");

                for (var index = 0; index < queue.Count; index++)
                {
                    Assert.That(up[index].Disabled, Is.EqualTo(index <= first),
                        $"row {index}'s ^ does not match the server's bound.");
                    Assert.That(down[index].Disabled, Is.EqualTo(index < first || index >= queue.Count - 1),
                        $"row {index}'s v does not match the server's bound.");
                }
            });
        });

    }

    private static List<Button> Buttons(Control root, string text)
    {
        var found = new List<Button>();
        Walk(root, control =>
        {
            if (control is Button button && button.Text == text)
                found.Add(button);
        });

        return found;
    }

    private static void Walk(Control control, Action<Control> visit)
    {
        visit(control);
        foreach (var child in control.Children)
            Walk(child, visit);
    }

    private static string Name(Control control) =>
        control.Name is { } name ? $"{control.GetType().Name} '{name}'" : control.GetType().Name;

    /// <summary>The whole tree with its measurements, printed only when an assertion has already failed.</summary>
    private static string Tree(Control root)
    {
        var report = new StringBuilder();

        void Dump(Control control, int level)
        {
            if (level > 6)
                return;

            report.AppendLine($"{new string(' ', level * 2)}{Name(control)} scale={control.UIScale} " +
                              $"desired={control.DesiredSize} size={control.Size} pos={control.Position}");
            foreach (var child in control.Children)
                Dump(child, level + 1);
        }

        Dump(root, 0);
        return report.ToString();
    }
}
