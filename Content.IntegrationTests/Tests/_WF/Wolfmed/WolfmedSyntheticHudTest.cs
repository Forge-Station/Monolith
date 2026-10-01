#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Client._WF.Wolfmed.Overlays;
using Content.IntegrationTests.Fixtures;
using Content.Server._WF.Wolfmed.Hud;
using Content.Server._WF.Wolfmed.Life;
using Content.Shared._Shitmed.Body.Organ;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WF.Wolfmed.Consciousness;
using Content.Shared._WF.Wolfmed.Hud;
using Content.Shared._WF.Wolfmed.Life;
using Content.Shared._WF.Wolfmed.Wounds;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs.Systems;
using Content.Shared.Rejuvenate;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Wolfmed;

/// <summary>
/// HUD: the synthetic diagnostics readout. The fault list is built on the server from the wounds and the
/// body conditions and pushed to one client; the escalation tiers and the on-screen layout are pure
/// functions, so both are measured here rather than looked at.
/// </summary>
[TestFixture]
[TestOf(typeof(WolfmedSyntheticHudSystem))]
public sealed class WolfmedSyntheticHudTest : GameTest
{
    /// <summary>
    /// A breach on the torso and a servo run cut in the left arm read as two lines, tagged by the data,
    /// named by the part, worst of the new ones first. A third fault arriving later goes above both.
    /// </summary>
    [Test]
    public async Task FaultsBuildFromWoundsTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var hudSystem = entities.System<WolfmedSyntheticHudSystem>();
            var wounds = entities.System<WoundSystem>();

            var body = entities.SpawnEntity("MobIPC", map.GridCoords);
            Assert.That(hudSystem.IsMechanicalBody(body), Is.True);

            var torso = Part(entities, body, BodyPartType.Torso);
            var arm = Part(entities, body, BodyPartType.Arm, BodyPartSymmetry.Left);
            wounds.CreateOrMergeWound(torso, "WFWolfmedBreachWound", FixedPoint2.New(20));
            wounds.CreateOrMergeWound(arm, "WFWolfmedServoDamageWound", FixedPoint2.New(20));

            var hud = entities.EnsureComponent<WolfmedSyntheticHudComponent>(body);
            hudSystem.Refresh((body, hud));

            Assert.That(hud.Faults, Has.Count.EqualTo(2));
            Assert.Multiple(() =>
            {
                // A cut servo is already a [CRIT]; a fresh breach is only a [WARN], so it sorts under it.
                Assert.That(hud.Faults[0].Part, Is.EqualTo(TargetBodyPart.LeftArm));
                Assert.That(hud.Faults[0].Line, Is.EqualTo("wolfmed-synthetic-line-servo"));
                Assert.That(hud.Faults[0].Severity, Is.EqualTo(WolfmedSyntheticSeverity.Crit));
                Assert.That(hud.Faults[1].Part, Is.EqualTo(TargetBodyPart.Torso));
                Assert.That(hud.Faults[1].Line, Is.EqualTo("wolfmed-synthetic-line-breach"));
                Assert.That(hud.Faults[1].Severity, Is.EqualTo(WolfmedSyntheticSeverity.Warn));
                Assert.That(hud.Advice, Is.EqualTo("wolfmed-synthetic-advice-servo"),
                    "the status line advises on the line at the top.");
            });

            // Newest first: a fault the readout did not have last time goes above everything already on it.
            var head = Part(entities, body, BodyPartType.Head);
            wounds.CreateOrMergeWound(head, "WFWolfmedOverheatingWound", FixedPoint2.New(50));
            hudSystem.Refresh((body, hud));

            Assert.That(hud.Faults, Has.Count.EqualTo(3));
            Assert.Multiple(() =>
            {
                Assert.That(hud.Faults[0].Line, Is.EqualTo("wolfmed-synthetic-line-overheating"));
                Assert.That(hud.Faults[0].Severity, Is.EqualTo(WolfmedSyntheticSeverity.Crit),
                    "past its escalation severity the data gives it the louder tag.");
                Assert.That(hud.Faults[1].Line, Is.EqualTo("wolfmed-synthetic-line-servo"));
                Assert.That(hud.Faults[2].Line, Is.EqualTo("wolfmed-synthetic-line-breach"));
            });

            // A breach on every part it will take is more lines than the wire carries.
            foreach (var (part, _) in entities.System<SharedBodySystem>().GetBodyChildren(body))
            {
                if (wounds.CanCreateWound(part, "WFWolfmedBreachWound"))
                    wounds.CreateOrMergeWound(part, "WFWolfmedBreachWound", FixedPoint2.New(20));

                if (wounds.CanCreateWound(part, "WFWolfmedDentWound"))
                    wounds.CreateOrMergeWound(part, "WFWolfmedDentWound", FixedPoint2.New(20));
            }

            hudSystem.Refresh((body, hud));
            Assert.That(hud.Faults, Has.Count.EqualTo(WolfmedSyntheticHudComponent.MaxFaults),
                "the readout is capped and never grows past what it can draw.");
        });
    }

    /// <summary>Flesh is not a chassis: no readout component, and none is ever filled for it.</summary>
    [Test]
    public async Task OrganicBodyHasNoReadoutTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var hudSystem = entities.System<WolfmedSyntheticHudSystem>();
            var human = entities.SpawnEntity("MobHuman", map.GridCoords);
            var torso = Part(entities, human, BodyPartType.Torso);
            entities.System<WoundSystem>().CreateOrMergeWound(torso, "WFWolfmedGunshotWound", FixedPoint2.New(30));

            Assert.Multiple(() =>
            {
                Assert.That(hudSystem.IsMechanicalBody(human), Is.False);
                Assert.That(entities.HasComponent<WolfmedSyntheticHudComponent>(human), Is.False);
            });
        });
    }

    /// <summary>
    /// The escalation bands. Depth and lost integrity are interchangeable inputs and the worse of the two
    /// wins, so a chassis that is barely scratched but nearly unconscious still gets the loud readout.
    /// </summary>
    [Test]
    public void TierBandsTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WolfmedSyntheticHudLineSystem.Tier(0f, 1f), Is.EqualTo(WolfmedSyntheticTier.Idle));
            Assert.That(WolfmedSyntheticHudLineSystem.Tier(0f, 0.95f), Is.EqualTo(WolfmedSyntheticTier.Idle));
            Assert.That(WolfmedSyntheticHudLineSystem.Tier(0f, 0.9f), Is.EqualTo(WolfmedSyntheticTier.Light));
            Assert.That(WolfmedSyntheticHudLineSystem.Tier(0.2f, 1f), Is.EqualTo(WolfmedSyntheticTier.Light));
            Assert.That(WolfmedSyntheticHudLineSystem.Tier(0f, 0.7f), Is.EqualTo(WolfmedSyntheticTier.Moderate));
            Assert.That(WolfmedSyntheticHudLineSystem.Tier(0.4f, 1f), Is.EqualTo(WolfmedSyntheticTier.Moderate));
            Assert.That(WolfmedSyntheticHudLineSystem.Tier(0f, 0.4f), Is.EqualTo(WolfmedSyntheticTier.Heavy));
            Assert.That(WolfmedSyntheticHudLineSystem.Tier(0.8f, 1f), Is.EqualTo(WolfmedSyntheticTier.Heavy));
            // The worse of the two, never the sum.
            Assert.That(WolfmedSyntheticHudLineSystem.Strain(0.3f, 0.9f), Is.EqualTo(0.3f).Within(0.001f));
            Assert.That(WolfmedSyntheticHudLineSystem.Strain(0.1f, 0.5f), Is.EqualTo(0.5f).Within(0.001f));
        });
    }

    /// <summary>
    /// The readout is data: every mechanical wound resolves to a line (its own or the fallback row), and
    /// every locale key the table names exists.
    /// </summary>
    [Test]
    public async Task EveryMechanicalWoundHasALineTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        var locale = server.ResolveDependency<ILocalizationManager>();

        await server.WaitAssertion(() =>
        {
            var lines = entities.System<WolfmedSyntheticHudLineSystem>();

            Assert.Multiple(() =>
            {
                foreach (var line in prototypes.EnumeratePrototypes<WolfmedSyntheticHudLinePrototype>())
                {
                    Assert.That(locale.HasString(line.Line.Id), Is.True,
                        $"{line.ID} has no readout string ({line.Line.Id}).");
                    Assert.That(line.Advice.Id, Is.Not.Empty, $"{line.ID} names no advice.");
                    Assert.That(locale.HasString(line.Advice.Id), Is.True,
                        $"{line.ID} has no advice string ({line.Advice.Id}).");
                    Assert.That(line.Wound == null || line.Condition == WolfmedSyntheticCondition.None, Is.True,
                        $"{line.ID} is keyed on both a wound and a condition.");
                }

                // Every condition the server can report has a row, or it would be silently dropped.
                foreach (var condition in Enum.GetValues<WolfmedSyntheticCondition>())
                {
                    if (condition == WolfmedSyntheticCondition.None)
                        continue;

                    Assert.That(lines.TryGetConditionLine(condition, out _), Is.True,
                        $"no readout line for condition {condition}.");
                }

                foreach (var wound in prototypes.EnumeratePrototypes<WoundPrototype>()
                             .Where(wound => wound.AnalyzerCategory == WolfmedWoundCategory.Mechanical)
                             .OrderBy(wound => wound.ID))
                {
                    Assert.That(lines.TryGetWoundLine(wound.ID, FixedPoint2.New(50), true, out _, out _), Is.True,
                        $"{wound.ID} resolves to no readout line, not even the fallback.");
                }
            });

            // The client renders the severity tag and the part name by key, so those must resolve too.
            Assert.Multiple(() =>
            {
                foreach (var part in Enum.GetValues<TargetBodyPart>())
                {
                    Assert.That(locale.HasString(WolfmedSyntheticHudLineSystem.PartKey(part)), Is.True,
                        $"no readout label for {part}.");
                }

                foreach (var key in new[] { "info", "warn", "crit", "fail" })
                    Assert.That(locale.HasString($"wolfmed-synthetic-tag-{key}"), Is.True);

                foreach (var key in new[]
                         {
                             "wolfmed-synthetic-system", "wolfmed-synthetic-diagnostics",
                             "wolfmed-synthetic-row-integrity", "wolfmed-synthetic-row-power",
                             "wolfmed-synthetic-row-fluid", "wolfmed-synthetic-row-servo",
                             "wolfmed-synthetic-row-sensor", "wolfmed-synthetic-row-core",
                             "wolfmed-synthetic-row-core-temp",
                             "wolfmed-synthetic-row-faults", "wolfmed-synthetic-advice",
                             "wolfmed-synthetic-banner-integrity", "wolfmed-synthetic-banner-downed",
                             "wolfmed-synthetic-banner-standby", "wolfmed-synthetic-banner-reboot",
                             "wolfmed-synthetic-banner-panic", "wolfmed-synthetic-panic-dump",
                             "wolfmed-synthetic-death-banner", "wolfmed-synthetic-death-banner-sub",
                         })
                    Assert.That(locale.HasString(key), Is.True, $"missing readout string: {key}");

                for (var i = 1; i <= 6; i++)
                    Assert.That(locale.HasString($"wolfmed-synthetic-idle-{i}"), Is.True);
            });
        });
    }

    /// <summary>
    /// The readout never covers the game HUD. It takes no input, so it cannot steal a click, but it can
    /// still hide the hotbar, the alerts column, the chat pane or the targeting doll.
    /// </summary>
    [Test]
    public void LayoutKeepsOutOfTheHudTest()
    {
        Assert.Multiple(() =>
        {
            foreach (var (width, height) in new[] { (2560f, 1351f), (1920f, 1080f), (1280f, 720f), (640f, 480f), (1024f, 768f) })
            {
                foreach (var requested in new[] { 0.6f, 1f, 1.4f, 2.5f, 5f })
                {
                    var screen = new UIBox2(0f, 0f, width, height);
                    var scale = WolfmedSyntheticHudLayout.FitScale(screen, requested);
                    var lines = WolfmedSyntheticHudLayout.MaxLines(screen, scale,
                        WolfmedSyntheticHudComponent.MaxFaults);
                    Assert.That(lines, Is.GreaterThan(3), $"{width}x{height} at {scale} shows almost no faults.");

                    var blocks = new (string Name, UIBox2 Box)[]
                    {
                        ("system", WolfmedSyntheticHudLayout.System(screen, scale)),
                        ("diagnostics", WolfmedSyntheticHudLayout.Diagnostics(screen, scale, lines)),
                        ("banner", WolfmedSyntheticHudLayout.Banner(screen, scale)),
                    };

                    foreach (var (name, box) in blocks)
                    {
                        Assert.That(box.Left, Is.GreaterThanOrEqualTo(screen.Left));
                        Assert.That(box.Right, Is.LessThanOrEqualTo(screen.Right));
                        Assert.That(box.Bottom, Is.LessThanOrEqualTo(screen.Top + height * WolfmedSyntheticHudLayout.BottomBand),
                            $"{name} at {width}x{height} reaches below the readout's own band.");

                        foreach (var (reserved, area) in WolfmedSyntheticHudLayout.Reserved(screen))
                        {
                            Assert.That(WolfmedSyntheticHudLayout.Overlaps(box, area), Is.False,
                                $"{name} covers the {reserved} at {width}x{height} scale {scale}.");
                        }
                    }

                    // Inset readouts stay below chat, away from the side buttons, and off the character.
                    Assert.That(blocks[0].Box.Top, Is.GreaterThanOrEqualTo(height * 0.40f));
                    Assert.That(blocks[1].Box.Top, Is.GreaterThanOrEqualTo(height * 0.40f));
                    Assert.That(blocks[0].Box.Left, Is.GreaterThanOrEqualTo(width * 0.14f));
                    Assert.That(blocks[1].Box.Right, Is.LessThanOrEqualTo(width * 0.90f));
                    Assert.That(WolfmedSyntheticHudLayout.Overlaps(blocks[0].Box, blocks[1].Box), Is.False);
                    Assert.That(WolfmedSyntheticHudLayout.Overlaps(blocks[0].Box, blocks[2].Box), Is.False);
                    Assert.That(WolfmedSyntheticHudLayout.Overlaps(blocks[1].Box, blocks[2].Box), Is.False);
                }
            }
        });
    }

    /// <summary>
    /// The readout draws in the viewport control's own coordinates. <c>ViewportBounds</c> arrives in global
    /// physical pixels while the handle is already translated to the control's top-left, so a viewport that
    /// does not start at the window origin used to throw both corner blocks off the screen; and the text
    /// scale has to carry the UI scale, because those pixels are physical ones.
    /// </summary>
    [Test]
    public void LayoutFollowsTheViewportControlTest()
    {
        Assert.Multiple(() =>
        {
            foreach (var (x, y, width, height) in new[] { (0, 0, 1920, 1080), (260, 140, 1280, 720), (96, 0, 1600, 900) })
            {
                var bounds = new UIBox2i(x, y, x + width, y + height);

                foreach (var ui in new[] { 1f, 1.25f, 2f })
                {
                    var screen = WolfmedSyntheticHudLayout.Screen(bounds, new Vector2(x, y));
                    Assert.That(screen.Left, Is.EqualTo(0f), "the readout kept the viewport's global origin.");
                    Assert.That(screen.Top, Is.EqualTo(0f), "the readout kept the viewport's global origin.");
                    Assert.That(screen.Width, Is.EqualTo((float) width));
                    Assert.That(screen.Height, Is.EqualTo((float) height));

                    var scale = WolfmedSyntheticHudLayout.FitScale(screen, WolfmedSyntheticHudLayout.Scale(1f, ui));
                    var lines = WolfmedSyntheticHudLayout.MaxLines(screen, scale,
                        WolfmedSyntheticHudComponent.MaxFaults);

                    foreach (var (name, box) in new (string, UIBox2)[]
                             {
                                 ("system", WolfmedSyntheticHudLayout.System(screen, scale)),
                                 ("diagnostics", WolfmedSyntheticHudLayout.Diagnostics(screen, scale, lines)),
                                 ("banner", WolfmedSyntheticHudLayout.Banner(screen, scale)),
                             })
                    {
                        var where = $"{name} at {width}x{height}+{x}+{y}, ui scale {ui}";
                        Assert.That(box.Left, Is.GreaterThanOrEqualTo(screen.Left), $"{where} starts left of the viewport.");
                        Assert.That(box.Top, Is.GreaterThanOrEqualTo(screen.Top), $"{where} starts above the viewport.");
                        Assert.That(box.Right, Is.LessThanOrEqualTo(screen.Right), $"{where} runs off the right edge.");
                        Assert.That(box.Bottom, Is.LessThanOrEqualTo(screen.Bottom), $"{where} runs off the bottom edge.");
                        Assert.That(box.Bottom, Is.LessThanOrEqualTo(screen.Top + height * WolfmedSyntheticHudLayout.BottomBand),
                            $"{where} reaches below the readout's own band.");
                    }
                }
            }

            // The player's text setting is clamped, so wolfmed.synthetic_hud_scale 0 cannot collapse a block.
            Assert.That(WolfmedSyntheticHudLayout.Scale(0f, 1f), Is.EqualTo(WolfmedSyntheticHudLayout.MinScale));
            Assert.That(WolfmedSyntheticHudLayout.Scale(99f, 1f), Is.EqualTo(WolfmedSyntheticHudLayout.MaxScale));
            var floor = WolfmedSyntheticHudLayout.System(new UIBox2(0f, 0f, 1920f, 1080f),
                WolfmedSyntheticHudLayout.Scale(0f, 1f));
            Assert.That(floor.Width, Is.GreaterThan(100f), "a zero text scale collapsed the SYSTEM block.");
            Assert.That(floor.Height, Is.GreaterThan(60f), "a zero text scale collapsed the SYSTEM block.");
        });
    }

    /// <summary>
    /// STANDBY means exactly one of two things: the chassis is shut down, or it is unconscious. Hull damage
    /// is neither of them, however much of it there is, and a readout that says otherwise is telling a
    /// walking machine it has stopped. M1a: the banner names the cause instead of a blanket STANDBY, so a
    /// pulled pump reads as the pump (plan §5.6).
    /// </summary>
    [Test]
    public async Task StandbyOnlyWhenTheChassisIsDownTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();
        EntityUid shut = default;

        await server.WaitAssertion(() =>
        {
            var hudSystem = entities.System<WolfmedSyntheticHudSystem>();
            var shutdown = entities.System<WolfmedShutdownSystem>();
            var mobState = entities.System<MobStateSystem>();
            var damage = entities.System<DamageableSystem>();

            var body = entities.SpawnEntity("MobIPC", map.GridCoords);
            var hud = entities.EnsureComponent<WolfmedSyntheticHudComponent>(body);

            // Small hits spread over the chassis, under the finishing damage so nothing is severed. While
            // the machine is still on its feet the readout may never claim standby: the flag is about the
            // cell and the pump, never about the hull.
            var targets = new[]
            {
                TargetBodyPart.Torso, TargetBodyPart.Head, TargetBodyPart.LeftArm,
                TargetBodyPart.RightArm, TargetBodyPart.LeftLeg, TargetBodyPart.RightLeg,
            };

            for (var hit = 0; hit < 120 && mobState.IsAlive(body); hit++)
            {
                damage.TryChangeDamage(body, Spec("Blunt", 6), ignoreResistances: true,
                    targetPart: targets[hit % targets.Length]);
                hudSystem.Refresh((body, hud));

                Assert.That(shutdown.IsShutDown(body), Is.False, "hull damage shut the chassis down.");
                Assert.That(hud.Shutdown, Is.False, "the readout called standby on a chassis that still walks.");
                Assert.That(hud.CauseLine, Is.Not.EqualTo("wolfmed-synthetic-cause-shutdown"),
                    "the readout named a shutdown on a chassis that still walks.");
            }

            Assert.Multiple(() =>
            {
                Assert.That(hud.Integrity, Is.LessThan(1f), "the chassis never took real damage.");
                Assert.That(hud.Faults, Is.Not.Empty, "the chassis took damage and reported no faults.");
            });

            // The cause the spec names, on a fresh chassis: the micro pump out of it, and that one does
            // stop the machine rather than only telling it that it stopped.
            var down = entities.SpawnEntity("MobIPC", map.GridCoords);
            var downHud = entities.EnsureComponent<WolfmedSyntheticHudComponent>(down);
            shut = down;

            Assert.That(entities.System<SharedBodySystem>().RemoveOrgan(Organ<HeartComponent>(entities, down)),
                Is.True);
            hudSystem.Refresh((down, downHud));

            Assert.Multiple(() =>
            {
                Assert.That(shutdown.IsShutDown(down), Is.True, "a chassis with no pump kept running.");
                Assert.That(downHud.Shutdown, Is.True, "the readout missed the shutdown.");
                Assert.That(mobState.IsCritical(down), Is.True, "a shut-down chassis stayed on its feet.");
                Assert.That(entities.GetComponent<WolfmedShutdownComponent>(down).Reason,
                    Is.EqualTo(WolfmedCauseSource.Pump), "a pulled pump was not the shutdown's reason.");
                Assert.That(downHud.CauseLine, Is.EqualTo("wolfmed-synthetic-cause-shutdown"),
                    "the banner did not name the shutdown.");
                Assert.That(entities.GetComponent<WolfmedConsciousnessComponent>(down).CauseSource,
                    Is.EqualTo(WolfmedCauseSource.Pump), "the banner's source is not the pump.");
            });

            // A rejuvenate wipes every consciousness pressure, this one included. The flag may not outlive
            // it: that leaves STANDBY over a chassis that is up and walking.
            entities.EventBus.RaiseLocalEvent(down, new RejuvenateEvent());
        });

        await Pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            var shutdown = entities.System<WolfmedShutdownSystem>();
            var mobState = entities.System<MobStateSystem>();

            Assert.That(shutdown.IsShutDown(shut) && !mobState.IsCritical(shut) && !mobState.IsDead(shut),
                Is.False, "the readout was left in standby over a chassis that was up and walking.");

            var hud = entities.GetComponent<WolfmedSyntheticHudComponent>(shut);
            entities.System<WolfmedSyntheticHudSystem>().Refresh((shut, hud));
            Assert.That(!shutdown.IsShutDown(shut) && hud.CauseLine == "wolfmed-synthetic-cause-shutdown",
                Is.False, "the banner kept naming a shutdown over a chassis that was up and walking.");
        });
    }

    /// <summary>
    /// M1a D (plan §5.6): the SYSTEM block carries a damage-sensor row, the chassis's pain against its soft
    /// cap, and a core-temperature row. M4: the row is the positronic core's own temperature, which the core-heat
    /// route tracks, not the chassis's.
    /// </summary>
    [Test]
    public async Task SystemBlockCarriesSensorsAndCoreTemperatureTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var entities = server.ResolveDependency<IEntityManager>();
        var map = await Pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var hudSystem = entities.System<WolfmedSyntheticHudSystem>();
            var body = entities.SpawnEntity("MobIPC", map.GridCoords);
            var hud = entities.EnsureComponent<WolfmedSyntheticHudComponent>(body);
            hudSystem.Refresh((body, hud));
            Assert.Multiple(() =>
            {
                Assert.That(hud.Sensors, Is.EqualTo(0f).Within(0.01f), "an undamaged chassis reports sensor load.");
                Assert.That(hud.CoreTemperature, Is.GreaterThan(0f), "no chassis temperature on the readout.");
            });

            var torso = Part(entities, body, BodyPartType.Torso);
            var pain = entities.GetComponent<PainComponent>(torso);
            pain.WoundPain = FixedPoint2.New(67.5f);
            entities.System<PainSystem>().SetPain((torso, pain), FixedPoint2.New(67.5f));
            // M4: the core's temperature, set through the core-heat route.
            var overheat = entities.System<Content.Server._WF.Wolfmed.Life.WolfmedOverheatSystem>();
            overheat.Tick(body, 0.0001f);
            overheat.SetCoreTemperature(body, 450f);
            hudSystem.Refresh((body, hud));

            Assert.Multiple(() =>
            {
                Assert.That(hud.Sensors, Is.EqualTo(0.5f).Within(0.01f), "half the soft cap is half the sensor load.");
                Assert.That(hud.CoreTemperature, Is.EqualTo(450f).Within(0.5f));
                Assert.That(Loc.GetString("wolfmed-synthetic-row-core-temp", ("value", 450)), Does.Contain("450 K"));
            });
        });
    }

    /// <summary>
    /// Playtest 3 (IPC): the SYSTEM panel is sized from what it holds. At UI scale 1 and 1.25, with 0, 1 and 6 faults
    /// and the longest advice in the locale, every row is one line high and inside the padding, no two rows overlap,
    /// the advice sits on its own rows under FAULTS and wraps whole into at most two, and the panel stays inside its
    /// largest size. Nothing the readout draws touches the viewport's origin, a healthy chassis draws nothing (the
    /// idle glyph was the four stray dots), and the centre of the screen stays clear.
    /// </summary>
    [Test]
    public async Task SyntheticHudPanelFitsTest()
    {
        var server = Pair.Server;
        await server.WaitIdleAsync();
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        var locale = server.ResolveDependency<ILocalizationManager>();

        var statuses = new List<(string Text, int Prefix)>();
        await server.WaitPost(() =>
        {
            // The longest advice any fault line can put on the status row, as the overlay composes it.
            var longest = prototypes.EnumeratePrototypes<WolfmedSyntheticHudLinePrototype>()
                .Select(line => locale.GetString(line.Advice.Id))
                .OrderByDescending(text => text.Length)
                .First();
            var composed = locale.GetString("wolfmed-synthetic-advice", ("advice", longest));
            statuses.Add((composed, composed.Length - longest.Length));
            var fluid = locale.GetString("wolfmed-synthetic-advice-low-fluid");
            var owner = locale.GetString("wolfmed-synthetic-advice", ("advice", fluid));
            statuses.Add((owner, owner.Length - fluid.Length));
            statuses.Add((locale.GetString("wolfmed-synthetic-idle-6"), 0));
        });

        var longestAdvice = statuses[0].Text;
        TestContext.Out.WriteLine($"longest advice: \"{longestAdvice}\" ({longestAdvice.Length} characters)");

        // The gauge row's columns fit the panel: label, the fixed-width bar, a gap, the right-aligned value.
        Assert.That(WolfmedSyntheticHudLayout.BarColumn + "[######]".Length + 1 + WolfmedSyntheticHudLayout.ValueColumns,
            Is.LessThanOrEqualTo(WolfmedSyntheticHudLayout.SystemColumns), "a gauge row is wider than the panel.");

        Assert.Multiple(() =>
        {
            foreach (var (x, y, width, height) in new[]
                     {
                         (0, 0, 1920, 1080), (0, 0, 1280, 720), (0, 0, 2560, 1351), (96, 40, 1600, 900),
                     })
            {
                var screen = WolfmedSyntheticHudLayout.Screen(new UIBox2i(x, y, x + width, y + height), new Vector2(x, y));

                foreach (var ui in new[] { 1f, 1.25f })
                {
                    var scale = WolfmedSyntheticHudLayout.FitScale(screen, WolfmedSyntheticHudLayout.Scale(1f, ui));
                    var line = WolfmedSyntheticHudLayout.LineHeight(scale);
                    var largest = WolfmedSyntheticHudLayout.System(screen, scale);

                    foreach (var (status, prefix) in statuses)
                    {
                        var advice = WolfmedSyntheticHudLayout.WrapAdvice(status, prefix);
                        Assert.That(advice, Has.Count.InRange(1, WolfmedSyntheticHudLayout.MaxAdviceRows));
                        Assert.That(string.Join(" ", advice.Select(row => row.Trim())), Is.EqualTo(status),
                            $"\"{status}\" did not wrap whole.");
                        foreach (var row in advice)
                        {
                            Assert.That(row.Length, Is.LessThanOrEqualTo(WolfmedSyntheticHudLayout.SystemColumns),
                                $"advice row \"{row}\" is wider than the panel.");
                        }

                        foreach (var gauges in new[] { 5, 6 })
                        {
                            foreach (var faults in new[] { 0, 1, 6 })
                            {
                                var where = $"{width}x{height}+{x}+{y}, ui {ui}, {gauges} gauges, {faults} faults, \"{status}\"";
                                var panel = WolfmedSyntheticHudLayout.SystemPanel(screen, scale, gauges, advice.Count);
                                var inner = new UIBox2(panel.Box.Left + WolfmedSyntheticHudLayout.Padding,
                                    panel.Box.Top + WolfmedSyntheticHudLayout.Padding,
                                    panel.Box.Right - WolfmedSyntheticHudLayout.Padding,
                                    panel.Box.Bottom - WolfmedSyntheticHudLayout.Padding);
                                var rows = panel.Rows().ToList();

                                Assert.That(panel.Gauges, Has.Length.EqualTo(gauges), where);
                                Assert.That(panel.Advice, Has.Length.EqualTo(advice.Count), where);
                                Assert.That(WolfmedSyntheticHudLayout.Contains(largest, panel.Box), Is.True,
                                    $"{where}: the panel outgrew its largest size.");

                                foreach (var (name, row) in rows)
                                {
                                    Assert.That(WolfmedSyntheticHudLayout.Contains(inner, row), Is.True,
                                        $"{where}: the {name} row is outside the panel's padding.");
                                    Assert.That(row.Height, Is.EqualTo(line).Within(0.01f),
                                        $"{where}: the {name} row is not one line high.");
                                    Assert.That(row.Width, Is.GreaterThanOrEqualTo(
                                            WolfmedSyntheticHudLayout.SystemColumns * WolfmedSyntheticHudLayout.CharWidth(scale) - 0.01f),
                                        $"{where}: the {name} row is narrower than the panel's columns.");
                                }

                                for (var i = 0; i < rows.Count; i++)
                                {
                                    for (var j = i + 1; j < rows.Count; j++)
                                    {
                                        Assert.That(WolfmedSyntheticHudLayout.Overlaps(rows[i].Box, rows[j].Box), Is.False,
                                            $"{where}: the {rows[i].Name} and {rows[j].Name} rows overlap.");
                                    }
                                }

                                // The blank half-row, then FAULTS, then the advice on its own rows.
                                Assert.That(panel.Faults.Top - panel.Gauges[^1].Bottom, Is.EqualTo(line / 2f).Within(0.01f), where);
                                Assert.That(panel.Advice[0].Top, Is.GreaterThanOrEqualTo(panel.Faults.Bottom), where);
                                Assert.That(panel.Box.Bottom - panel.Advice[^1].Bottom,
                                    Is.EqualTo(WolfmedSyntheticHudLayout.Padding).Within(0.01f),
                                    $"{where}: the panel is not sized from its rows.");

                                // Nothing is drawn at the origin: every box starts clear of the viewport's top-left corner.
                                foreach (var (name, box) in WolfmedSyntheticHudLayout.DrawnBoxes(screen, scale, gauges,
                                             advice.Count, faults))
                                {
                                    Assert.That(box.Left >= screen.Left + WolfmedSyntheticHudLayout.Margin &&
                                                box.Top >= screen.Top + WolfmedSyntheticHudLayout.Margin, Is.True,
                                        $"{where}: {name} touches the screen origin at {box}.");
                                    Assert.That(WolfmedSyntheticHudLayout.Contains(screen, box), Is.True,
                                        $"{where}: {name} is off the viewport.");
                                }
                            }
                        }
                    }

                    // FAULTS is one row whatever the count, so the panel is the same height for 0, 1 and 6 faults.
                    Assert.That(WolfmedSyntheticHudLayout.SystemHeight(scale, 6, 2),
                        Is.EqualTo(WolfmedSyntheticHudLayout.SystemPanel(screen, scale, 6, 2).Box.Height).Within(0.01f));
                }
            }

            // A healthy chassis draws nothing at all; a light one only its SYSTEM panel.
            foreach (var faults in new[] { 0, 1, 6 })
            {
                Assert.That(WolfmedSyntheticHudLayout.Visible(WolfmedSyntheticTier.Idle, false, false, faults),
                    Is.EqualTo((false, false)), "a healthy chassis draws something.");
            }

            Assert.That(WolfmedSyntheticHudLayout.Visible(WolfmedSyntheticTier.Light, false, false, 6), Is.EqualTo((true, false)));
            Assert.That(WolfmedSyntheticHudLayout.Visible(WolfmedSyntheticTier.Heavy, false, false, 6), Is.EqualTo((true, true)));
            Assert.That(WolfmedSyntheticHudLayout.Visible(WolfmedSyntheticTier.Heavy, true, false, 6), Is.EqualTo((false, false)));

            // An advice too long for two rows ends in an ellipsis instead of running past the panel.
            var wrapped = WolfmedSyntheticHudLayout.WrapAdvice(
                "ADVICE: " + string.Join(" ", Enumerable.Repeat("REPLACE SERVO", 8)), 8);
            Assert.That(wrapped, Has.Count.EqualTo(WolfmedSyntheticHudLayout.MaxAdviceRows));
            Assert.That(wrapped[^1], Does.EndWith("..."));
            Assert.That(wrapped.All(row => row.Length <= WolfmedSyntheticHudLayout.SystemColumns), Is.True);
        });
    }

    private static DamageSpecifier Spec(string type, int amount) => new()
    {
        DamageDict = { [new ProtoId<DamageTypePrototype>(type)] = FixedPoint2.New(amount) },
    };

    private static EntityUid Organ<T>(IEntityManager entities, EntityUid body) where T : IComponent
    {
        foreach (var (organ, _) in entities.System<SharedBodySystem>().GetBodyOrgans(body))
        {
            if (entities.HasComponent<T>(organ))
                return organ;
        }

        Assert.Fail($"the fixture has no {typeof(T).Name}.");
        return default;
    }

    private static EntityUid Part(
        IEntityManager entities,
        EntityUid body,
        BodyPartType type,
        BodyPartSymmetry symmetry = BodyPartSymmetry.None)
    {
        return entities.System<SharedBodySystem>().GetBodyChildren(body)
            .Single(part => part.Component.PartType == type && part.Component.Symmetry == symmetry)
            .Id;
    }
}
