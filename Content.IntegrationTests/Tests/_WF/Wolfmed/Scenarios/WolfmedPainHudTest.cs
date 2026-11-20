#nullable enable
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._WF.Wolfmed.Consciousness;
using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared._WF.Wolfmed.Consciousness;
using Content.Shared.Alert;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.FixedPoint;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;

namespace Content.IntegrationTests.Tests._WF.Wolfmed.Scenarios;

/// <summary>
/// Playtest 4, VISUALS: the pain HUD. Its severity is round(effective body pain / soft cap × 7), it hides at or under
/// wolfmed.pain_hud_from, shows paindd (8) through a pain faint, and a machine gets the same scale under its own alert.
/// </summary>
[TestFixture]
[TestOf(typeof(WolfmedPainAlertSystem))]
public sealed class WolfmedPainHudTest : GameTest
{
    private async Task Pin()
    {
        await OverrideCVar(Side.Server, WolfmedCVars.Consciousness, true);
        await OverrideCVar(Side.Server, WolfmedCVars.ConsciousnessPainDown, 0.95f);
        await OverrideCVar(Side.Server, WolfmedCVars.ConsciousnessPainOut, 1.4f);
        await OverrideCVar(Side.Server, WolfmedCVars.PainFaintSeconds, 20f);
        await OverrideCVar(Side.Server, WolfmedCVars.PainFaintCooldown, 50f);
        await OverrideCVar(Side.Server, WolfmedCVars.PainHudFrom, 5f);
    }

    /// <summary>Pain on one part, with its wound floor at the same value so it does not recover away.</summary>
    private void SetPain(EntityUid body, BodyPartType type, float value)
    {
        var part = SEntMan.System<SharedBodySystem>().GetBodyChildren(body).First(p => p.Component.PartType == type).Id;
        var pain = SEntMan.GetComponent<PainComponent>(part);
        pain.WoundPain = FixedPoint2.New(value);
        SEntMan.System<PainSystem>().SetPain((part, pain), FixedPoint2.New(value));
    }

    private AlertState? PainAlert(EntityUid body) =>
        SEntMan.System<AlertsSystem>().TryGetAlertState(body, AlertKey.ForCategory(WolfmedPainAlertSystem.Category),
            out var state)
            ? state
            : null;

    [Test]
    public async Task PainHudStepsThroughTheScaleTest()
    {
        await Pin();
        var map = await Pair.CreateTestMap();
        var s = new WolfmedScenario(SEntMan);
        EntityUid a = default, b = default, machine = default;
        await Server.WaitPost(() =>
        {
            s.SetAir(map.MapUid, true);
            a = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            b = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            machine = SEntMan.SpawnEntity("MobIPC", map.GridCoords);
        });
        await RunSeconds(2);

        await Server.WaitAssertion(() =>
        {
            Assert.That(PainAlert(a), Is.Null, "an unhurt body shows the pain HUD.");

            // Under the line: nothing. Just over it: the bottom of the scale.
            SetPain(a, BodyPartType.Torso, 4);
            Assert.That(PainAlert(a), Is.Null, "4 pain is under wolfmed.pain_hud_from.");
            SetPain(a, BodyPartType.Torso, 6);
            Assert.That(PainAlert(a)?.Severity, Is.EqualTo((short) 0), "6 of 135 is severity 0.");
            Assert.That(PainAlert(a)?.Type.Id, Is.EqualTo(WolfmedPainAlertSystem.Alert.Id));

            // 58 / 135 × 7 = 3.01.
            SetPain(a, BodyPartType.Torso, 58);
            Assert.That(PainAlert(a)?.Severity, Is.EqualTo((short) 3), "58 of 135 is severity 3.");

            // The cap Downs the body, and the HUD shows that as the flashing paindowned (playtest 4), not plain 7.
            SetPain(a, BodyPartType.Torso, 135);
            Assert.That(PainAlert(a)?.Severity, Is.EqualTo(WolfmedPainAlertSystem.DownedSeverity), "Downed by pain is the flashing severity.");
            Assert.That(SEntMan.GetComponent<WolfmedConsciousnessComponent>(a).State,
                Is.EqualTo(WolfmedConsciousness.Downed));

            SetPain(a, BodyPartType.Torso, 0);
            Assert.That(PainAlert(a), Is.Null, "no pain, no HUD.");

            // Summed pain past the faint line: paindd for as long as the faint lasts.
            SetPain(b, BodyPartType.Torso, 100);
            SetPain(b, BodyPartType.Head, 100);
            var consciousness = SEntMan.GetComponent<WolfmedConsciousnessComponent>(b);
            Assert.That(consciousness.Cause, Is.EqualTo(WolfmedCause.PainFaint), "200 summed pain did not faint.");
            Assert.That(PainAlert(b)?.Severity, Is.EqualTo(WolfmedPainAlertSystem.FaintSeverity),
                "a fainted body does not show paindd.");

            // A machine reads the same scale as sensor overload.
            SetPain(machine, BodyPartType.Torso, 58);
            Assert.That(PainAlert(machine)?.Type.Id, Is.EqualTo(WolfmedPainAlertSystem.MechanicalAlert.Id),
                "a machine shows the flesh alert.");
            Assert.That(PainAlert(machine)?.Severity, Is.EqualTo((short) 3));

            // The hover text follows the severity AlertControl passes it.
            Assert.That(Loc.GetString("alerts-wolfmed-pain-desc", ("severity", 3)), Is.EqualTo("Strong pain."));
            Assert.That(Loc.GetString("alerts-wolfmed-pain-desc", ("severity", (int) WolfmedPainAlertSystem.DownedSeverity)), Does.StartWith("Floored"));
            Assert.That(Loc.GetString("alerts-wolfmed-pain-desc", ("severity", (int) WolfmedPainAlertSystem.FaintSeverity)), Does.StartWith("Passed out"));
        });
    }
}
