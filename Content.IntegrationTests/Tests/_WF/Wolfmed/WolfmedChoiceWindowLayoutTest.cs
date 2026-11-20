#nullable enable
using System.Numerics;
using System.Threading.Tasks;
using Content.Client._WF.Wolfmed.Life;
using Content.IntegrationTests.Fixtures;
using Content.Shared._WF.Wolfmed.Life;
using NUnit.Framework;
using Robust.Client.UserInterface;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.IoC;
using Robust.Shared.Localization;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// Playtest 1: the "Let go?" dialog opened half off the screen with a tall empty body. It opens centred once its
/// text is in, wraps the text, is as tall as its contents and keeps both buttons inside, at UI scale 1 and 1.25.
/// </summary>
[TestFixture]
[TestOf(typeof(WolfmedChoiceWindow))]
public sealed class WolfmedChoiceWindowLayoutTest : GameTest
{
    [Test]
    [TestCase(1f)]
    [TestCase(1.25f)]
    public async Task ChoiceWindowFitsItsTextTest(float uiScale)
    {
        var client = Pair.Client;
        var config = client.ResolveDependency<IConfigurationManager>();
        var original = config.GetCVar(CVars.DisplayUIScale);
        WolfmedChoiceEui eui = default!;

        await client.WaitPost(() => config.SetCVar(CVars.DisplayUIScale, uiScale));
        await Pair.RunTicksSync(5);

        await client.WaitPost(() =>
        {
            var loc = IoCManager.Resolve<ILocalizationManager>();
            // The EUI's own order: opened first, the text a moment later.
            eui = new WolfmedChoiceEui();
            eui.Opened();
            eui.HandleState(new WolfmedChoiceEuiState(
                loc.GetString("wolfmed-succumb-dialog-title"),
                loc.GetString("wolfmed-succumb-dialog-text", ("cause", "blood"), ("minutes", 10)),
                loc.GetString("wolfmed-succumb-dialog-accept"),
                loc.GetString("wolfmed-succumb-dialog-deny")));
        });
        await Pair.RunTicksSync(10);

        await client.WaitAssertion(() =>
        {
            var window = eui.Window;
            Assert.That(window.IsOpen, Is.True, "the dialog never opened.");

            var screen = window.Parent!.Size;
            var contents = window.Contents;
            var bottom = contents.GlobalPosition.Y + contents.Size.Y;
            var text = window.Text;
            var row = window.ButtonRow;
            var report = $"screen {screen}, window at {window.Position} size {window.Size} desired {window.DesiredSize}; " +
                         $"text {text.Size}, buttons at {row.GlobalPosition} size {row.Size} desired {row.DesiredSize}";

            Assert.Multiple(() =>
            {
                Assert.That(window.Position.X, Is.GreaterThanOrEqualTo(0f), report);
                Assert.That(window.Position.Y, Is.GreaterThanOrEqualTo(0f), report);
                Assert.That(window.Position.X + window.Size.X, Is.LessThanOrEqualTo(screen.X + 0.5f), report);
                Assert.That(window.Position.Y + window.Size.Y, Is.LessThanOrEqualTo(screen.Y + 0.5f), report);

                // Centred.
                var centre = window.Position + window.Size / 2f;
                Assert.That(Vector2.Distance(centre, screen / 2f), Is.LessThan(2f), $"the dialog is not centred. {report}");

                // Wrapped, and no taller than its words and buttons.
                Assert.That(text.Size.X, Is.LessThanOrEqualTo(WolfmedChoiceWindow.TextWidth + 0.5f), report);
                Assert.That(text.Size.Y, Is.GreaterThan(text.DesiredSize.Y - 0.5f).And.LessThan(text.DesiredSize.Y + 0.5f),
                    $"the text is padded or squeezed. {report}");
                Assert.That(window.Size.Y, Is.LessThanOrEqualTo(window.DesiredSize.Y + 0.5f), $"the window is taller than its contents. {report}");

                // Both buttons inside, full height.
                // One unit of slack: at 1.25 the window lands on the pixel grid a fraction under its desired height.
                Assert.That(row.Size.Y, Is.GreaterThanOrEqualTo(row.DesiredSize.Y - 1f), $"the buttons were squeezed. {report}");
                Assert.That(row.GlobalPosition.Y + row.Size.Y, Is.LessThanOrEqualTo(bottom + 0.5f), $"the buttons hang below the window. {report}");
                Assert.That(bottom - (row.GlobalPosition.Y + row.Size.Y), Is.LessThan(16f), $"empty space under the buttons. {report}");
            });
        });

        await client.WaitPost(() =>
        {
            eui.Closed();
            config.SetCVar(CVars.DisplayUIScale, original);
        });
        await Pair.RunTicksSync(5);
    }
}
