using System.Collections.Generic;
using System.Numerics;
using Content.Client.HealthAnalyzer.UI;
using Content.IntegrationTests.Fixtures;
using Robust.Client.UserInterface.Controls;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// The analyzer's doll buttons must sit on the limbs of the doll the window draws. The doll is 32 px art drawn at
/// 3x in a 96x96 view; each expected centre below is the limb's pixel bounds in the status art, times three.
/// Twice this was "fixed" by reasoning alone and was still wrong on screen, so the layout is measured here.
/// </summary>
[TestFixture]
public sealed class WolfmedAnalyzerDollLayoutTest : GameTest
{
    private static readonly Dictionary<string, Vector2> Expected = new()
    {
        { "HeadButton", new Vector2(46.5f, 19.5f) },
        { "ChestButton", new Vector2(46.5f, 40.5f) },
        { "GroinButton", new Vector2(46.5f, 58.5f) },
        { "LeftArmButton", new Vector2(64.5f, 39f) },
        { "LeftHandButton", new Vector2(66f, 54f) },
        { "RightArmButton", new Vector2(28.5f, 39f) },
        { "RightHandButton", new Vector2(27f, 54f) },
        { "LeftLegButton", new Vector2(54f, 69f) },
        { "LeftFootButton", new Vector2(57f, 82.5f) },
        { "RightLegButton", new Vector2(39f, 69f) },
        { "RightFootButton", new Vector2(36f, 82.5f) },
    };

    [Test]
    public async Task DollButtonsSitOnTheLimbsTest()
    {
        var client = Pair.Client;
        HealthAnalyzerWindow window = default!;

        await client.WaitPost(() =>
        {
            window = new HealthAnalyzerWindow();
            window.OpenCentered();
        });
        await Pair.RunTicksSync(10);

        // Hidden controls are not laid out, so the highlights are shown for the measurement.
        await client.WaitPost(() =>
        {
            foreach (var child in window.FindControl<PanelContainer>("PartView").Children)
            {
                if (child is TextureRect)
                    child.Visible = true;
            }
        });
        await Pair.RunTicksSync(5);

        await client.WaitAssertion(() =>
        {
            var origin = window.SpriteView.GlobalPosition;
            Assert.That(window.SpriteView.Size, Is.EqualTo(new Vector2(96, 96)));

            Assert.Multiple(() =>
            {
                foreach (var (name, expected) in Expected)
                {
                    var button = window.FindControl<TextureButton>(name);
                    var centre = button.GlobalPosition + button.Size / 2 - origin;
                    Assert.That(Vector2.Distance(centre, expected), Is.LessThanOrEqualTo(2.5f),
                        $"{name} is centred at {centre}, the limb is at {expected}.");
                }
            });

            // The selection highlights are whole-doll frames: same place, same size as the doll itself.
            var partView = window.FindControl<PanelContainer>("PartView");
            var overlays = 0;
            foreach (var child in partView.Children)
            {
                if (child is not TextureRect overlay)
                    continue;

                overlays++;
                Assert.That(overlay.GlobalPosition, Is.EqualTo(origin), "a highlight frame must start where the doll starts.");
                Assert.That(overlay.Size, Is.EqualTo(new Vector2(96, 96)));
            }

            Assert.That(overlays, Is.EqualTo(Expected.Count));

            window.Close();
        });
    }
}
