using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using Content.Client._WF.Cockpit;
using NUnit.Framework;

namespace Content.Tests._WF.Cockpit;

/// <summary>Checks that the cockpit's position readout keeps fixed decimals and a steady width.</summary>
[TestFixture]
public sealed class WFCockpitCoordinatesTest
{
    /// <summary>Reads and writes numbers the same way on every machine.</summary>
    [SetUp]
    public void UseInvariantCulture() => CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

    [TestCase(0f, 0f, "    0.0", "    0.0")]
    [TestCase(12.34f, -56.78f, "   12.3", "  -56.8")]
    [TestCase(-0.04f, 1234.56f, "    0.0", " 1234.6")]
    [TestCase(-12345.6f, 7f, "-12345.6", "     7.0")]
    public void CoordinatesUseOneDecimalAndAMatchedWidth(float x, float y, string expectedX, string expectedY)
    {
        var (formattedX, formattedY) = WFCockpitCoordinates.Format(new Vector2(x, y));
        Assert.That(formattedX, Is.EqualTo(expectedX));
        Assert.That(formattedY, Is.EqualTo(expectedY));
    }

    [Test]
    public void DigitsDoNotShiftWhileTheShipMoves()
    {
        var widths = new HashSet<int>();
        for (var step = -9999; step <= 9999; step += 37)
        {
            var (x, y) = WFCockpitCoordinates.Format(new Vector2(step / 10f, -step / 3f));
            Assert.That(x.Length, Is.EqualTo(y.Length), "Both coordinates share one field width.");
            Assert.That(x.IndexOf('.'), Is.EqualTo(x.Length - 2), "One decimal place, always.");
            widths.Add(x.Length);
        }
        Assert.That(widths, Is.EquivalentTo(new[] { WFCockpitCoordinates.FieldWidth }),
            "Within the reserved range the text length never changes.");
    }
}
