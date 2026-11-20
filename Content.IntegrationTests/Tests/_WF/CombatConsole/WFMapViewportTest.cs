using System.Numerics;
using System.Reflection;
using Content.Client.Shuttles.UI;
using Content.Client.UserInterface.Controls;
using Robust.Client.UserInterface;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.CombatConsole;

/// <summary>Checks rectangular map culling against actual plotting and pointer coordinates.</summary>
[TestFixture]
public sealed class WFMapViewportTest
{
    [Test]
    public async Task RectangularExtentsFollowPointerTransformWithoutChangingSensorRange()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        await pair.Client.WaitAssertion(() =>
        {
            var ui = pair.Client.ResolveDependency<IUserInterfaceManager>();
            var inverse = typeof(MapGridControl).GetMethod("InverseMapPosition", BindingFlags.NonPublic | BindingFlags.Instance)!;
            foreach (var map in new MapGridControl[] { new ShuttleNavControl(), new ShuttleDockControl(), new ShuttleMapControl() })
            {
                using (map)
                {
                    ui.StateRoot.AddChild(map);
                    var range = map.WorldRange;
                    var sensor = map.MaxRadarRangeVector;
                    map.WfFitInstrument = true;
                    foreach (var size in new[] { new Vector2(900, 300), new Vector2(300, 900), new Vector2(380, 400) })
                    {
                        map.SetSize = size;
                        map.InvalidateMeasure();
                        map.InvalidateArrange();
                        map.Measure(size);
                        map.Arrange(UIBox2.FromDimensions(Vector2.Zero, size));
                        map.Offset = new Vector2(17, -23);
                        var pixelCenter = new Vector2(map.PixelWidth, map.PixelHeight) / 2;
                        Vector2 Pointer(Vector2 point) => (Vector2) inverse.Invoke(map, new object[] { point })!;
                        Assert.That(Vector2.Distance(Pointer(pixelCenter), map.Offset), Is.LessThan(0.001f));
                        var farCorner = Pointer(new Vector2(map.PixelWidth, map.PixelHeight)) - map.Offset;
                        Assert.That(map.WorldRangeVector.X, Is.EqualTo(farCorner.X).Within(0.001f));
                        Assert.That(map.WorldRangeVector.Y, Is.EqualTo(-farCorner.Y).Within(0.001f));
                        var visibleBounds = new Box2(map.Offset - map.WorldRangeVector, map.Offset + map.WorldRangeVector);
                        var edgePoint = Pointer(size.X > size.Y
                            ? new Vector2(map.PixelWidth * 0.95f, map.PixelHeight * 0.5f)
                            : new Vector2(map.PixelWidth * 0.5f, map.PixelHeight * 0.95f));
                        Assert.That(visibleBounds.Contains(edgePoint), Is.True,
                            "Docks, hulls and FTL exclusions drawn on the long edge must survive viewport culling.");
                        if (size.X != 380)
                            Assert.That(new Box2(map.Offset - new Vector2(range), map.Offset + new Vector2(range)).Contains(edgePoint), Is.False,
                                "This regression must exercise space previously culled by the old square.");
                        Assert.That(map.WorldRange, Is.EqualTo(range), "Resizing the plot must not change zoom intent.");
                        Assert.That(map.MaxRadarRangeVector, Is.EqualTo(sensor), "Viewport geometry must not widen sensor limits.");
                    }
                    map.WfFitInstrument = false;
                    Assert.That(map.WorldRangeVector, Is.EqualTo(new Vector2(range)), "Unmodified square controls retain upstream bounds.");
                    map.Orphan();
                }
            }
        });
        await pair.CleanReturnAsync();
    }
}
