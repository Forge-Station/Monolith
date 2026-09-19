using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Client._WF.TractorBeam;
using Content.Shared._WF.TractorBeam;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.TractorBeam;

public sealed class TractorBeamConsoleWindowTest
{
    [Test]
    public async Task ReopeningRestoresCaptureAndKeepsCommandsVisibleAndRangeEditable()
    {
        await using var pair = await PoolManager.GetServerClient();
        await pair.Client.WaitAssertion(() =>
        {
            using var window = new TractorBeamConsoleWindow();
            window.UpdateState(State());
            var pin = Find<Button>(window, "LockInPlace");
            var range = Find<Button>(window, "SetRange");
            var desired = Find<FloatSpinBox>(window, "DesiredRange");
            var radar = Tree(window).OfType<TractorBeamRadarControl>().Single();
            Assert.Multiple(() =>
            {
                Assert.That(radar.Selected, Is.EqualTo(new NetEntity(2)), "Reopening must select the existing capture.");
                Assert.That(pin.Disabled, Is.False);
                Assert.That(range.Disabled, Is.False);
                Assert.That(desired.Value, Is.EqualTo(55f), "Suggest inward travel instead of the changing upper bound.");
            });

            window.UpdateState(State(distance: 59.99f));
            Assert.That(range.Disabled, Is.False, "Tiny inward drift must not disable the initial suggested range.");
            desired.Value = 40;
            window.UpdateState(State(distance: 59.9f));
            Assert.That(desired.Value, Is.EqualTo(40), "Telemetry must preserve the operator's edit.");

            radar.OnTargetSelected?.Invoke(new NetEntity(3));
            window.UpdateState(State());
            Assert.Multiple(() =>
            {
                Assert.That(radar.Selected, Is.EqualTo(new NetEntity(3)), "A manual selection must survive telemetry.");
                Assert.That(pin.Disabled, Is.True);
                Assert.That(range.Disabled, Is.True);
            });

            // Commands must remain visible in the compact window.
            var size = new Vector2(840, 480);
            window.SetSize = size;
            window.Measure(size);
            window.Arrange(UIBox2.FromDimensions(Vector2.Zero, size));
            foreach (var action in new Control[] { pin, range, desired, Find<Button>(window, "Release") })
            {
                Assert.That(action.Size.Y, Is.GreaterThan(0));
                Assert.That(action.GlobalPosition.Y, Is.GreaterThanOrEqualTo(window.GlobalPosition.Y));
                Assert.That(action.GlobalPosition.Y + action.Size.Y,
                    Is.LessThanOrEqualTo(window.GlobalPosition.Y + size.Y), $"{action.Name} must remain visible.");
                for (var parent = action.Parent; parent != null; parent = parent.Parent)
                    Assert.That(parent, Is.Not.InstanceOf<ScrollContainer>(), "Commands must not scroll out with telemetry.");
            }
        });
        await pair.CleanReturnAsync();
    }

    [TestCase(TractorBeamPinStatus.StopSource, "tractor-beam-console-pin-stop-source")]
    [TestCase(TractorBeamPinStatus.StopTarget, "tractor-beam-console-pin-stop-target")]
    [TestCase(TractorBeamPinStatus.InsufficientPower, "tractor-beam-console-pin-power")]
    public async Task DisabledPinKeepsTheServerReasonInItsTooltip(TractorBeamPinStatus status, string reason)
    {
        await using var pair = await PoolManager.GetServerClient();
        await pair.Client.WaitAssertion(() =>
        {
            using var window = new TractorBeamConsoleWindow();
            window.UpdateState(State(status: status));
            Assert.That(Find<Button>(window, "LockInPlace").Disabled, Is.True);
            Assert.That(Find<Button>(window, "LockInPlace").ToolTip, Is.EqualTo(Loc.GetString(reason)));
        });
        await pair.CleanReturnAsync();
    }

    private static TractorBeamConsoleBoundUserInterfaceState State(float distance = 60,
        TractorBeamPinStatus status = TractorBeamPinStatus.Ready)
    {
        var emitter = new TractorBeamEmitterEntry(new NetEntity(1), "Test dish", new NetEntity(2), true,
            0.2f, 500000, 500000, 200, canLockInPlace: status == TractorBeamPinStatus.Ready,
            currentDistance: distance, holdDistance: 60, minimumDistance: 10, active: true, pinStatus: status);
        return new TractorBeamConsoleBoundUserInterfaceState(true, 200, new[] { emitter }, new[]
        {
            new TractorBeamTargetEntry(new NetEntity(2), "Captured vessel", new Vector2(0, distance), 10000),
            new TractorBeamTargetEntry(new NetEntity(3), "Other vessel", new Vector2(5, 80), 10000),
        });
    }

    private static T Find<T>(Control root, string name) where T : Control =>
        Tree(root).OfType<T>().Single(control => control.Name == name);

    private static IEnumerable<Control> Tree(Control root)
    {
        yield return root;
        foreach (var child in root.Children)
        foreach (var descendant in Tree(child))
            yield return descendant;
    }
}
