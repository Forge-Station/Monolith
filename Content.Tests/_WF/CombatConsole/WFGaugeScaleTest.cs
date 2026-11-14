using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using Content.Client._WF.CombatConsole;
using NUnit.Framework;

namespace Content.Tests._WF.CombatConsole;

/// <summary>Checks signed scales, overrange readings and unavailable telemetry.</summary>
[TestFixture]
public sealed class WFGaugeScaleTest
{
    /// <summary>Reads and writes numbers the same way on every machine.</summary>
    [SetUp]
    public void UseInvariantCulture() => CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

    [Test]
    public void SignedMotionCrossesTheCentreAndClampsAtTheStops()
    {
        Assert.That(WFGaugeScale.Fraction(-100, -100, 100), Is.EqualTo(0));
        Assert.That(WFGaugeScale.Fraction(0, -100, 100), Is.EqualTo(0.5f));
        Assert.That(WFGaugeScale.Fraction(50, -100, 100), Is.EqualTo(0.75f));
        Assert.That(WFGaugeScale.Fraction(200, -100, 100), Is.EqualTo(1));
        Assert.That(WFGaugeScale.Fraction(-200, -100, 100), Is.EqualTo(0));
    }

    [Test]
    public void MissingAndInvalidDataNeverLooksLikeARealZero()
    {
        foreach (var value in new double?[] { null, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            Assert.That(WFGaugeScale.Fraction(value, 0, 100), Is.Null);
        Assert.That(WFGaugeScale.Fraction(0, 0, 100), Is.Zero);
        Assert.That(WFGaugeScale.Fraction(3, 5, 5), Is.Null);
        Assert.That(WFGaugeScale.Fraction(3, 5, -5), Is.Null);
        Assert.That(WFGaugeScale.Fraction(3, 0, double.NaN), Is.Null);
        Assert.That(WFGaugeScale.Fraction(0, -double.MaxValue, double.MaxValue), Is.Null);
    }

    [Test]
    public void FittedFontScalesStayBoundedAndNeverGrowPastTheirAllowance()
    {
        var scales = new HashSet<float>();
        for (var index = 1; index <= 20000; index++)
        {
            var allowance = index / 1000f;
            var scale = WFGaugeScale.FontScale(allowance);
            Assert.That(scale, Is.InRange(0f, MathF.Min(4, allowance)));
            scales.Add(scale);
        }
        Assert.That(scales.Count, Is.LessThanOrEqualTo(33), "Resizing must not grow an unbounded set of font atlases.");
        foreach (var invalid in new[] { -1f, 0f, float.NaN, float.NegativeInfinity, float.PositiveInfinity })
            Assert.That(WFGaugeScale.FontScale(invalid), Is.Zero);
    }

    [TestCase(28)]
    [TestCase(40)]
    [TestCase(60)]
    [TestCase(100)]
    [TestCase(160)]
    public void EndpointLabelsRemainInsideTheLensAndSeparate(float radius)
    {
        var minimum = WFGaugeScale.RoundLabelBounds(radius, false);
        var maximum = WFGaugeScale.RoundLabelBounds(radius, true);
        Assert.That(minimum.Right, Is.LessThan(maximum.Left));
        Assert.That(minimum.Top, Is.EqualTo(maximum.Top));
        foreach (var bounds in new[] { minimum, maximum })
        {
            Assert.That(bounds.Width, Is.GreaterThan(0));
            Assert.That(bounds.Height, Is.GreaterThan(0));
            foreach (var corner in new[] { bounds.TopLeft, bounds.TopRight, bounds.BottomLeft, bounds.BottomRight })
                Assert.That(Vector2.Distance(Vector2.Zero, corner), Is.LessThan(radius * 0.98f),
                    "The whole endpoint number must leave a clear gap before the curved rim.");
        }
    }

    [TestCase(0, 30, 30)]
    [TestCase(31, 30, 50)]
    [TestCase(101, 30, 200)]
    [TestCase(250, 30, 500)]
    [TestCase(500, 30, 500)]
    [TestCase(501, 30, 1000)]
    public void CountScalesRemainLabelledAndContainTheWholeQuantity(double value, double floor, double expected) =>
        Assert.That(WFGaugeScale.Ceiling(value, floor), Is.EqualTo(expected));

    [Test]
    public void ReadingsKeepFixedDecimalsAndNeverShowNegativeZero()
    {
        Assert.That(WFGaugeScale.Fixed(12.34, 1), Is.EqualTo("12.3"));
        Assert.That(WFGaugeScale.Fixed(-1.26, 1), Is.EqualTo("-1.3"));
        Assert.That(WFGaugeScale.Fixed(-0.04, 1), Is.EqualTo("0.0"));
        Assert.That(WFGaugeScale.Fixed(-0.004, 2), Is.EqualTo("0.00"));
        Assert.That(WFGaugeScale.Fixed(7, 0), Is.EqualTo("7"));
    }

    [Test]
    public void DigitsKeepTheirWidthAndDecimalPointAcrossTheWholeScale()
    {
        foreach (var value in new[] { -100d, -12.34, -0.04, 0, 3.5, 99.94, 100 })
        {
            var signed = WFGaugeScale.Digits(value, -100, 100, 1);
            Assert.That(signed, Has.Length.EqualTo(6), signed);
            Assert.That(signed.IndexOf('.'), Is.EqualTo(4), signed);
        }
        foreach (var value in new[] { 0d, 0.04, 5, 12.3, 99.94, 100 })
        {
            var unsigned = WFGaugeScale.Digits(value, 0, 100, 1);
            Assert.That(unsigned, Has.Length.EqualTo(5), unsigned);
            Assert.That(unsigned.IndexOf('.'), Is.EqualTo(3), unsigned);
        }
        Assert.That(WFGaugeScale.Digits(7, 0, 30, 0), Is.EqualTo(" 7"));
        Assert.That(WFGaugeScale.Digits(1234.5, 0, 100, 1), Is.EqualTo("1234.5"), "A reading past the scale is never cut short.");
    }

    [TestCase(88, 100, 1)]
    [TestCase(96, 160, 1)]
    [TestCase(120, 142, 1)]
    [TestCase(120, 142, 2)]
    [TestCase(200, 600, 1.5f)]
    public void RoundDialKeepsTheCaptionClearBeneathTheDial(float width, float height, float scale)
    {
        var layout = WFGaugeScale.RoundDial(width, height, scale);
        var bottom = layout.Center.Y + layout.Radius;
        Assert.That(layout.Radius, Is.GreaterThan(0));
        Assert.That(layout.Center.X - layout.Radius, Is.GreaterThanOrEqualTo(0));
        Assert.That(layout.Center.X + layout.Radius, Is.LessThanOrEqualTo(width));
        Assert.That(layout.Center.Y - layout.Radius, Is.GreaterThanOrEqualTo(0));
        Assert.That(layout.CaptionBaseline - bottom, Is.EqualTo(WFGaugeScale.CaptionDrop * scale).Within(0.001f),
            "The caption sits beneath the dial with a clear gap, scaled with the UI.");
        Assert.That(WFGaugeScale.CaptionDrop, Is.GreaterThanOrEqualTo(15), "The gap leaves room above the letters, past the dial's shadow.");
        var below = height - (layout.CaptionBaseline + WFGaugeScale.CaptionMargin * scale);
        Assert.That(below, Is.GreaterThanOrEqualTo(-0.001f), "The caption must stay inside the control.");
        Assert.That(layout.Center.Y - layout.Radius - WFGaugeScale.DialTop * scale, Is.EqualTo(below).Within(0.001f),
            "The dial and caption form one block, centred in the control.");
    }

    [TestCase(88, 100, 38.5f)]
    [TestCase(96, 110, 43.5f)]
    [TestCase(120, 142, 57)]
    public void ShortDialsKeepMostOfTheirFace(float width, float height, float radius)
    {
        var layout = WFGaugeScale.RoundDial(width, height, 1);
        Assert.That(layout.Radius, Is.EqualTo(radius).Within(0.001f), "The readout sits inside the dial, so nothing but the caption takes height.");
    }

    [Test]
    public void StretchingADialTallerThanItsFaceMovesTheCaptionWithTheDial()
    {
        var snug = WFGaugeScale.RoundDial(96, 140, 1);
        var tall = WFGaugeScale.RoundDial(96, 400, 1);
        Assert.That(tall.Radius, Is.EqualTo(snug.Radius));
        Assert.That(tall.CaptionBaseline - tall.Center.Y, Is.EqualTo(snug.CaptionBaseline - snug.Center.Y).Within(0.001f),
            "The caption must stay the same distance from the dial however tall the control is.");
        Assert.That(tall.Center.Y, Is.GreaterThan(snug.Center.Y));
    }

    [TestCase(30, 1)]
    [TestCase(40, 1)]
    [TestCase(57, 1)]
    [TestCase(80, 2)]
    public void RingReadoutStaysInsideTheRingAndAboveItsOpening(float radius, float scale)
    {
        var centre = new Vector2(100, 100);
        var (figure, unit) = WFGaugeScale.RingReadout(centre, radius, scale);
        Assert.That(figure.Bottom, Is.LessThan(unit.Top));
        Assert.That(unit.Bottom, Is.LessThan(centre.Y + radius * 0.7f), "The unit must clear the scale labels at the ring's opening.");
        foreach (var box in new[] { figure, unit })
        foreach (var corner in new[] { box.TopLeft, box.TopRight, box.BottomLeft, box.BottomRight })
            Assert.That(Vector2.Distance(centre, corner), Is.LessThan(radius - 9 * scale),
                "The readout must stay inside the ring's inner edge.");
    }

    [TestCase(190, 106, 1)]
    [TestCase(190, 176, 1)]
    [TestCase(300, 170, 1)]
    [TestCase(150, 400, 2)]
    public void HeadingTextStaysBeneathTheCompass(float width, float height, float scale)
    {
        var (panel, text) = WFGaugeScale.HeadingPanel(width, height, scale);
        Assert.That(panel.Width, Is.EqualTo(panel.Height).Within(0.001f));
        Assert.That(panel.Left, Is.GreaterThanOrEqualTo(0));
        Assert.That(panel.Right, Is.LessThanOrEqualTo(width + 0.001f));
        Assert.That(panel.Top, Is.GreaterThanOrEqualTo(0));
        Assert.That(text.Top - panel.Bottom, Is.EqualTo(2 * scale).Within(0.001f), "The text sits against the compass.");
        Assert.That(text.Bottom, Is.LessThanOrEqualTo(height + 0.001f));
        Assert.That(height - text.Bottom, Is.EqualTo(panel.Top + 2 * scale).Within(0.001f), "The compass and text are centred as one block.");
    }

    [TestCase(64, 64, 0)]
    [TestCase(100, 64, 18)]
    [TestCase(30, 64, 0)]
    public void LinearInstrumentsCentreTheirContentInATallerControl(float height, float content, float expected) =>
        Assert.That(WFGaugeScale.StripOffset(height, content), Is.EqualTo(expected));
}
