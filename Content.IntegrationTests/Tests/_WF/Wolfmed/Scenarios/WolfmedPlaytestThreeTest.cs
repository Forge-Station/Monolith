#nullable enable
using System;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Content.Client._WF.Wolfmed.Overlays;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server.Gravity;
using Content.Server._WF.Wolfmed.Consciousness;
using Content.Server._WF.Wolfmed.Life;
using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Wolfmed.Body;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared._WF.Wolfmed.Consciousness;
using Content.Shared._WF.Wolfmed.Life;
using Content.Shared.Alert;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Climbing.Components;
using Content.Shared.Climbing.Events;
using Content.Shared.Climbing.Systems;
using Content.Shared.DoAfter;
using Content.Shared.FixedPoint;
using Content.Shared.Gravity;
using Content.Shared.Physics;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Wolfmed.Scenarios;

/// <summary>
/// Playtest 3 (2026-09-24): the explanation card counts down, a Downed body stays off the tables, and the analyzer's
/// vitals block says less and rounds.
/// </summary>
[TestFixture]
public sealed class WolfmedPlaytestThreeTest : GameTest
{
    private const float FaintSeconds = 20f;

    private async Task Pin()
    {
        await OverrideCVar(Side.Server, WolfmedCVars.Consciousness, true);
        await OverrideCVar(Side.Server, WolfmedCVars.ConsciousnessPainDown, 0.95f);
        await OverrideCVar(Side.Server, WolfmedCVars.ConsciousnessPainOut, 1.4f);
        await OverrideCVar(Side.Server, WolfmedCVars.ConsciousnessBloodDown, 0.5f);
        await OverrideCVar(Side.Server, WolfmedCVars.ConsciousnessBloodOut, 0.35f);
        await OverrideCVar(Side.Server, WolfmedCVars.ConsciousnessHysteresis, 0.1f);
        await OverrideCVar(Side.Server, WolfmedCVars.PainFaintSeconds, FaintSeconds);
        await OverrideCVar(Side.Server, WolfmedCVars.PainShockThreshold, 130f);
        await OverrideCVar(Side.Server, WolfmedCVars.ArrestShockBlood, 0f);
        await OverrideCVar(Side.Server, WolfmedCVars.ArrestBlood, 0.30f);
        await OverrideCVar(Side.Server, WolfmedCVars.ArrestOxygenation, 0.15f);
        await OverrideCVar(Side.Server, WolfmedCVars.BrainBloodStart, 0.5f);
        await OverrideCVar(Side.Server, WolfmedCVars.BrainPressureStart, 0.75f);
        await OverrideCVar(Side.Server, WolfmedCVars.BrainPressureOut, 0.45f);
        await OverrideCVar(Side.Server, WolfmedCVars.CardExaminedSeconds, 3f);
    }

    private WolfmedConsciousnessComponent Consc(EntityUid body) => SEntMan.GetComponent<WolfmedConsciousnessComponent>(body);

    private WolfmedCardComponent? Card(EntityUid body) => SEntMan.GetComponentOrNull<WolfmedCardComponent>(body);

    /// <summary>Pain on one part, with its wound floor at the same value so it does not recover away.</summary>
    private void SetPain(EntityUid body, BodyPartType type, float value)
    {
        var part = SEntMan.System<SharedBodySystem>().GetBodyChildren(body).First(p => p.Component.PartType == type).Id;
        var pain = SEntMan.GetComponent<PainComponent>(part);
        pain.WoundPain = FixedPoint2.New(value);
        SEntMan.System<PainSystem>().SetPain((part, pain), FixedPoint2.New(value));
    }

    /// <summary>
    /// <c>CardCountdownTest</c> (item 1): a pain faint sets the card's wake window, the same one the faint alert's
    /// cooldown uses, at the moment it starts; the countdown reads "COMING ROUND IN N S" with N within a second of the
    /// time left, and its bar drains between two reads. A faint something else holds, and an Unconscious-by-blood body,
    /// read ∞ with an empty bar and the help that says what has to change. A Dying body shows the brain bar and no
    /// countdown. The accent colour and the icons come from the cause prototypes.
    /// </summary>
    [Test]
    public async Task CardCountdownTest()
    {
        await Pin();
        var map = await Pair.CreateTestMap();
        var s = new WolfmedScenario(SEntMan);
        var alerts = SEntMan.System<WolfmedConditionAlertSystem>();
        var cards = SEntMan.System<WolfmedCardSystem>();
        EntityUid faint = default, blocked = default, bled = default, arrest = default;
        var first = 0f;
        var firstSeconds = 0;
        var trace = string.Empty;

        await Server.WaitPost(() =>
        {
            s.SetAir(map.MapUid, true);
            faint = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            blocked = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            bled = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            arrest = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
        });
        await RunSeconds(2);

        await Server.WaitAssertion(() =>
        {
            SetPain(faint, BodyPartType.Torso, 100);
            SetPain(faint, BodyPartType.Head, 100);
            s.SetBlood(blocked, 0.40f);
            SetPain(blocked, BodyPartType.Torso, 100);
            SetPain(blocked, BodyPartType.Head, 100);
            s.SetBlood(bled, 0.33f);
            s.SetBlood(arrest, 0.29f);
            s.Advance(arrest, 1);

            // No refresh for the faint: its card has to be there from the moment it went out. The felt states read the
            // networked vitals the life tick writes once a second.
            foreach (var body in new[] { blocked, bled, arrest })
            {
                s.Life.UpdateVitalSigns(body);
                cards.Refresh(body);
            }

            var now = SGameTiming.CurTime;
            var comp = Consc(faint);
            var card = Card(faint);
            Assert.Multiple(() =>
            {
                Assert.That(comp.Cause, Is.EqualTo(WolfmedCause.PainFaint));
                Assert.That(card, Is.Not.Null, "the faint's card waited for the once-a-second pass.");
                Assert.That(card?.WakeStart, Is.EqualTo(comp.PainFaintStart), "the card's window does not start with the faint.");
                Assert.That(card?.WakeEnd, Is.EqualTo(comp.PainFaintUntil), "the card's window does not end with the faint.");
                Assert.That(alerts.GetFaintCountdown(faint), Is.EqualTo((card?.WakeStart ?? default, card?.WakeEnd ?? default)),
                    "the alert and the card count down different windows.");
            });

            var countdown = WolfmedExplanationCard.Countdown(SProtoMan, comp, card, now);
            var left = (card!.WakeEnd!.Value - now).TotalSeconds;
            Assert.That(countdown, Is.Not.Null, "a faint has no countdown row.");
            var match = Regex.Match(countdown!.Value.Text, @"^COMING ROUND IN (\d+) S$");
            Assert.That(match.Success, Is.True, $"the countdown reads '{countdown.Value.Text}'.");
            firstSeconds = int.Parse(match.Groups[1].Value);
            first = countdown.Value.Fraction;
            trace = $"{countdown.Value.Text} ({left:0.00} s left, bar {first:0.000})";
            Assert.Multiple(() =>
            {
                Assert.That(firstSeconds, Is.EqualTo(left).Within(1d), "N is not the time left.");
                Assert.That(countdown.Value.Timed, Is.True);
                Assert.That(first, Is.EqualTo(left / FaintSeconds).Within(0.01f), "the bar is not the remainder over the faint.");
            });

            // Something else holds the faint: no countdown, and the help does not promise coming round.
            var blockedRow = WolfmedExplanationCard.Countdown(SProtoMan, Consc(blocked), Card(blocked), now);
            var bledRow = WolfmedExplanationCard.Countdown(SProtoMan, Consc(bled), Card(bled), now);
            var none = Loc.GetString("wolfmed-card-countdown-none");
            Assert.Multiple(() =>
            {
                Assert.That(Consc(blocked).Blockers, Is.Not.EqualTo(WolfmedCauseFlags.None));
                Assert.That(Card(blocked)?.WakeEnd, Is.Null, "a faint something else holds has a wake time.");
                Assert.That(blockedRow?.Text, Is.EqualTo(none));
                Assert.That(blockedRow?.Fraction, Is.Zero);
                Assert.That(WolfmedExplanationCard.Lines(SProtoMan, Consc(blocked), Card(blocked)),
                    Does.Contain(Loc.GetString("wolfmed-cause-pain-faint-help-blocked")));

                // Unconscious from blood: no timer, ∞, an empty bar, the help that says what has to change.
                Assert.That(Consc(bled).Cause, Is.EqualTo(WolfmedCause.Blood));
                Assert.That(bledRow?.Text, Is.EqualTo("COMING ROUND: ∞"));
                Assert.That(bledRow?.Fraction, Is.Zero);
                Assert.That(bledRow?.Timed, Is.False);
                Assert.That(WolfmedExplanationCard.Lines(SProtoMan, Consc(bled), Card(bled)),
                    Does.Contain(Loc.GetString("wolfmed-cause-blood-help-out")).And.Contain("Blood critically low"));

                // Dying: the brain bar has the row.
                Assert.That(Consc(arrest).Cause, Is.EqualTo(WolfmedCause.Arrest));
                Assert.That(WolfmedExplanationCard.Countdown(SProtoMan, Consc(arrest), Card(arrest), now), Is.Null,
                    "a Dying body shows a countdown as well as the brain bar.");
                Assert.That(WolfmedExplanationCard.Bar(SProtoMan, Consc(arrest), Card(arrest)), Is.Not.Null);
                Assert.That(WolfmedExplanationCard.Bar(SProtoMan, Consc(faint), card), Is.Null);
            });

            // The look: the accent from the cause prototype, the icons the alerts bar shows, no numbers in the rows.
            Assert.Multiple(() =>
            {
                Assert.That(WolfmedExplanationCard.Colour(SProtoMan, comp), Is.EqualTo(Color.FromHex("#e3a33c")));
                Assert.That(WolfmedExplanationCard.Colour(SProtoMan, Consc(arrest)), Is.EqualTo(Color.FromHex("#d0343c")));
                Assert.That(WolfmedExplanationCard.Colour(SProtoMan, Consc(bled)), Is.EqualTo(WolfmedExplanationCard.DefaultColour));
                Assert.That(WolfmedExplanationCard.CauseAlert(SProtoMan, comp)?.Id, Is.EqualTo("WFWolfmedFaintPain"));
                Assert.That(WolfmedExplanationCard.BlockerAlerts(SProtoMan, Consc(blocked)).Select(alert => alert.Id),
                    Does.Contain("WFWolfmedDownedBlood"));
                foreach (var body in new[] { faint, blocked, bled, arrest })
                {
                    Assert.That(WolfmedExplanationCard.Lines(SProtoMan, Consc(body), Card(body)).Any(line => line.Any(char.IsDigit)),
                        Is.False, "a card row shows a number.");
                }
            });
        });

        await RunSeconds(3);
        await Server.WaitAssertion(() =>
        {
            var later = WolfmedExplanationCard.Countdown(SProtoMan, Consc(faint), Card(faint), SGameTiming.CurTime);
            var seconds = int.Parse(Regex.Match(later!.Value.Text, @"(\d+)").Groups[1].Value);
            trace += $"; 3 s later {later.Value.Text} (bar {later.Value.Fraction:0.000})";
            Assert.Multiple(() =>
            {
                Assert.That(later.Value.Fraction, Is.LessThan(first), "the bar did not drain.");
                Assert.That(seconds, Is.LessThan(firstSeconds), "the seconds did not count down.");
            });
        });

        TestContext.Out.WriteLine($"CardCountdownTest: {trace}");
    }

    /// <summary>
    /// The card's look (item 1): it never covers the alerts column, the chat, the action buttons, the targeting doll or
    /// the hotbar, at UI scale 1 and 1.25; and every cause's icon resolves on the client the way the alerts bar draws
    /// it, while an alert that does not exist draws nothing.
    /// </summary>
    [Test]
    public async Task CardLooksTest()
    {
        Assert.Multiple(() =>
        {
            foreach (var (width, height) in new[] { (2560f, 1351f), (1920f, 1080f), (1280f, 720f), (1024f, 768f) })
            {
                foreach (var ui in new[] { 1f, 1.25f })
                {
                    var screen = new UIBox2(0f, 0f, width, height);
                    var scale = WolfmedExplanationCardLayout.Scale(screen, ui);
                    var cardWidth = WolfmedExplanationCardLayout.Width(screen, scale);
                    foreach (var cardHeight in new[] { 150f * scale, 280f * scale, height * 0.4f })
                    {
                        var box = WolfmedExplanationCardLayout.Box(screen, cardWidth, cardHeight);
                        Assert.That(box.Left, Is.GreaterThanOrEqualTo(screen.Left));
                        Assert.That(box.Right, Is.LessThanOrEqualTo(screen.Right));
                        foreach (var (name, area) in WolfmedSyntheticHudLayout.Reserved(screen))
                        {
                            if (name is "central sightline" or "toolbar")
                                continue;

                            Assert.That(WolfmedSyntheticHudLayout.Overlaps(box, area), Is.False,
                                $"the card covers the {name} at {width}x{height}, UI scale {ui}, {cardHeight:0} px tall.");
                        }
                    }
                }
            }
        });

        await Client.WaitAssertion(() =>
        {
            var overlay = new WolfmedExplanationCardOverlay();
            var prototypes = Client.ResolveDependency<IPrototypeManager>();
            Assert.Multiple(() =>
            {
                foreach (var cause in prototypes.EnumeratePrototypes<WolfmedConsciousnessCausePrototype>())
                {
                    if ((cause.AlertOut ?? cause.AlertDowned) is not { } alert)
                        continue;

                    Assert.That(overlay.Icon(alert), Is.Not.Null, $"{cause.ID}'s card icon ({alert.Id}) does not resolve.");
                }

                Assert.That(overlay.Icon(new ProtoId<AlertPrototype>("WolfmedNoSuchAlert")), Is.Null,
                    "an unknown alert drew something.");
                Assert.That(overlay.Icon(null), Is.Null);
            });
        });
    }

    /// <summary>
    /// <c>DownedCannotClimbTest</c> (item 2): a Downed body's own climb onto a table is refused with no do-after; a
    /// standing body's starts; somebody else can still lift the Downed body on. And the route the owner actually took:
    /// lying down takes the tables' layer off a body's masks so it can crawl under them, which put a Downed body on top
    /// of one; a Downed body keeps the layer and stops at the table's edge.
    /// </summary>
    [Test]
    public async Task DownedCannotClimbTest()
    {
        await Pin();
        var map = await Pair.CreateTestMap();
        var s = new WolfmedScenario(SEntMan);
        var climb = SEntMan.System<ClimbSystem>();
        EntityUid table = default, downed = default, standing = default, medic = default;
        EntityUid crawlTable = default, crawler = default;

        EntityCoordinates At(int x, int y) => new(map.Grid, x + 0.5f, y + 0.5f);

        await Server.WaitPost(() =>
        {
            var maps = SEntMan.System<SharedMapSystem>();
            for (var x = -2; x <= 5; x++)
            {
                for (var y = -4; y <= 3; y++)
                    maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), map.Tile.Tile);
            }

            var gravity = SEntMan.EnsureComponent<GravityComponent>(map.Grid);
            SEntMan.System<GravitySystem>().EnableGravity(map.Grid, gravity);
            s.SetAir(map.MapUid, true);
            s.KeepGrid(map.Grid);

            table = SEntMan.SpawnEntity("Table", At(1, 0));
            downed = SEntMan.SpawnEntity("MobHuman", At(0, 0));
            standing = SEntMan.SpawnEntity("MobHuman", At(0, 1));
            medic = SEntMan.SpawnEntity("MobHuman", At(1, 1));
            crawlTable = SEntMan.SpawnEntity("Table", At(2, -3));
            crawler = SEntMan.SpawnEntity("MobHuman", At(0, -3));
        });
        await RunSeconds(2);

        await Server.WaitAssertion(() =>
        {
            s.SetBlood(downed, 0.45f);
            s.SetBlood(crawler, 0.45f);
        });
        await RunSeconds(1);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<WolfmedDownedComponent>(downed), Is.True, "45% blood did not Down the body.");
            Assert.That(SEntMan.HasComponent<WolfmedDownedComponent>(crawler), Is.True);

            // Its own climb: refused, and nothing started.
            Assert.That(climb.TryClimb(downed, downed, table, out var own), Is.False, "a Downed body climbed a table.");
            Assert.That(own, Is.Null);
            Assert.That(SEntMan.GetComponentOrNull<DoAfterComponent>(downed)?.DoAfters.Count ?? 0, Is.Zero,
                "the refused climb started a do-after.");

            // The refusal on its own, past the Downed reach rule that stops the attempt first today.
            var self = new AttemptClimbEvent(downed, downed, table);
            SEntMan.EventBus.RaiseLocalEvent(table, ref self);
            var lifted = new AttemptClimbEvent(medic, downed, table);
            SEntMan.EventBus.RaiseLocalEvent(table, ref lifted);
            Assert.Multiple(() =>
            {
                Assert.That(self.Cancelled, Is.True, "the climb event let a Downed body climb itself.");
                Assert.That(lifted.Cancelled, Is.False, "the climb event stopped a medic lifting a Downed body.");
            });

            // Standing: the climb starts. A second person lifts the Downed body on.
            Assert.That(climb.TryClimb(standing, standing, table, out var up), Is.True, "a standing body could not climb.");
            Assert.That(up, Is.Not.Null);
            Assert.That(climb.TryClimb(medic, downed, table, out var lift), Is.True, "the medic could not lift the Downed body.");
            Assert.That(lift, Is.Not.Null);
        });

        await RunSeconds(3);
        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.GetComponent<ClimbingComponent>(standing).IsClimbing, Is.True,
                    "the standing body's climb did not finish.");
                Assert.That(SEntMan.GetComponent<ClimbingComponent>(downed).IsClimbing, Is.True,
                    "the medic's lift did not put the Downed body on the table.");
            });
        });

        // The crawl route: the Downed body's fixtures still collide with tables, and pushed at one it stops there.
        var tableLayer = 0;
        await Server.WaitAssertion(() =>
        {
            var tableFixtures = SEntMan.GetComponent<FixturesComponent>(crawlTable).Fixtures.Values;
            tableLayer = tableFixtures.Where(fixture => fixture.Hard).Aggregate(0, (layer, fixture) => layer | fixture.CollisionLayer);
            Assert.That(tableLayer & (int) CollisionGroup.TableLayer, Is.Not.Zero, "the table is not on the tables' layer.");
            Assert.That(SEntMan.GetComponent<FixturesComponent>(crawler).Fixtures.Values
                    .Where(fixture => fixture.Hard)
                    .Any(fixture => (fixture.CollisionMask & tableLayer & (int) CollisionGroup.TableLayer) != 0),
                Is.True, "a Downed body's masks let it crawl through tables.");
        });

        var physics = SEntMan.System<SharedPhysicsSystem>();
        var transforms = SEntMan.System<SharedTransformSystem>();
        for (var tick = 0; tick < 60; tick++)
        {
            await Server.WaitPost(() => physics.SetLinearVelocity(crawler, new Vector2(3f, 0f)));
            await RunTicksSync(1);
        }

        float crawlerX = 0f, tableX = 0f;
        await Server.WaitAssertion(() =>
        {
            crawlerX = transforms.GetWorldPosition(crawler).X;
            tableX = transforms.GetWorldPosition(crawlTable).X;
        });

        TestContext.Out.WriteLine($"DownedCannotClimbTest: pushed at the table, the Downed body stopped at x {crawlerX:0.00} " +
                                  $"(table centre {tableX:0.00}, its near edge {tableX - 0.5f:0.00}).");
        Assert.That(crawlerX, Is.LessThan(tableX - 0.5f), "a Downed body crawled onto the table.");
    }

    /// <summary>
    /// <c>VitalsBlockIsCompactTest</c> (item 3): the owner's screenshot state (Downed by blood with pain, laboured
    /// breathing, a weak pulse, 43% and falling, burns weeping 1.3 u/s, lungs impaired) reads as exactly three lines:
    /// the state, one vitals line with each item once, and "Do first" with each aid once. No float leaks its digits.
    /// </summary>
    [Test]
    public async Task VitalsBlockIsCompactTest()
    {
        await Server.WaitAssertion(() =>
        {
            var owner = new WolfmedVitalsReport
            {
                State = WolfmedVitalsState.Downed,
                Cause = WolfmedCause.Blood,
                Blockers = WolfmedCauseFlags.Pain,
                Breathing = WolfmedBreathing.Laboured,
                BreathingSource = WolfmedBreathingSource.LungsDamaged,
                BloodBand = WolfmedBloodBand.Weak,
                Blood = 0.43f,
                Trend = WolfmedBloodTrend.Falling,
                UnitsToLine = 19.4f,
                Line = 50f,
                BurnFluid = 1.2999999523162842f,
                BurnFluidFast = true,
                Organs = { new WolfmedOrganReading("lungs", WolfmedOrganBand.Impaired) },
                Routes = WolfmedRoutes.BurnFluid | WolfmedRoutes.Lungs | WolfmedRoutes.Circulation,
            };

            var lines = WolfmedVitalsText.Lines(owner);
            TestContext.Out.WriteLine("VitalsBlockIsCompactTest:\n  " + string.Join("\n  ", lines));

            var items = lines[1].Split(Loc.GetString("wolfmed-vitals-separator"));
            var expected = new[]
            {
                "Breathing laboured", "Pulse weak, rapid", "Blood 43% ↓", "Lungs impaired", "Burn fluid loss 1.3 u/s",
            };
            Assert.Multiple(() =>
            {
                Assert.That(lines, Has.Count.EqualTo(3), "the owner's state is not three lines.");
                Assert.That(lines[0], Is.EqualTo("DOWNED: blood loss (also: pain)"));
                Assert.That(items, Is.EqualTo(expected), "line 2 does not list each abnormal vital once, in order.");
                Assert.That(lines[1], Does.Contain("1.3 u/s").And.Not.Contain("1.29"));
                Assert.That(lines[2], Does.StartWith("Do first: "));
                Assert.That(lines[2], Does.Contain("transfuse ≈ 20 u"), "the transfusion units are not on line 3.");
                Assert.That(lines[1], Does.Not.Contain("transfuse"), "the transfusion units are repeated on line 2.");
                Assert.That(lines.Any(line => line.Contains("Getting worse")), Is.False);
                // Line 1 keeps its "(also: …)"; the rest carry no "(short of breath)" style asides.
                Assert.That(lines.Skip(1).Any(line => line.Contains('(')), Is.False, "a parenthetical survived.");
            });

            // Each aid once, in the routes' order (burns, lungs, circulation).
            var aids = lines[2]["Do first: ".Length..].Split("; ");
            Assert.That(aids, Is.EqualTo(new[]
            {
                WolfmedVitalsText.Aid(WolfmedRoutes.BurnFluid, false),
                WolfmedVitalsText.Aid(WolfmedRoutes.Lungs, false),
                "transfuse ≈ 20 u",
            }));

            // The same aid from two routes is listed once.
            owner.Routes |= WolfmedRoutes.Airway;
            var withAirway = WolfmedVitalsText.DoFirstLine(owner)!;
            Assert.That(withAirway.Split("; ").Distinct().Count(), Is.EqualTo(withAirway.Split("; ").Length));

            // A patient down but stable, and a healthy one.
            var stable = new WolfmedVitalsReport { State = WolfmedVitalsState.Downed, Cause = WolfmedCause.Pain, Blood = 1f };
            var healthy = new WolfmedVitalsReport { State = WolfmedVitalsState.Up, Blood = 1f };
            Assert.Multiple(() =>
            {
                Assert.That(WolfmedVitalsText.Lines(stable), Is.EqualTo(new[]
                    { "DOWNED: pain", "Vitals normal", "Do first: nothing; stable" }));
                Assert.That(WolfmedVitalsText.Lines(healthy), Is.EqualTo(new[] { "CONSCIOUS", "Vitals normal" }));
            });

            // The one rounding helper: at most one decimal, invariant, no trailing zero, no "-0".
            Assert.Multiple(() =>
            {
                Assert.That(WolfmedVitalsText.Number(1.2999999523162842f), Is.EqualTo("1.3"));
                Assert.That(WolfmedVitalsText.Number(2f), Is.EqualTo("2"));
                Assert.That(WolfmedVitalsText.Number(0.04f), Is.EqualTo("0"));
                Assert.That(WolfmedVitalsText.Number(-0.04f), Is.EqualTo("0"));
                Assert.That(WolfmedVitalsText.Number(1234.56f), Is.EqualTo("1234.6"));
                Assert.That(WolfmedVitalsText.Units(19.01f), Is.EqualTo("20"));
                Assert.That(WolfmedVitalsText.Whole(42.4f), Is.EqualTo("42"));
            });
        });
    }
}
