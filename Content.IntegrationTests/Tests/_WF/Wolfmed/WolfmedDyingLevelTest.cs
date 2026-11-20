using Content.Client._WF.Wolfmed.Overlays;
using Content.Shared._WF.Wolfmed.Consciousness;
using Content.Shared.Mobs;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>The dying effects ramp in from half way to crit, carry on through crit, and stop at death.</summary>
[TestFixture]
[TestOf(typeof(WolfmedDyingEffectsSystem))]
public sealed class WolfmedDyingLevelTest
{
    [Test]
    public void LevelFollowsTheWayToDeath()
    {
        const float crit = 100f;
        const float dead = 200f;

        Assert.Multiple(() =>
        {
            Assert.That(WolfmedDyingEffectsSystem.Level(MobState.Alive, 0f, crit, dead), Is.Zero);
            Assert.That(WolfmedDyingEffectsSystem.Level(MobState.Alive, 50f, crit, dead), Is.Zero);
            Assert.That(WolfmedDyingEffectsSystem.Level(MobState.Alive, 75f, crit, dead),
                Is.EqualTo(WolfmedDyingEffectsSystem.CritLevel / 2f).Within(0.001f));
            Assert.That(WolfmedDyingEffectsSystem.Level(MobState.Critical, 100f, crit, dead),
                Is.EqualTo(WolfmedDyingEffectsSystem.CritLevel).Within(0.001f));
            Assert.That(WolfmedDyingEffectsSystem.Level(MobState.Critical, 200f, crit, dead), Is.EqualTo(1f).Within(0.001f));
            Assert.That(WolfmedDyingEffectsSystem.Level(MobState.Dead, 300f, crit, dead), Is.Zero);
        });
    }

    /// <summary>
    /// M2 (plan §5.2): an unconscious wound host's view deepens with its route toward arrest, not with a pressure past
    /// 1 (which never happened). Blood from 35% to 30%, oxygenation from 0.45 to 0.15, whichever is further along.
    /// </summary>
    [Test]
    public void DepthFollowsTheRoute()
    {
        float Depth(float blood, float oxygenation) =>
            WolfmedDyingDepth.Unconscious(WolfmedDyingDepth.RouteProgress(blood, 0.35f, 0.30f, oxygenation, 0.45f, 0.15f));

        Assert.Multiple(() =>
        {
            Assert.That(Depth(0.34f + 0.01f, 1f), Is.EqualTo(WolfmedDyingDepth.UnconsciousStart).Within(0.001f),
                "at the Unconscious line the view is only as deep as Unconscious starts.");
            Assert.That(Depth(0.325f, 1f), Is.EqualTo(0.55f + 0.45f * 0.5f).Within(0.001f), "halfway down the blood route.");
            Assert.That(Depth(0.30f, 1f), Is.EqualTo(1f).Within(0.001f), "at the blood arrest line the view is at its deepest.");
            Assert.That(Depth(1f, 0.30f), Is.EqualTo(0.55f + 0.45f * 0.5f).Within(0.001f), "halfway down the oxygen route.");
            Assert.That(Depth(0.34f, 0.2f), Is.GreaterThan(Depth(0.34f, 0.4f)), "the further route does not set the depth.");
            Assert.That(Depth(1f, 1f), Is.EqualTo(WolfmedDyingDepth.UnconsciousStart).Within(0.001f),
                "a pain faint or a shutdown with nothing running out is not dying.");
        });
    }
}
