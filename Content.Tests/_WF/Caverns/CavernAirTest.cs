using System;
using Content.Shared._WF.Caverns;
using Content.Shared.Atmos;
using NUnit.Framework;

namespace Content.Tests._WF.Caverns;

/// <summary>The shaft air readings: each world's level classifies as section 4.8 says, and every threshold is exact.</summary>
[TestFixture]
[TestOf(typeof(WFCavernAirClassifier))]
public sealed class CavernAirTest
{
    /// <summary>The cavern levels' map atmosphere volume.</summary>
    private const float Volume = 2500f;

    /// <summary>Section 4.8: each level's atmosphere from levels.yml and the reading its shafts give.</summary>
    // CavernMouthTest.GateExists checks the shades on the real maps report the same readings.
    private static readonly (string World, float Temperature, float[] Moles, WFCavernAir Air)[] Worlds =
    {
        ("Asclepiu", 285.15f, new[] { 21.824879f, 82.10312f }, WFCavernAir.Breathable),
        ("Fervidus", 413.15f, new[] { 0f, 60f, 40f }, WFCavernAir.Scalding),
        ("Merak", 294.15f, new[] { 21.824879f, 82.10312f }, WFCavernAir.Breathable),
        ("Aerumna", 277.15f, new[] { 4f, 72f, 24f }, WFCavernAir.Toxic),
        ("Thrascias", 235.15f, new[] { 0f, 100f }, WFCavernAir.Freezing),
        ("Carcinoma", 310.15f, new[] { 21.824879f, 76f, 0f, 0f, 0f, 0f, 1f }, WFCavernAir.Foul),
    };

    /// <summary>Plain station air at room temperature: breathable, and the base every edge case starts from.</summary>
    private static readonly float[] StationAir = { 21.824879f, 82.10312f };

    private const float Room = 293.15f;

    /// <summary>
    /// The six levels read as section 4.8 says. Each threshold is exact at its edge: the smallest amount that reaches it
    /// counts, one float step less does not, and the checks run in the design's order.
    /// </summary>
    [Test]
    public void ClassifiesEachWorld()
    {
        Assert.Multiple(() =>
        {
            foreach (var (world, temperature, moles, air) in Worlds)
            {
                Assert.That(WFCavernAirClassifier.Classify(Mix(temperature, moles)), Is.EqualTo(air), $"{world} reads wrong.");
            }

            Assert.That(Classify(WFCavernAirClassifier.ScaldingAbove), Is.EqualTo(WFCavernAir.Breathable), "330 K is not yet scalding.");
            Assert.That(Classify(MathF.BitIncrement(WFCavernAirClassifier.ScaldingAbove)), Is.EqualTo(WFCavernAir.Scalding),
                "Just over 330 K is scalding.");
            Assert.That(Classify(WFCavernAirClassifier.FreezingBelow), Is.EqualTo(WFCavernAir.Breathable), "260 K is not yet freezing.");
            Assert.That(Classify(MathF.BitDecrement(WFCavernAirClassifier.FreezingBelow)), Is.EqualTo(WFCavernAir.Freezing),
                "Just under 260 K is freezing.");

            var carbon = EdgeMoles(Gas.CarbonDioxide, WFCavernAirClassifier.ToxicCarbonDioxide);
            Assert.That(WFCavernAirClassifier.Classify(With(Gas.CarbonDioxide, carbon)), Is.EqualTo(WFCavernAir.Toxic),
                "5 kPa of carbon dioxide is toxic.");
            Assert.That(WFCavernAirClassifier.Classify(With(Gas.CarbonDioxide, MathF.BitDecrement(carbon))), Is.EqualTo(WFCavernAir.Breathable),
                "Just under 5 kPa of carbon dioxide is not.");

            Assert.That(WFCavernAirClassifier.Classify(With(Gas.Plasma, float.Epsilon)), Is.EqualTo(WFCavernAir.Toxic), "Any plasma is toxic.");
            Assert.That(WFCavernAirClassifier.Classify(With(Gas.Tritium, float.Epsilon)), Is.EqualTo(WFCavernAir.Toxic), "Any tritium is toxic.");

            var oxygen = EdgeMoles(Gas.Oxygen, WFCavernAirClassifier.ThinOxygen);
            Assert.That(WFCavernAirClassifier.Classify(With(Gas.Oxygen, oxygen)), Is.EqualTo(WFCavernAir.Breathable),
                "16 kPa of oxygen is enough.");
            Assert.That(WFCavernAirClassifier.Classify(With(Gas.Oxygen, MathF.BitDecrement(oxygen))), Is.EqualTo(WFCavernAir.Thin),
                "Just under 16 kPa of oxygen is thin.");

            Assert.That(WFCavernAirClassifier.Classify(With(Gas.Ammonia, float.Epsilon)), Is.EqualTo(WFCavernAir.Foul), "Any ammonia is foul.");
            Assert.That(WFCavernAirClassifier.Classify(With(Gas.NitrousOxide, float.Epsilon)), Is.EqualTo(WFCavernAir.Foul),
                "Any nitrous oxide is foul.");

            // Order: heat and cold before poison, poison before thin air, thin air before stink.
            var hotPoison = With(Gas.Plasma, 1f);
            hotPoison.Temperature = 400f;
            Assert.That(WFCavernAirClassifier.Classify(hotPoison), Is.EqualTo(WFCavernAir.Scalding), "Heat outranks poison.");

            var coldPoison = With(Gas.Plasma, 1f);
            coldPoison.Temperature = 200f;
            Assert.That(WFCavernAirClassifier.Classify(coldPoison), Is.EqualTo(WFCavernAir.Freezing), "Cold outranks poison.");

            var thinPoison = With(Gas.Plasma, 1f);
            thinPoison.SetMoles(Gas.Oxygen, 0f);
            Assert.That(WFCavernAirClassifier.Classify(thinPoison), Is.EqualTo(WFCavernAir.Toxic), "Poison outranks thin air.");

            var thinStink = With(Gas.Ammonia, 1f);
            thinStink.SetMoles(Gas.Oxygen, 0f);
            Assert.That(WFCavernAirClassifier.Classify(thinStink), Is.EqualTo(WFCavernAir.Thin), "Thin air outranks stink.");
        });
    }

    /// <summary>Station air at a given temperature, classified.</summary>
    private static WFCavernAir Classify(float temperature)
    {
        return WFCavernAirClassifier.Classify(Mix(temperature, StationAir));
    }

    /// <summary>Station air at room temperature with one gas set to an amount.</summary>
    private static GasMixture With(Gas gas, float moles)
    {
        var mixture = Mix(Room, StationAir);
        mixture.SetMoles(gas, moles);
        return mixture;
    }

    /// <summary>The fewest moles of a gas, in station air at room temperature, whose partial pressure reaches a threshold.</summary>
    private static float EdgeMoles(Gas gas, float kilopascals)
    {
        var mixture = Mix(Room, StationAir);
        var moles = kilopascals * Volume / (Atmospherics.R * Room);

        mixture.SetMoles(gas, moles);
        while (WFCavernAirClassifier.PartialPressure(mixture, gas) < kilopascals)
        {
            moles = MathF.BitIncrement(moles);
            mixture.SetMoles(gas, moles);
        }

        while (true)
        {
            mixture.SetMoles(gas, MathF.BitDecrement(moles));
            if (WFCavernAirClassifier.PartialPressure(mixture, gas) < kilopascals)
                return moles;

            moles = MathF.BitDecrement(moles);
        }
    }

    /// <summary>A cavern-volume mixture of the given gases, in Gas order.</summary>
    private static GasMixture Mix(float temperature, float[] moles)
    {
        var mixture = new GasMixture(Volume) { Temperature = temperature };

        for (var gas = 0; gas < moles.Length; gas++)
        {
            mixture.SetMoles(gas, moles[gas]);
        }

        return mixture;
    }
}
