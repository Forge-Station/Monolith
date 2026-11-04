#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Content.Client._WF.Planets;
using Content.Client._WF.Warmth;
using Content.IntegrationTests.Pair;
using Content.Shared._WF.Warmth;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.Warmth;

/// <summary>The climate panels grow to their longest line instead of clipping it, and show the temperature gauge.</summary>
[TestFixture]
public sealed class ClimateHudLayoutTest
{
    [Test]
    public async Task PlanetPanelFitsItsLongestLines()
    {
        await using var pair = await PoolManager.GetServerClient();
        var client = pair.Client;
        await client.WaitAssertion(() =>
        {
            var panel = new WFPlanetTimepieceHud(client.ResolveDependency<IResourceCache>());
            panel.SetReadout("Asclepiu Prime", "23:59", "Blizzard", "Winter · Outside -273°C");
            panel.SetThermal("Here -273°C", "Overheating", Color.White);
            panel.SetGauge(true, 100, WFWarmthTrend.Cooling, true);
            panel.Measure(new Vector2(1000, 1000));
            panel.Arrange(UIBox2.FromDimensions(Vector2.Zero, panel.DesiredSize));

            Assert.That(panel.GaugeVisible, Is.True);
            Assert.That(panel.Width, Is.GreaterThanOrEqualTo(WFPlanetTimepieceHud.PanelWidth));
            Assert.That(panel.Width, Is.LessThanOrEqualTo(WFPlanetTimepieceHud.MaxPanelWidth));

            // Every line has its whole text inside the panel, none clipped to a fixed width.
            foreach (var label in FindLabels(panel))
            {
                Assert.That(label.ClipText, Is.False, $"'{label.Text}' is set to clip, so the panel never grows to hold it.");
                Assert.That(label.DesiredSize.X, Is.GreaterThan(0f), $"'{label.Text}' asks for no width.");
                Assert.That(label.Width, Is.GreaterThanOrEqualTo(label.DesiredSize.X - 0.5f), $"'{label.Text}' is clipped.");
                Assert.That(label.Height, Is.GreaterThan(0f), $"'{label.Text}' has no height.");
            }

            // The panel's own height holds every row, gauge and near-warmth line included.
            var rows = 0f;
            foreach (var child in panel.Children)
            {
                rows += child.DesiredSize.Y;
            }

            Assert.That(panel.Height, Is.GreaterThanOrEqualTo(WFPlanetTimepieceHud.PanelHeight));
            Assert.That(panel.Height, Is.GreaterThanOrEqualTo(rows));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CompactPanelFitsItsLongestLines()
    {
        await using var pair = await PoolManager.GetServerClient();
        var client = pair.Client;
        await client.WaitAssertion(() =>
        {
            var panel = new WFWarmthHud(client.ResolveDependency<IResourceCache>());
            panel.SetReadout("Here -273°C and falling fast", "Overheating", Color.White);
            panel.SetGauge(0, WFWarmthTrend.Warming, true);
            panel.Measure(new Vector2(1000, 1000));
            panel.Arrange(UIBox2.FromDimensions(Vector2.Zero, panel.DesiredSize));

            Assert.That(panel.Width, Is.LessThanOrEqualTo(WFWarmthHud.MaxPanelWidth));
            foreach (var label in FindLabels(panel))
            {
                Assert.That(label.ClipText, Is.False, $"'{label.Text}' is set to clip, so the panel never grows to hold it.");
                Assert.That(label.Width, Is.GreaterThanOrEqualTo(label.DesiredSize.X - 0.5f), $"'{label.Text}' is clipped.");
            }
        });
        await pair.CleanReturnAsync();
    }

    private static IEnumerable<Label> FindLabels(Robust.Client.UserInterface.Control control)
    {
        foreach (var child in control.Children)
        {
            if (child is Label label)
                yield return label;

            foreach (var nested in FindLabels(child))
            {
                yield return nested;
            }
        }
    }
}
