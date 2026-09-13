using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Content.Client._Shitmed.Medical.Surgery;
using Content.Client._WF.Genitals;
using Content.Server._WF.Genitals;
using Content.Server.Administration.Logs;
using Content.Shared._Shitmed.Medical.Surgery;
using Content.Shared._Shitmed.Medical.Surgery.Conditions;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.Genitals;
using Content.Shared._WF.Genitals.Components;
using Content.Shared._WF.Genitals.Systems;
using Content.Shared.Administration.Logs;
using Content.Shared.Body.Organ;
using Content.Shared.Body.Part;
using Content.Shared.Database;
using Content.Shared.Humanoid;
using Content.Shared.Prototypes;
using Content.Shared.Standing;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
using static Content.IntegrationTests.Tests._WF.Genitals.GenitalTestHelpers;

namespace Content.IntegrationTests.Tests._WF.Genitals;

/// <summary>Anatomy surgeries - the body mirror, the patient condition, the server-side surgeon check, the client list filter and the popup keys.</summary>
[TestFixture]
[TestOf(typeof(GenitalSurgeryConditionSystem))]
public sealed class GenitalSurgeryTest
{
    // Plain strings: the YAML linter validates static prototype-id fields, and these are checked by SurgeryPrototypesTest.
    private const string RemovePenis = "SurgeryWFRemovePenis";
    private const string InsertPenis = "SurgeryWFInsertPenis";
    private const string RemoveVagina = "SurgeryWFRemoveVagina";
    private const string OpenIncision = "SurgeryOpenIncision";
    private const string StepOpenIncision = "SurgeryStepOpenIncisionScalpel";
    private const string StepClampInternal = "SurgeryStepClampInternalBleeders";
    private const string StepRemoveOrgan = "SurgeryStepRemoveOrgan";
    private const string StepInsertOrgan = "SurgeryStepInsertOrgan";
    private const string StepSealOrgan = "SurgeryStepSealOrganWound";
    private const string NonHumanoidMob = "MobMouse";
    private const string RefusedKey = "wf-anatomy-surgery-refused";
    private const string ProcedurePopupPrefix = "surgery-popup-procedure-SurgeryWF";

    /// <summary>One remove and one insert surgery per organ, with the organ's marker component.</summary>
    private static readonly (GenitalSlot Slot, string Remove, string Insert, Type Marker)[] Procedures =
    {
        (GenitalSlot.Penis, RemovePenis, InsertPenis, typeof(PenisOrganComponent)),
        (GenitalSlot.Testicles, "SurgeryWFRemoveTesticles", "SurgeryWFInsertTesticles", typeof(TesticlesOrganComponent)),
        (GenitalSlot.Vagina, RemoveVagina, "SurgeryWFInsertVagina", typeof(VaginaOrganComponent)),
        (GenitalSlot.Womb, "SurgeryWFRemoveWomb", "SurgeryWFInsertWomb", typeof(WombOrganComponent)),
        (GenitalSlot.Breasts, "SurgeryWFRemoveBreasts", "SurgeryWFInsertBreasts", typeof(BreastsOrganComponent)),
    };

    /// <summary>How a surgeon fails the surgeon check.</summary>
    public enum SurgeonCase
    {
        /// <summary>An adult humanoid with other toggles on but the master switch off.</summary>
        MasterOff,

        /// <summary>A 17-year-old humanoid with the master switch on.</summary>
        Minor,

        /// <summary>A non-humanoid mob with the master switch on.</summary>
        NotHumanoid,
    }

    /// <summary>Each organ has a torso remove and insert surgery with the generic organ steps, its marker and slot, and both anatomy gates.</summary>
    [Test]
    public async Task SurgeryPrototypesTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var protoMan = server.ProtoMan;
        var factory = server.ResolveDependency<IComponentFactory>();

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                foreach (var (slot, remove, insert, marker) in Procedures)
                {
                    var markerName = factory.GetComponentName(marker);
                    AssertSurgery(protoMan, factory, remove, markerName, false, null, new[] { StepClampInternal, StepRemoveOrgan });
                    AssertSurgery(protoMan, factory, insert, markerName, true, GenitalOrganSystem.SlotId(slot), new[] { StepInsertOrgan, StepSealOrgan });
                }

                // The client filter and the surgeon check cover exactly these ten real surgeries.
                var expected = Procedures.SelectMany(p => new[] { p.Remove, p.Insert }).ToHashSet();
                var marked = protoMan.EnumeratePrototypes<EntityPrototype>()
                    .Where(p => p.HasComponent<SurgeryComponent>(factory) && p.HasComponent<GenitalSurgeryComponent>(factory))
                    .Select(p => p.ID)
                    .ToHashSet();
                Assert.That(marked, Is.EquivalentTo(expected), "Every anatomy surgery must be one of the ten organ procedures.");
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Removing the penis empties its mirror field, so its sheath goes too; reinserting it into another adult keeps the donor's colour.</summary>
    [Test]
    public async Task RemoveAndReinsertTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        var recipientSkin = Color.FromHex("#4A3B2C");
        EntityUid surgeon = default;
        EntityUid donor = default;
        EntityUid recipient = default;
        EntityUid organ = default;
        GenitalOrganState donorState = default;
        await server.WaitPost(() =>
        {
            surgeon = SpawnSurgeon(entMan, map.GridCoords);
            donor = SpawnPatient(entMan, map.MapCoords);
            recipient = SpawnPatient(entMan, map.MapCoords, GenitalProfile.Empty.WithVagina(new VaginaProfile(VaginaHuman)));

            organ = GenitalOrgan(entMan, donor, GenitalSlot.Penis) ?? EntityUid.Invalid;
            if (organ.IsValid())
                donorState = entMan.GetComponent<GenitalOrganComponent>(organ).State;

            // Transplanted tissue keeps its colour, so give the recipient a different skin.
            var humanoid = entMan.GetComponent<HumanoidAppearanceComponent>(recipient);
            humanoid.SkinColor = recipientSkin;
            entMan.Dirty(recipient, humanoid);
        });

        await server.WaitAssertion(() =>
        {
            Assert.That(organ.IsValid(), "The donor has no penis organ.");
            Assert.Multiple(() =>
            {
                Assert.That(donorState.Sheath, Is.EqualTo(SheathType.Sheath), "Precondition: FullProfile gives the penis a sheath.");
                Assert.That(donorState.Color, Is.Not.EqualTo(recipientSkin), "Precondition: the donor tissue differs from the recipient's skin.");
                Assert.That(entMan.GetComponent<GenitalsComponent>(donor).Penis, Is.EqualTo(donorState), "Precondition: the donor's mirror shows the penis.");
            });

            var torso = RootPart(entMan, donor)!.Value;
            Assert.That(IsSurgeryValid(entMan, donor, torso, RemovePenis, StepRemoveOrgan, surgeon), Is.True, "The removal step was refused.");
            PerformStep(entMan, surgeon, donor, torso, RemovePenis, StepRemoveOrgan);
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var genitals = entMan.GetComponent<GenitalsComponent>(donor);
            Assert.Multiple(() =>
            {
                Assert.That(entMan.EntityExists(organ), "A surgically removed organ of an opted-in body was deleted.");
                Assert.That(entMan.GetComponent<OrganComponent>(organ).Body, Is.Null, "The penis is still in a body.");
                Assert.That(genitals.Penis, Is.Null, "The mirror still shows the removed penis and its sheath.");
                Assert.That(genitals.Testicles, Is.Not.Null, "Removing the penis cleared the testicles.");
                Assert.That(entMan.GetComponent<GenitalOrganComponent>(organ).State, Is.EqualTo(donorState), "The organ lost its state.");
            });

            var torso = RootPart(entMan, recipient)!.Value;
            Assert.That(entMan.GetComponent<GenitalsComponent>(recipient).Penis, Is.Null, "Precondition: the recipient has no penis.");
            Assert.That(IsSurgeryValid(entMan, recipient, torso, InsertPenis, StepInsertOrgan, surgeon), Is.True, "The insertion step was refused.");
            PerformStep(entMan, surgeon, recipient, torso, InsertPenis, StepInsertOrgan, organ);
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var genitals = entMan.GetComponent<GenitalsComponent>(recipient);
            Assert.Multiple(() =>
            {
                Assert.That(entMan.GetComponent<OrganComponent>(organ).Body, Is.EqualTo(recipient), "The penis is not in the recipient.");
                Assert.That(genitals.Penis, Is.EqualTo(donorState), "The recipient's mirror must carry the donor organ's state.");
                Assert.That(genitals.Penis?.Color, Is.EqualTo(donorState.Color), "The donor's colour must be kept.");
                Assert.That(genitals.Vagina, Is.Not.Null, "The insertion cleared the recipient's own organ.");
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Removing one arousable organ while another remains keeps arousal; removing the last one resets it to 0.</summary>
    [Test]
    public async Task LastArousableOrganRemovedTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        EntityUid surgeon = default;
        EntityUid patient = default;
        await server.WaitPost(() =>
        {
            surgeon = SpawnSurgeon(entMan, map.GridCoords);
            patient = SpawnPatient(entMan, map.MapCoords);
            var genitals = entMan.GetComponent<GenitalsComponent>(patient);
            genitals.Arousal = 80;
            entMan.Dirty(patient, genitals);
        });

        await server.WaitAssertion(() =>
        {
            var torso = RootPart(entMan, patient)!.Value;
            Assert.That(IsSurgeryValid(entMan, patient, torso, RemovePenis, StepRemoveOrgan, surgeon), Is.True, "The penis removal was refused.");
            PerformStep(entMan, surgeon, patient, torso, RemovePenis, StepRemoveOrgan);
        });

        await pair.RunTicksSync(2);
        await server.WaitAssertion(() =>
        {
            var genitals = entMan.GetComponent<GenitalsComponent>(patient);
            Assert.That(genitals.Vagina, Is.Not.Null, "Precondition: the vagina remains.");
            Assert.That(genitals.Arousal, Is.EqualTo(80), "Arousal must stay while an arousable organ remains.");

            var torso = RootPart(entMan, patient)!.Value;
            Assert.That(IsSurgeryValid(entMan, patient, torso, RemoveVagina, StepRemoveOrgan, surgeon), Is.True, "The vagina removal was refused.");
            PerformStep(entMan, surgeon, patient, torso, RemoveVagina, StepRemoveOrgan);
        });

        await pair.RunTicksSync(2);
        await server.WaitAssertion(() =>
            Assert.That(entMan.GetComponent<GenitalsComponent>(patient).Arousal, Is.EqualTo(0), "Removing the last arousable organ must reset arousal."));

        await pair.CleanReturnAsync();
    }

    /// <summary>Without the patient's master switch the anatomy surgeries are not listed and their steps fail, for other surgeons and for the patient; a minor never gets them.</summary>
    [Test]
    public async Task PatientWithoutMasterTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var surgeon = SpawnSurgeon(entMan, map.GridCoords);
            var patient = SpawnPatient(entMan, map.MapCoords);
            var torso = RootPart(entMan, patient)!.Value;
            Assert.That(Procedures.All(p => GenitalOrgan(entMan, patient, p.Slot) != null), "Precondition: FullProfile builds every organ.");

            Assert.Multiple(() =>
            {
                foreach (var procedure in Procedures)
                {
                    Assert.That(IsListed(entMan, patient, torso, procedure.Remove), Is.True, $"{procedure.Remove} must be listed for an opted-in patient.");
                }

                Assert.That(IsSurgeryValid(entMan, patient, torso, RemovePenis, StepRemoveOrgan, surgeon), Is.True, "Control: the step passes with consent.");
            });

            // Other toggles stay on; only the master switch goes off.
            SetConsent(entMan, patient, SurgeryToggle);
            Assert.Multiple(() =>
            {
                foreach (var procedure in Procedures)
                {
                    Assert.That(IsListed(entMan, patient, torso, procedure.Remove), Is.False, $"{procedure.Remove} is listed for a patient without adult content.");
                }

                Assert.That(IsSurgeryValid(entMan, patient, torso, RemovePenis, StepRemoveOrgan, surgeon), Is.False, "Another surgeon's step passed.");
                Assert.That(IsSurgeryValid(entMan, patient, torso, RemovePenis, StepRemoveOrgan, patient), Is.False, "The patient's own step passed.");
                Assert.That(GenitalOrgan(entMan, patient, GenitalSlot.Penis), Is.Not.Null, "The organs stay in the body while the switch is off.");
            });

            // A minor gets no anatomy and no slots, even with every toggle on.
            var minor = entMan.SpawnEntity(GenitalConsentTestHelpers.HumanMob, map.GridCoords);
            var humanoid = entMan.GetComponent<HumanoidAppearanceComponent>(minor);
            humanoid.Age = 17;
            entMan.Dirty(minor, humanoid);
            GrantConsent(entMan, minor, StripToggle, SurgeryToggle);
            GenitalConsentTestHelpers.SetSsd(entMan, minor, false);
            var minorTorso = RootPart(entMan, minor)!.Value;
            Assert.That(entMan.HasComponent<GenitalsComponent>(minor), Is.False, "Precondition: a minor has no anatomy.");
            Assert.Multiple(() =>
            {
                foreach (var procedure in Procedures)
                {
                    Assert.That(IsListed(entMan, minor, minorTorso, procedure.Insert), Is.False, $"{procedure.Insert} is listed for a minor.");
                    Assert.That(IsListed(entMan, minor, minorTorso, procedure.Remove), Is.False, $"{procedure.Remove} is listed for a minor.");
                }
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>An SSD patient keeps the list entries (the list is per body), but another surgeon's steps are refused until the player is back.</summary>
    [Test]
    public async Task SsdPatientTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var surgeon = SpawnSurgeon(entMan, map.GridCoords);
            var patient = SpawnPatient(entMan, map.MapCoords);
            var torso = RootPart(entMan, patient)!.Value;

            GenitalConsentTestHelpers.SetSsd(entMan, patient, true);
            Assert.Multiple(() =>
            {
                Assert.That(IsListed(entMan, patient, torso, RemovePenis), Is.True, "The per-body list only needs the patient's master switch.");
                Assert.That(IsSurgeryValid(entMan, patient, torso, RemovePenis, StepRemoveOrgan, surgeon), Is.False, "A step on an SSD patient passed.");
            });

            GenitalConsentTestHelpers.SetSsd(entMan, patient, false);
            Assert.That(IsSurgeryValid(entMan, patient, torso, RemovePenis, StepRemoveOrgan, surgeon), Is.True, "The step must pass once the patient is back.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The server refuses a surgeon without adult content, under 18 or not humanoid, whatever the patient allows; other surgeries are unaffected.</summary>
    [TestCase(SurgeonCase.MasterOff)]
    [TestCase(SurgeonCase.Minor)]
    [TestCase(SurgeonCase.NotHumanoid)]
    public async Task RefusedSurgeonTest(SurgeonCase surgeonCase)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var patient = SpawnPatient(entMan, map.MapCoords);
            var torso = RootPart(entMan, patient)!.Value;

            EntityUid surgeon;
            switch (surgeonCase)
            {
                case SurgeonCase.MasterOff:
                    surgeon = SpawnSurgeon(entMan, map.GridCoords, master: false);
                    break;
                case SurgeonCase.Minor:
                    surgeon = SpawnSurgeon(entMan, map.GridCoords, age: 17);
                    break;
                default:
                    surgeon = entMan.SpawnEntity(NonHumanoidMob, map.GridCoords);
                    GrantConsent(entMan, surgeon);
                    break;
            }

            Assert.Multiple(() =>
            {
                Assert.That(IsListed(entMan, patient, torso, RemovePenis), Is.True, "Precondition: the patient's list offers the surgery.");
                Assert.That(IsSurgeryValid(entMan, patient, torso, RemovePenis, StepRemoveOrgan, surgeon), Is.False, $"A {surgeonCase} surgeon passed the anatomy check.");
                Assert.That(IsSurgeryValid(entMan, patient, torso, OpenIncision, StepOpenIncision, surgeon), Is.True, "The hook must not affect other surgeries.");
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Surgery by another player needs the patient's AnatomySurgery toggle; self-surgery needs only the master switch.</summary>
    [Test]
    public async Task OtherPlayerAndSelfSurgeryTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var surgeon = SpawnSurgeon(entMan, map.GridCoords);
            var patient = SpawnPatient(entMan, map.MapCoords, surgeryConsent: false);
            var torso = RootPart(entMan, patient)!.Value;

            Assert.Multiple(() =>
            {
                Assert.That(IsSurgeryValid(entMan, patient, torso, RemovePenis, StepRemoveOrgan, surgeon), Is.False, "Another player operated without AnatomySurgery.");
                Assert.That(IsSurgeryValid(entMan, patient, torso, RemovePenis, StepRemoveOrgan, patient), Is.True, "Self-surgery must need only the master switch.");
            });

            GrantConsent(entMan, patient, SurgeryToggle);
            Assert.That(IsSurgeryValid(entMan, patient, torso, RemovePenis, StepRemoveOrgan, surgeon), Is.True, "Another player must be allowed with AnatomySurgery.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>With wf.anatomy_enabled off no anatomy surgery is listed or allowed, even with every toggle on.</summary>
    [Test]
    public async Task KillSwitchTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        EntityUid surgeon = default;
        EntityUid patient = default;
        EntityUid torso = default;
        await server.WaitPost(() =>
        {
            surgeon = SpawnSurgeon(entMan, map.GridCoords);
            patient = SpawnPatient(entMan, map.MapCoords);
            torso = RootPart(entMan, patient)!.Value;
        });

        try
        {
            await server.WaitAssertion(() =>
                Assert.That(IsSurgeryValid(entMan, patient, torso, RemovePenis, StepRemoveOrgan, surgeon), Is.True, "Control: allowed with the switch on."));

            await server.WaitPost(() => server.CfgMan.SetCVar(WolfgateCVars.AnatomyEnabled, false));
            await server.WaitAssertion(() =>
            {
                Assert.Multiple(() =>
                {
                    Assert.That(IsListed(entMan, patient, torso, RemovePenis), Is.False, "Listed with the kill switch off.");
                    Assert.That(IsSurgeryValid(entMan, patient, torso, RemovePenis, StepRemoveOrgan, surgeon), Is.False, "Another surgeon passed with the kill switch off.");
                    Assert.That(IsSurgeryValid(entMan, patient, torso, RemovePenis, StepRemoveOrgan, patient), Is.False, "Self-surgery passed with the kill switch off.");
                });
            });
        }
        finally
        {
            await server.WaitPost(() => server.CfgMan.SetCVar(WolfgateCVars.AnatomyEnabled, true));
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>An accepted procedure is logged once although the hook runs on both paths; a refused surgeon is not logged.</summary>
    [Test]
    public async Task ProcedureAdminLogTest()
    {
        // A real round so the logs are stored. No connected client: its player would spawn as a random
        // species, and that species' unrelated client errors could fail this test.
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            AdminLogsEnabled = true,
            DummyTicker = false,
        });
        var server = pair.Server;
        var entMan = server.EntMan;
        var adminLog = server.ResolveDependency<IAdminLogManager>();
        var map = await pair.CreateTestMap();

        EntityUid surgeon = default;
        EntityUid refused = default;
        var firstCheck = false;
        var secondCheck = false;
        var refusedCheck = true;
        await server.WaitPost(() =>
        {
            surgeon = SpawnSurgeon(entMan, map.GridCoords);
            refused = SpawnSurgeon(entMan, map.GridCoords, master: false);
            var patient = SpawnPatient(entMan, map.MapCoords);
            var torso = RootPart(entMan, patient)!.Value;

            // The step being chosen, then its DoAfter completing: two checks, one procedure.
            firstCheck = IsSurgeryValid(entMan, patient, torso, RemovePenis, StepRemoveOrgan, surgeon);
            secondCheck = IsSurgeryValid(entMan, patient, torso, RemovePenis, StepRemoveOrgan, surgeon);
            refusedCheck = IsSurgeryValid(entMan, patient, torso, RemovePenis, StepRemoveOrgan, refused);
        });

        Assert.Multiple(() =>
        {
            Assert.That(firstCheck && secondCheck, "The accepted surgeon was refused.");
            Assert.That(refusedCheck, Is.False, "The surgeon without adult content was accepted.");
        });

        // ToPrettyString writes "name (uid/nNetEntity, prototype)".
        var surgeonTag = $"({surgeon}/n";
        var refusedTag = $"({refused}/n";
        List<SharedAdminLog> logs = new();
        await PoolManager.WaitUntil(server, async () =>
        {
            logs = await adminLog.CurrentRoundLogs(new LogFilter { Types = new HashSet<LogType> { LogType.WFAnatomy } });
            return logs.Any(l => l.Message.Contains(surgeonTag) && l.Message.Contains("performs"));
        });

        Assert.Multiple(() =>
        {
            // Logs are written in order, so a second entry from the same tick would be there by now.
            var procedures = logs.Where(l => l.Message.Contains(surgeonTag) && l.Message.Contains("performs")).ToList();
            Assert.That(procedures, Has.Count.EqualTo(1), "The procedure must be logged once per surgeon, patient and surgery.");
            Assert.That(procedures[0].Impact, Is.EqualTo(LogImpact.Medium));
            Assert.That(procedures[0].Message, Does.Contain(RemovePenis));
            Assert.That(logs.Any(l => l.Message.Contains(refusedTag) && l.Message.Contains("performs")), Is.False, "A refused surgeon was logged as operating.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The client window filters the server's list once per state: without the viewer's adult content the choices every
    /// read uses hold no anatomy surgery, and switching it on or off refilters the open window.
    /// </summary>
    [Test]
    public async Task ClientSurgeryListTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;
        var sEntMan = server.EntMan;
        var viewer = client.System<ClientGenitalConsentSystem>();

        var player = await GenitalConsentTestHelpers.AttachToNewBody(pair);
        await GenitalConsentTestHelpers.SetPlayerConsent(pair);

        EntityUid patient = default;
        NetEntity netTorso = default;
        var offered = new List<EntProtoId>();
        await server.WaitPost(() =>
        {
            var coords = sEntMan.System<SharedTransformSystem>().ToMapCoordinates(player.Map.GridCoords.Offset(new Vector2(1f, 0f)));
            patient = SpawnPatient(sEntMan, coords);
            var torso = RootPart(sEntMan, patient)!.Value;
            netTorso = sEntMan.GetNetEntity(torso);

            // The torso list as the server builds it (SurgerySystem.RefreshUI), from a plain surgery and every anatomy one.
            var candidates = new List<string> { OpenIncision };
            candidates.AddRange(Procedures.SelectMany(p => new[] { p.Remove, p.Insert }));
            foreach (var surgery in candidates)
            {
                if (IsListed(sEntMan, patient, torso, surgery))
                    offered.Add(surgery);
            }

            var choices = new Dictionary<NetEntity, List<EntProtoId>> { [netTorso] = offered };
            var ui = sEntMan.System<SharedUserInterfaceSystem>();
            ui.OpenUi(patient, SurgeryUIKey.Key, player.Body);
            ui.SetUiState(patient, SurgeryUIKey.Key, new SurgeryBuiState(choices));
        });

        var plainOnly = new List<EntProtoId> { OpenIncision };
        Assert.Multiple(() =>
        {
            Assert.That(offered, Does.Contain(new EntProtoId(OpenIncision)), "Precondition: the server offers the plain surgery.");
            Assert.That(offered.Count(IsAnatomyId), Is.EqualTo(Procedures.Length), "Precondition: the server offers every removal.");
        });

        await pair.RunTicksSync(10);
        var cPatient = pair.ToClientUid(patient);
        await client.WaitAssertion(() =>
        {
            var choices = BuiChoices(client.EntMan, cPatient);
            Assert.That(choices.Keys, Is.EquivalentTo(new[] { netTorso }), "The torso must stay: it still offers a plain surgery.");
            Assert.That(choices[netTorso], Is.EquivalentTo(plainOnly), "A viewer without adult content was offered anatomy surgeries.");
        });

        // Turning adult content on refilters the open window from the stored state.
        await GenitalConsentTestHelpers.SetPlayerConsent(pair, MasterToggle);
        await client.WaitAssertion(() =>
            Assert.That(BuiChoices(client.EntMan, cPatient)[netTorso], Is.EquivalentTo(offered), "The window did not refilter when adult content came on."));

        // The server refuses a surgeon under 18 even with adult content on (CanOperate), so the filter offers them nothing.
        void SetSurgeonAge(int age)
        {
            var humanoid = sEntMan.GetComponent<HumanoidAppearanceComponent>(player.Body);
            humanoid.Age = age;
            sEntMan.Dirty(player.Body, humanoid);
        }

        var cSurgeon = pair.ToClientUid(player.Body);
        await server.WaitPost(() => SetSurgeonAge(17));
        await GenitalConsentTestHelpers.WaitFor(pair,
            () => client.EntMan.GetComponent<HumanoidAppearanceComponent>(cSurgeon).Age == 17,
            "the client to see the surgeon's new age");
        await client.WaitAssertion(() =>
        {
            var filtered = viewer.FilterSurgeryChoices(new Dictionary<NetEntity, List<EntProtoId>> { [netTorso] = offered });
            Assert.That(filtered[netTorso], Is.EquivalentTo(plainOnly), "A surgeon under 18 was offered anatomy surgeries.");
        });
        await server.WaitPost(() => SetSurgeonAge(30));

        await GenitalConsentTestHelpers.SetPlayerConsent(pair);
        await client.WaitAssertion(() =>
        {
            Assert.That(BuiChoices(client.EntMan, cPatient)[netTorso], Is.EquivalentTo(plainOnly), "The window did not refilter when adult content went off.");

            // A part that offers only anatomy surgeries is dropped; one with a plain surgery keeps just that.
            var anatomyOnly = new NetEntity(1);
            var mixed = new NetEntity(2);
            var filtered = viewer.FilterSurgeryChoices(new Dictionary<NetEntity, List<EntProtoId>>
            {
                [anatomyOnly] = new() { RemovePenis, InsertPenis },
                [mixed] = new() { OpenIncision, RemoveVagina },
            });
            Assert.That(filtered.Keys, Is.EquivalentTo(new[] { mixed }), "A part that offered only anatomy surgeries was kept.");
            Assert.That(filtered[mixed], Is.EquivalentTo(plainOnly));
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>No procedure-specific popup key exists for the anatomy surgeries, so bystanders only see the generic organ lines.</summary>
    [Test]
    public async Task NoProcedurePopupKeysTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var res = server.ResolveDependency<IResourceManager>();
        var loc = server.ResolveDependency<ILocalizationManager>();
        var protoMan = server.ProtoMan;
        var factory = server.ResolveDependency<IComponentFactory>();

        await server.WaitAssertion(() =>
        {
            var files = 0;
            var offenders = new List<string>();
            foreach (var path in res.ContentFindFiles(new ResPath("/Locale/en-US")))
            {
                if (!path.Filename.EndsWith(".ftl", StringComparison.OrdinalIgnoreCase))
                    continue;

                files++;
                using var reader = new StreamReader(res.ContentFileRead(path));
                var lineNumber = 0;
                while (reader.ReadLine() is { } line)
                {
                    lineNumber++;
                    if (line.TrimStart().StartsWith(ProcedurePopupPrefix, StringComparison.Ordinal))
                        offenders.Add($"{path}:{lineNumber}");
                }
            }

            Assert.Multiple(() =>
            {
                Assert.That(files, Is.GreaterThan(0), "No .ftl file was found under /Locale/en-US.");
                Assert.That(offenders, Is.Empty, $"Keys starting with {ProcedurePopupPrefix} would show procedure names to bystanders.");
                Assert.That(loc.HasString(RefusedKey), Is.True, $"{RefusedKey} is missing.");

                foreach (var surgery in Procedures.SelectMany(p => new[] { p.Remove, p.Insert }))
                {
                    if (!protoMan.Index<EntityPrototype>(surgery).TryGetComponent<SurgeryComponent>(out var comp, factory))
                    {
                        Assert.Fail($"{surgery} has no Surgery component.");
                        continue;
                    }

                    foreach (var step in comp.Steps)
                    {
                        Assert.That(loc.HasString($"surgery-popup-procedure-{surgery}-step-{step.Id}"), Is.False, $"{surgery} has a procedure popup for {step.Id}.");
                        Assert.That(loc.HasString($"surgery-popup-step-{step.Id}"), Is.True, $"The generic popup for {step.Id} is missing.");
                    }
                }
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Asserts the recipe, targeting and gates of one anatomy surgery prototype.</summary>
    private static void AssertSurgery(IPrototypeManager protoMan, IComponentFactory factory, string id, string marker,
        bool insert, string slotId, string[] steps)
    {
        if (!protoMan.TryIndex<EntityPrototype>(id, out var proto))
        {
            Assert.Fail($"Surgery {id} does not exist.");
            return;
        }

        Assert.That(proto.TryGetComponent<SurgeryComponent>(out var surgery, factory), $"{id} has no Surgery component.");
        Assert.That(surgery?.Requirement, Is.EqualTo((EntProtoId?) new EntProtoId(OpenIncision)), $"{id} must need the open incision.");
        Assert.That(surgery?.Steps.Select(s => s.Id), Is.EqualTo(steps), $"{id} must use the generic organ steps.");

        Assert.That(proto.TryGetComponent<SurgeryPartConditionComponent>(out var part, factory), $"{id} has no part condition.");
        Assert.That(part?.Part, Is.EqualTo(BodyPartType.Torso), $"{id} must target the torso.");
        Assert.That(part?.Inverse, Is.False, $"{id} must target the torso.");

        Assert.That(proto.TryGetComponent<SurgeryOrganConditionComponent>(out var organ, factory), $"{id} has no organ condition.");
        Assert.That(organ?.Organ?.Keys, Is.EquivalentTo(new[] { marker }), $"{id} must select the {marker} organ.");
        Assert.That(organ?.Inverse, Is.EqualTo(insert), $"{id} has the wrong organ condition direction.");
        Assert.That(organ?.Reattaching, Is.EqualTo(insert), $"{id} has the wrong reattaching flag.");

        var hasSlot = proto.TryGetComponent<SurgeryOrganSlotConditionComponent>(out var slot, factory);
        Assert.That(hasSlot, Is.EqualTo(insert), $"{id}: only insertions need an organ slot.");
        if (insert)
            Assert.That(slot?.OrganSlot, Is.EqualTo(slotId), $"{id} targets the wrong organ slot.");

        Assert.That(proto.HasComponent<SurgeryGenitalConsentConditionComponent>(factory), $"{id} lacks the patient consent condition.");
        Assert.That(proto.HasComponent<GenitalSurgeryComponent>(factory), $"{id} lacks the anatomy surgery marker.");
    }

    /// <summary>A present MobHuman surgeon (30 by default). With master, only the master switch is on; without it, the other anatomy toggles are.</summary>
    private static EntityUid SpawnSurgeon(IEntityManager entMan, EntityCoordinates coords, int age = 30, bool master = true)
    {
        var surgeon = GenitalConsentTestHelpers.SpawnPresentAdult(entMan, coords);
        var humanoid = entMan.GetComponent<HumanoidAppearanceComponent>(surgeon);
        humanoid.Age = age;
        entMan.Dirty(surgeon, humanoid);

        if (master)
            GrantConsent(entMan, surgeon);
        else
            SetConsent(entMan, surgeon, StripToggle, SurgeryToggle);

        return surgeon;
    }

    /// <summary>An opted-in adult MobHuman with anatomy (FullProfile by default), present and lying down, optionally allowing surgery by others.</summary>
    private static EntityUid SpawnPatient(IEntityManager entMan, MapCoordinates coords, GenitalProfile anatomy = null, bool surgeryConsent = true)
    {
        var patient = SpawnWithAnatomy(entMan, coords, anatomy);
        if (surgeryConsent)
            GrantConsent(entMan, patient, SurgeryToggle);

        // A mob without a player starts SSD.
        GenitalConsentTestHelpers.SetSsd(entMan, patient, false);
        entMan.System<StandingStateSystem>().Down(patient, playSound: false);
        return patient;
    }

    /// <summary>Whether the surgery would be listed for the part, as the server's per-body list (SurgerySystem.RefreshUI) decides.</summary>
    private static bool IsListed(IEntityManager entMan, EntityUid body, EntityUid part, string surgery)
    {
        var singleton = entMan.System<SharedSurgerySystem>().GetSingleton(surgery);
        Assert.That(singleton, Is.Not.Null, $"{surgery} has no singleton.");

        var ev = new SurgeryValidEvent(body, part);
        entMan.EventBus.RaiseLocalEvent(singleton!.Value, ref ev);
        return !ev.Cancelled;
    }

    /// <summary>Calls the protected SharedSurgerySystem.IsSurgeryValid, which the step-chosen and the DoAfter paths both use. Lays the patient down first.</summary>
    private static bool IsSurgeryValid(IEntityManager entMan, EntityUid body, EntityUid part, string surgery, string step, EntityUid user)
    {
        entMan.System<StandingStateSystem>().Down(body, playSound: false);

        var method = typeof(SharedSurgerySystem).GetMethod("IsSurgeryValid", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null, "SharedSurgerySystem.IsSurgeryValid was not found.");

        var args = new object[] { body, part, new EntProtoId(surgery), new EntProtoId(step), user, null, null, null };
        return (bool) method!.Invoke(entMan.System<SharedSurgerySystem>(), args)!;
    }

    /// <summary>Runs one step as OnTargetDoAfter does once IsSurgeryValid passed: SurgeryStepEvent on the step singleton, with these tools.</summary>
    private static void PerformStep(IEntityManager entMan, EntityUid surgeon, EntityUid body, EntityUid part, string surgery, string step,
        params EntityUid[] tools)
    {
        var surgerySys = entMan.System<SharedSurgerySystem>();
        var surgeryEnt = surgerySys.GetSingleton(surgery);
        var stepEnt = surgerySys.GetSingleton(step);
        Assert.That(surgeryEnt, Is.Not.Null, $"{surgery} has no singleton.");
        Assert.That(stepEnt, Is.Not.Null, $"{step} has no singleton.");

        var ev = new SurgeryStepEvent(surgeon, body, part, tools.ToList(), surgeryEnt!.Value);
        entMan.EventBus.RaiseLocalEvent(stepEnt!.Value, ref ev);
    }

    /// <summary>The filtered choices (the WOLFGATE _choices field) of the client's open surgery window on the patient.</summary>
    private static Dictionary<NetEntity, List<EntProtoId>> BuiChoices(IEntityManager clientEntMan, EntityUid patient)
    {
        var ui = clientEntMan.System<SharedUserInterfaceSystem>();
        Assert.That(ui.TryGetOpenUi<SurgeryBui>(patient, SurgeryUIKey.Key, out var bui), "The client has no open surgery window.");

        var field = typeof(SurgeryBui).GetField("_choices", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, "SurgeryBui._choices was not found.");
        return (Dictionary<NetEntity, List<EntProtoId>>) field!.GetValue(bui);
    }

    private static bool IsAnatomyId(EntProtoId id)
    {
        return id.Id.StartsWith("SurgeryWF", StringComparison.Ordinal);
    }
}
