#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.Client._WF.Wolfmed.Life;
using Content.Client.Ghost.UI;
using Content.Client.UserInterface.Systems.Chat;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._WF.Wolfmed.Autodoc;
using Content.Server._WF.Wolfmed.Consciousness;
using Content.Server._WF.Wolfmed.Life;
using Content.Server.Chat.Systems;
using Content.Server.Ghost;
using Content.Server.Medical;
using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Wolfmed.Autodoc;
using Content.Shared._WF.Wolfmed.CCVar;
using Content.Shared._WF.Wolfmed.Consciousness;
using Content.Shared._WF.Wolfmed.Life;
using Content.Shared.Actions;
using Content.Shared.Body.Part;
using Content.Shared.Chat;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Ghost;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Mind;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Players;
using Content.Shared.Speech.EntitySystems;
using Content.Shared.StatusEffect;
using NUnit.Framework;
using Robust.Client.UserInterface;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Wolfmed.Scenarios;

/// <summary>
/// M1a package C (plan §5.4, §2.2): only the Dying may let go, and every way out of the body says exactly what
/// happens. Succumb is a revivable death with the brain left in place; the <c>ghost</c> command reaches the same
/// dialog in arrest and a "left alive but empty" one otherwise; a pod revival offers the way back as the hand
/// defibrillator does. Also pins that a Critical player still hears speech.
/// </summary>
[TestFixture]
[TestOf(typeof(WolfmedDyingActionsSystem))]
public sealed class WolfmedHonestEndingTest : GameTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: WolfmedEndingAutodoc
  parent: WFMachineAutodoc
  suffix: honest ending
  components:
  - type: Autodoc
    stepTime: 0.02
    bestStepTime: 0.02
    malfunctionChance: 0
    autoPlanInterval: 0.2
  - type: ApcPowerReceiver
    needsPower: false
";

    private static readonly string[] UpstreamCritActions = { "ActionCritSuccumb", "ActionCritLastWords", "ActionCritFakeDeath" };

    private async Task Pin()
    {
        await OverrideCVar(Side.Server, WolfmedCVars.Consciousness, true);
        await OverrideCVar(Side.Server, WolfmedCVars.ConsciousnessPainDown, 0.95f);
        await OverrideCVar(Side.Server, WolfmedCVars.ConsciousnessPainOut, 1.4f);
        await OverrideCVar(Side.Server, WolfmedCVars.ConsciousnessBloodDown, 0.5f);
        await OverrideCVar(Side.Server, WolfmedCVars.ConsciousnessBloodOut, 0.35f);
        await OverrideCVar(Side.Server, WolfmedCVars.ConsciousnessHysteresis, 0.1f);
        await OverrideCVar(Side.Server, WolfmedCVars.PainFaintSeconds, 20f);
        await OverrideCVar(Side.Server, WolfmedCVars.ArrestBlood, 0.30f);
        await OverrideCVar(Side.Server, WolfmedCVars.DefibBlood, 0.25f);
    }

    /// <summary>The prototype ids of every action the body holds.</summary>
    private List<string> ActionIds(EntityUid body) =>
        SEntMan.System<SharedActionsSystem>().GetActions(body)
            .Select(action => SEntMan.GetComponent<MetaDataComponent>(action.Id).EntityPrototype?.ID ?? string.Empty)
            .ToList();

    private void AssertNoWayOut(EntityUid body, string what)
    {
        var ids = ActionIds(body);
        Assert.That(ids, Does.Not.Contain(WolfmedDyingActionsSystem.SuccumbAction.Id), $"{what} was offered Succumb.");
        Assert.That(ids, Does.Not.Contain(WolfmedDyingActionsSystem.LastWordsAction.Id), $"{what} was offered Last Words.");
        foreach (var upstream in UpstreamCritActions)
            Assert.That(ids, Does.Not.Contain(upstream), $"{what} was granted the upstream {upstream}.");
    }

    /// <summary>Pain on one part with its wound floor at the same value, so it does not recover away.</summary>
    private void SetPain(WolfmedScenario s, EntityUid body, BodyPartType type, float value)
    {
        var part = s.Part(body, type);
        var pain = SEntMan.GetComponent<PainComponent>(part);
        pain.WoundPain = FixedPoint2.New(value);
        SEntMan.System<PainSystem>().SetPain((part, pain), FixedPoint2.New(value));
    }

    /// <summary>Puts the test player into a fresh body of this prototype. Returns the body and its mind.</summary>
    private async Task<(EntityUid Body, EntityUid Mind)> Possess(string prototype, TestMapData map)
    {
        Assert.That(ServerSession, Is.Not.Null, "These tests need a connected pair.");
        var session = ServerSession!;
        var minds = SEntMan.System<SharedMindSystem>();
        EntityUid body = default;
        EntityUid mind = default;

        await Server.WaitPost(() =>
        {
            minds.WipeMind(session.ContentData()?.Mind);
            body = SEntMan.SpawnEntity(prototype, map.GridCoords);
            mind = minds.CreateMind(session.UserId).Owner;
            minds.TransferTo(mind, body);
        });
        await RunTicksSync(30);
        Assert.That(session.AttachedEntity, Is.EqualTo(body), "the player did not attach to the new body.");
        return (body, mind);
    }

    /// <summary>What the <c>ghost</c> command runs (GhostCommand.cs), without its lobby check.</summary>
    private bool GhostCommand(EntityUid mind) =>
        SEntMan.System<GhostSystem>().OnGhostAttempt(mind, true, true, mind: SEntMan.GetComponent<MindComponent>(mind));

    private async Task<int> ClientWindows<T>() where T : Control
    {
        var count = 0;
        await Client.WaitPost(() => count = Client.ResolveDependency<IUserInterfaceManager>().WindowRoot.Children
            .OfType<T>().Count(window => window.Visible));
        return count;
    }

    /// <summary>
    /// The honest ending (plan §12 M1a): faint, blood-Unconscious and shutdown bodies have no Succumb; an
    /// arrested one does. Succumb leaves Dead, brain organ at 0 and still in the body, no arrest, a returnable
    /// ghost and Asphyxiation untouched. The ghost command in arrest opens the Succumb dialog and ghosts nobody
    /// until it is confirmed; from a faint it opens "left alive but empty", which ghosts for good and deals
    /// nothing. Brain repair and a shock, by hand or by pod, bring the same mind back and offer the return.
    /// </summary>
    [Test]
    public async Task HonestEndingScenarioTest()
    {
        await Pin();
        var map = await Pair.CreateTestMap();
        var s = new WolfmedScenario(SEntMan);
        var dying = SEntMan.System<WolfmedDyingActionsSystem>();
        var mobState = SEntMan.System<MobStateSystem>();
        var minds = SEntMan.System<SharedMindSystem>();

        // --- Faint, blood-Unconscious and shutdown: out, but not Dying. ---
        EntityUid fainted = default, bled = default, ipc = default, arrested = default;
        await Server.WaitPost(() =>
        {
            s.SetAir(map.MapUid, true);
            fainted = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            bled = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            ipc = SEntMan.SpawnEntity("MobIPC", map.GridCoords);
            arrested = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            minds.TransferTo(minds.CreateMind(null).Owner, ipc);
        });
        await RunSeconds(2);

        await Server.WaitAssertion(() =>
        {
            SetPain(s, fainted, BodyPartType.Torso, 100);
            SetPain(s, fainted, BodyPartType.Head, 100);
            s.SetBlood(bled, 0.33f);

            var slots = SEntMan.System<ItemSlotsSystem>();
            Assert.That(slots.TryGetSlot(ipc, "cell_slot", out var slot), Is.True);
            SEntMan.System<SharedContainerSystem>().Remove(slot!.Item!.Value, slot.ContainerSlot!);

            Assert.That(s.Life.StartArrest(arrested, "oxygen"), Is.True);
        });
        await RunSeconds(2);

        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(s.Vitals(fainted).Cause, Is.EqualTo(WolfmedCause.PainFaint));
                Assert.That(s.Vitals(bled).Cause, Is.EqualTo(WolfmedCause.Blood));
                Assert.That(s.Vitals(bled).State, Is.EqualTo(WolfmedConsciousness.Unconscious));
                Assert.That(s.Vitals(ipc).Cause, Is.EqualTo(WolfmedCause.Shutdown));
                Assert.That(mobState.IsCritical(fainted) && mobState.IsCritical(bled) && mobState.IsCritical(ipc),
                    Is.True, "the fixtures are not all out.");

                AssertNoWayOut(fainted, "a fainted patient");
                AssertNoWayOut(bled, "a patient out from blood loss");

                // Playtest 5: a shut-down chassis may let go after all. Nothing in it runs out, but nobody may come.
                var shutIds = ActionIds(ipc);
                Assert.That(shutIds, Does.Contain(WolfmedDyingActionsSystem.SuccumbAction.Id), "a shut-down chassis has no Succumb.");
                Assert.That(shutIds, Does.Contain(WolfmedDyingActionsSystem.LastWordsAction.Id));

                var ids = ActionIds(arrested);
                Assert.That(ids, Does.Contain(WolfmedDyingActionsSystem.SuccumbAction.Id), "an arrested patient has no Succumb.");
                Assert.That(ids, Does.Contain(WolfmedDyingActionsSystem.LastWordsAction.Id));
                foreach (var upstream in UpstreamCritActions)
                    Assert.That(ids, Does.Not.Contain(upstream), $"an arrested wound host holds the upstream {upstream}.");
            });

            // The heart back: the way out goes with it.
            Assert.That(s.Shock(arrested, out _), Is.True);
            AssertNoWayOut(arrested, "a patient whose heart restarted");
        });

        // --- The ghost command from a faint: "left alive but empty". ---
        var (faintBody, faintMind) = await Possess("MobHuman", map);
        FixedPoint2 faintDamage = default;
        await Server.WaitAssertion(() =>
        {
            SetPain(s, faintBody, BodyPartType.Torso, 100);
            SetPain(s, faintBody, BodyPartType.Head, 100);
            Assert.That(s.Vitals(faintBody).Cause, Is.EqualTo(WolfmedCause.PainFaint));
            faintDamage = SEntMan.GetComponent<DamageableComponent>(faintBody).TotalDamage;

            Assert.That(GhostCommand(faintMind), Is.True, "the ghost command was denied instead of asking.");
            Assert.That(dying.GetPendingChoice(faintBody), Is.EqualTo(WolfmedEndingChoice.LeaveAlive));
            Assert.That(ServerSession!.AttachedEntity, Is.EqualTo(faintBody), "the ghost command ghosted before asking.");
        });
        await RunTicksSync(10);
        Assert.That(await ClientWindows<WolfmedChoiceWindow>(), Is.GreaterThan(0), "the dialog never reached the player.");

        await Server.WaitAssertion(() =>
        {
            Assert.That(dying.Confirm(faintBody), Is.True);
            var ghost = ServerSession!.AttachedEntity;
            Assert.Multiple(() =>
            {
                Assert.That(ghost, Is.Not.EqualTo(faintBody));
                Assert.That(SEntMan.TryGetComponent(ghost, out GhostComponent? ghostComp), Is.True);
                Assert.That(ghostComp!.CanReturnToBody, Is.False, "leaving a living body offered a way back.");
                Assert.That(mobState.IsDead(faintBody), Is.False, "leaving the body killed it.");
                Assert.That(SEntMan.GetComponent<DamageableComponent>(faintBody).TotalDamage, Is.EqualTo(faintDamage),
                    "leaving the body dealt damage.");
            });
        });

        // --- "Left alive but empty" open when the body dies another way: the dialog goes, nothing is ghosted. ---
        var (diedBody, diedMind) = await Possess("MobHuman", map);
        await Server.WaitAssertion(() =>
        {
            Assert.That(GhostCommand(diedMind), Is.True);
            Assert.That(dying.GetPendingChoice(diedBody), Is.EqualTo(WolfmedEndingChoice.LeaveAlive));

            s.Life.Kill(diedBody);
            Assert.That(mobState.IsDead(diedBody), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(dying.GetPendingChoice(diedBody), Is.EqualTo(WolfmedEndingChoice.None),
                    "death left the \"left alive but empty\" dialog open.");
                Assert.That(dying.Confirm(diedBody), Is.False);
                Assert.That(dying.LeaveAlive(diedBody), Is.False, "a dead body was left \"alive but empty\".");
                Assert.That(ServerSession!.AttachedEntity, Is.EqualTo(diedBody), "a stale dialog ghosted the player.");
            });
        });

        // --- Arrest: Last Words, the ghost command, then Succumb. ---
        var (body, mindId) = await Possess("MobHuman", map);
        // A pooled pair's client keeps its chat history from earlier tests: read only what this one adds.
        var historyStart = 0;
        await Client.WaitPost(() => historyStart = Client.ResolveDependency<IUserInterfaceManager>()
            .GetUIController<ChatUIController>().History.Count);
        FixedPoint2 asphyxiation = default;
        await Server.WaitAssertion(() =>
        {
            Assert.That(s.Life.StartArrest(body, "oxygen"), Is.True);
            asphyxiation = s.Damage(body, "Asphyxiation");

            // Playtest 3: Last Words opens "Let go?" first; nothing is whispered until the player says yes.
            dying.OpenSuccumbDialog(body, 30);
            Assert.That(dying.GetPendingChoice(body), Is.EqualTo(WolfmedEndingChoice.Succumb),
                "Last Words did not open the Let go dialog first.");
            dying.Decline(body);
            Assert.That(dying.GetPendingChoice(body), Is.EqualTo(WolfmedEndingChoice.None));
            Assert.That(mobState.IsDead(body), Is.False, "declining Let go after Last Words killed.");
        });
        await RunTicksSync(10);

        var heard = false;
        await Client.WaitPost(() =>
        {
            var chat = Client.ResolveDependency<IUserInterfaceManager>().GetUIController<ChatUIController>();
            heard = chat.History.Skip(historyStart).Any(h => h.Msg.Channel == ChatChannel.Whisper &&
                                                             h.Msg.Message.Contains("crew of the"));
        });
        Assert.That(heard, Is.False, "last words were whispered before Let go was answered.");

        await Server.WaitAssertion(() =>
        {
            Assert.That(GhostCommand(mindId), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(dying.GetPendingChoice(body), Is.EqualTo(WolfmedEndingChoice.Succumb),
                    "the ghost command in arrest did not open the Succumb dialog.");
                Assert.That(ServerSession!.AttachedEntity, Is.EqualTo(body), "the ghost command ghosted before confirming.");
                Assert.That(mobState.IsDead(body), Is.False);
                Assert.That(s.Damage(body, "Asphyxiation"), Is.EqualTo(asphyxiation), "the ghost command dealt the top-up.");
            });
        });

        await Server.WaitAssertion(() =>
        {
            Assert.That(dying.Confirm(body), Is.True);
            var brain = s.Life.GetBrainOrgan(body);
            var mind = SEntMan.GetComponent<MindComponent>(mindId);
            Assert.Multiple(() =>
            {
                Assert.That(mobState.IsDead(body), Is.True, "Succumb did not kill.");
                Assert.That(brain, Is.Not.Null, "the brain left the body.");
                Assert.That(brain!.Value.Comp.Health, Is.EqualTo(FixedPoint2.Zero), "the brain was not destroyed.");
                Assert.That(SEntMan.HasComponent<WolfmedCardiacArrestComponent>(body), Is.False);
                Assert.That(s.Damage(body, "Asphyxiation"), Is.EqualTo(asphyxiation), "Succumb dealt Asphyxiation.");
                Assert.That(mind.OwnedEntity, Is.EqualTo(body), "the mind left the body for good.");
                Assert.That(SEntMan.TryGetComponent(mind.VisitingEntity, out GhostComponent? ghost), Is.True);
                Assert.That(ghost!.CanReturnToBody, Is.True, "Succumb's ghost cannot return.");
                AssertNoWayOut(body, "a dead body");
            });
        });

        // --- Brain repair and the hand paddles: the same mind, offered the return. ---
        var returnPrompts = await ClientWindows<ReturnToBodyMenu>();
        await Server.WaitAssertion(() =>
        {
            s.Life.RepairBrain(body);
            var medic = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            var defib = SEntMan.SpawnEntity("Defibrillator", map.GridCoords);
            SEntMan.System<ItemToggleSystem>().TryActivate(defib, medic);
            s.Revival.ForcedRoll = 0f;
            SEntMan.System<DefibrillatorSystem>().Zap(defib, body, medic);
            s.Revival.ForcedRoll = null;

            Assert.That(mobState.IsDead(body), Is.False, "brain repair and a shock did not revive the body.");
            Assert.That(SEntMan.GetComponent<MindComponent>(mindId).OwnedEntity, Is.EqualTo(body));
        });
        await RunTicksSync(10);
        Assert.That(await ClientWindows<ReturnToBodyMenu>(), Is.GreaterThan(returnPrompts),
            "the hand defibrillator's revival offered no return.");

        await Server.WaitAssertion(() =>
        {
            minds.UnVisit(mindId);
            Assert.That(ServerSession!.AttachedEntity, Is.EqualTo(body), "the same mind did not come back.");
        });

        // --- Succumb again, brain repair, and the pod: the same prompt. ---
        await Server.WaitAssertion(() =>
        {
            Assert.That(s.Life.StartArrest(body, "oxygen"), Is.True);
            // The shock and the repaired brain stutter, and a stutter drops letters at random: whisper without it.
            SEntMan.System<StatusEffectsSystem>().TryRemoveStatusEffect(body, SharedStutteringSystem.StutterKey);
            // Playtest 3: the whisper prompt's answer whispers, cut to the limit, and then lets go.
            Assert.That(dying.SayLastWords(body, "Remember me to the crew of the Wolfgate", 30), Is.True);
            Assert.That(mobState.IsDead(body), Is.True, "last words did not let go.");
            s.Life.RepairBrain(body);
        });
        await RunTicksSync(10);
        await Client.WaitPost(() =>
        {
            var chat = Client.ResolveDependency<IUserInterfaceManager>().GetUIController<ChatUIController>();
            heard = chat.History.Skip(historyStart).Any(h => h.Msg.Channel == ChatChannel.Whisper &&
                                                             h.Msg.Message.Contains("crew of the") &&
                                                             !h.Msg.Message.Contains("Wolfgate"));
        });
        Assert.That(heard, Is.True, "the last words were not whispered, or not cut to 30 characters.");
        returnPrompts = await ClientWindows<ReturnToBodyMenu>();

        await Server.WaitAssertion(() =>
        {
            var autodoc = SEntMan.System<AutodocSystem>();
            var pod = SEntMan.SpawnEntity("WolfmedEndingAutodoc", map.GridCoords);
            var podEnt = new Entity<AutodocComponent>(pod, SEntMan.GetComponent<AutodocComponent>(pod));
            Assert.That(SEntMan.System<ItemSlotsSystem>().TryInsert(pod, AutodocComponent.ModuleSlotId,
                SEntMan.SpawnEntity("WFAutodocDefibModule", map.GridCoords), null), Is.True);
            Assert.That(autodoc.TryInsert(podEnt, body), Is.True);

            s.Revival.ForcedRoll = 0f;
            Assert.That(autodoc.TryDefibrillateOccupant(podEnt, body), Is.True, "the pod did not revive the repaired body.");
            s.Revival.ForcedRoll = null;
            Assert.That(SEntMan.GetComponent<MindComponent>(mindId).OwnedEntity, Is.EqualTo(body));
        });
        await RunTicksSync(10);
        Assert.That(await ClientWindows<ReturnToBodyMenu>(), Is.GreaterThan(returnPrompts),
            "the pod's revival offered no return.");
    }

    /// <summary>
    /// Plan §5.4 item 1: no wound host is ever granted the upstream crit actions. Every entity prototype that is
    /// a wound host and carries MobStateActions is spawned and its Critical list checked, and a human held
    /// Critical is checked for the actions themselves.
    /// </summary>
    [Test]
    public async Task CritSuccumbNeverGrantedToWoundHostsTest()
    {
        await Pin();
        var map = await Pair.CreateTestMap();
        var factory = SEntMan.ComponentFactory;
        var woundHost = factory.GetComponentName(typeof(WoundHostComponent));
        var stateActions = factory.GetComponentName(typeof(MobStateActionsComponent));

        var hosts = SProtoMan.EnumeratePrototypes<EntityPrototype>()
            .Where(p => !p.Abstract && p.Components.ContainsKey(woundHost) && p.Components.ContainsKey(stateActions))
            .Select(p => p.ID)
            .ToList();
        Assert.That(hosts, Does.Contain("MobHuman"), "the scan found no wound hosts at all.");

        await Server.WaitAssertion(() =>
        {
            foreach (var id in hosts)
            {
                var mob = SEntMan.SpawnEntity(id, map.GridCoords);
                if (SEntMan.HasComponent<WoundHostComponent>(mob) &&
                    SEntMan.TryGetComponent(mob, out MobStateActionsComponent? actions))
                {
                    Assert.That(actions.Actions.ContainsKey(Content.Shared.Mobs.MobState.Critical), Is.False,
                        $"{id} keeps the upstream Critical actions.");
                }

                SEntMan.DeleteEntity(mob);
            }

            var human = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            SEntMan.System<WolfmedConsciousnessSystem>().SetExternalPressure(human, "test", 1f);
            Assert.That(SEntMan.System<MobStateSystem>().IsCritical(human), Is.True);
            AssertNoWayOut(human, "a human held Critical");
        });
    }

    /// <summary>Plan §2.2: a Critical player still hears speech nearby (ChatSystem only filters crit LOOC).</summary>
    [Test]
    public async Task CriticalHearingTest()
    {
        await Pin();
        var map = await Pair.CreateTestMap();
        var (listener, _) = await Possess("MobHuman", map);
        EntityUid speaker = default;

        await Server.WaitAssertion(() =>
        {
            speaker = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            SEntMan.System<WolfmedConsciousnessSystem>().SetExternalPressure(listener, "test", 1f);
            Assert.That(SEntMan.System<MobStateSystem>().IsCritical(listener), Is.True);
            SEntMan.System<ChatSystem>().TrySendInGameICMessage(speaker, "Stay with me, you hear", InGameICChatType.Speak,
                false);
        });
        await RunTicksSync(15);

        var heard = false;
        await Client.WaitPost(() =>
        {
            var chat = Client.ResolveDependency<IUserInterfaceManager>().GetUIController<ChatUIController>();
            heard = chat.History.Any(h => h.Msg.Channel == ChatChannel.Local && h.Msg.Message.Contains("Stay with me"));
        });
        Assert.That(heard, Is.True, "a Critical player did not hear speech next to them.");
    }
}
