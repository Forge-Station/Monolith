using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Client._Common.Consent;
using Content.Client._WF.Genitals;
using Content.IntegrationTests.Pair;
using Content.Server._Common.Consent;
using Content.Server._WF.Genitals;
using Content.Server.GameTicking;
using Content.Shared._Common.Consent;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.Genitals;
using Content.Shared._WF.Genitals.Components;
using Content.Shared._WF.Genitals.Prototypes;
using Content.Shared._WF.Genitals.Systems;
using Content.Shared.Humanoid;
using Content.Shared.Mind;
using Content.Shared.Players;
using Content.Shared.SSDIndicator;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using static Content.IntegrationTests.Tests._WF.Genitals.GenitalTestHelpers;

namespace Content.IntegrationTests.Tests._WF.Genitals;

/// <summary>Strict consent, presence, the kill switch, the settings wiring and the client viewer gate.</summary>
[TestFixture]
[TestOf(typeof(GenitalConsentSystem))]
public sealed class GenitalConsentGateTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: WFTestAnatomySurgery
  name: anatomy surgery test dummy
  components:
  - type: GenitalSurgery

- type: entity
  id: WFTestPlainSurgery
  name: plain surgery test dummy
";

    // Plain strings: the YAML linter validates static prototype-id fields and does not load test prototypes.
    private const string AnatomySurgeryProto = "WFTestAnatomySurgery";
    private const string PlainSurgeryProto = "WFTestPlainSurgery";

    /// <summary>A humanoid mob without SSDIndicatorComponent.</summary>
    private const string MonkeyMob = "MobMonkey";

    /// <summary>
    /// No ConsentComponent means no; an SSD body keeps its toggles but fails TargetConsents; a body without
    /// SSDIndicatorComponent counts as absent.
    /// </summary>
    [Test]
    public async Task StrictConsentTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var consent = entMan.System<GenitalConsentSystem>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = entMan.SpawnEntity(GenitalConsentTestHelpers.HumanMob, map.GridCoords);
            Assert.That(entMan.HasComponent<ConsentComponent>(body), Is.False,
                "Precondition: a mob that was never possessed has no ConsentComponent.");
            Assert.Multiple(() =>
            {
                Assert.That(consent.HasStrictConsent(body, MasterToggle), Is.False, "No ConsentComponent must mean no.");
                Assert.That(consent.HasMaster(body), Is.False);
                Assert.That(consent.TargetConsents(body), Is.False);
            });

            GrantConsent(entMan, body);
            GenitalConsentTestHelpers.SetSsd(entMan, body, true);
            Assert.Multiple(() =>
            {
                Assert.That(consent.HasMaster(body), Is.True, "An SSD body keeps its toggles.");
                Assert.That(consent.IsActivelyPresent(body), Is.False);
                Assert.That(consent.TargetConsents(body), Is.False, "An SSD body must fail TargetConsents.");
            });

            GenitalConsentTestHelpers.SetSsd(entMan, body, false);
            Assert.That(consent.TargetConsents(body), Is.True, "A present body with the master switch consents.");

            // Monkeys and kobolds have no SSD indicator, so their presence is unknown.
            var monkey = entMan.SpawnEntity(MonkeyMob, map.GridCoords);
            GrantConsent(entMan, monkey);
            Assert.Multiple(() =>
            {
                Assert.That(entMan.HasComponent<SSDIndicatorComponent>(monkey), Is.False, "Precondition: a monkey has no SSD indicator.");
                Assert.That(consent.HasMaster(monkey), Is.True, "Precondition: the monkey has the master switch.");
                Assert.That(consent.IsActivelyPresent(monkey), Is.False, "A body without SSDIndicatorComponent must count as absent.");
                Assert.That(consent.TargetConsents(monkey), Is.False);
            });

            entMan.DeleteEntity(monkey);
            entMan.DeleteEntity(body);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>With wf.anatomy_enabled off every gate fails, even when every toggle is on.</summary>
    [Test]
    public async Task KillSwitchTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var consent = entMan.System<GenitalConsentSystem>();
        var map = await pair.CreateTestMap();

        EntityUid actor = default;
        EntityUid target = default;
        await server.WaitPost(() =>
        {
            actor = GenitalConsentTestHelpers.SpawnPresentAdult(entMan, map.GridCoords);
            target = GenitalConsentTestHelpers.SpawnPresentAdult(entMan, map.GridCoords);
            GrantConsent(entMan, actor);
            GrantConsent(entMan, target, StripToggle, SurgeryToggle);
        });

        try
        {
            await server.WaitAssertion(() => AssertGates(consent, actor, target, true));
            await server.WaitPost(() => server.CfgMan.SetCVar(WolfgateCVars.AnatomyEnabled, false));
            await server.WaitAssertion(() => AssertGates(consent, actor, target, false));
        }
        finally
        {
            await server.WaitPost(() => server.CfgMan.SetCVar(WolfgateCVars.AnatomyEnabled, true));
        }

        await server.WaitPost(() =>
        {
            entMan.DeleteEntity(actor);
            entMan.DeleteEntity(target);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The Default settings name both anatomy toggles; both exist on each side and require the master switch.</summary>
    [Test]
    public async Task SettingsWiredTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        ProtoId<ConsentTogglePrototype> strip = StripToggle;
        ProtoId<ConsentTogglePrototype> surgery = SurgeryToggle;

        foreach (var protoMan in new[] { pair.Server.ProtoMan, pair.Client.ProtoMan })
        {
            var settings = protoMan.Index(GenitalSettingsPrototype.DefaultId);
            Assert.Multiple(() =>
            {
                Assert.That(settings.StripConsent, Is.EqualTo(strip), "stripConsent must name UndergarmentStrip.");
                Assert.That(settings.SurgeryConsent, Is.EqualTo(surgery), "surgeryConsent must name AnatomySurgery.");
                Assert.That(protoMan.TryIndex(strip, out var stripProto), Is.True, "UndergarmentStrip must exist.");
                Assert.That(protoMan.TryIndex(surgery, out var surgeryProto), Is.True, "AnatomySurgery must exist.");
                Assert.That(stripProto?.Requires, Is.EqualTo(settings.MasterConsent), "UndergarmentStrip must require the master switch.");
                Assert.That(surgeryProto?.Requires, Is.EqualTo(settings.MasterConsent), "AnatomySurgery must require the master switch.");
                Assert.That(protoMan.Index(settings.MasterConsent).Requires, Is.Null, "The master switch requires nothing.");
            });
        }

        await pair.CleanReturnAsync();
    }

    /// <summary>A real player's body: detached (SSD) it keeps its toggles but fails TargetConsents; ghosted it keeps none.</summary>
    [Test]
    public async Task PlayerPresenceTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var mindSys = entMan.System<SharedMindSystem>();
        var consent = entMan.System<GenitalConsentSystem>();

        Assert.Multiple(() =>
        {
            Assert.That(server.System<GenitalConsentSystem>(), Is.TypeOf<ServerGenitalConsentSystem>());
            Assert.That(pair.Client.System<GenitalConsentSystem>(), Is.TypeOf<ClientGenitalConsentSystem>());
        });

        var player = await GenitalConsentTestHelpers.AttachToNewBody(pair);
        var body = player.Body;
        await GenitalConsentTestHelpers.SetPlayerConsent(pair, MasterToggle, StripToggle, SurgeryToggle);

        EntityUid actor = default;
        await server.WaitAssertion(() =>
        {
            actor = GenitalConsentTestHelpers.SpawnPresentAdult(entMan, player.Map.GridCoords);
            GrantConsent(entMan, actor);
            Assert.Multiple(() =>
            {
                Assert.That(consent.HasMaster(body), Is.True, "The saved master switch did not reach the attached body.");
                Assert.That(consent.TargetConsents(body), Is.True);
                Assert.That(consent.CanStrip(actor, body), Is.True);
                Assert.That(consent.CanOperate(actor, body), Is.True);
            });
        });

        // The session leaves the body; the mind and the toggles stay, as on a disconnect.
        await server.WaitPost(() => Detach(server.PlayerMan, player.Session));
        await pair.RunTicksSync(2);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<SSDIndicatorComponent>(body).IsSSD, Is.True, "Precondition: the detached body is SSD.");
            Assert.Multiple(() =>
            {
                Assert.That(consent.HasMaster(body), Is.True, "An SSD body keeps its toggles.");
                Assert.That(consent.TargetConsents(body), Is.False, "An SSD body must fail TargetConsents.");
                Assert.That(consent.CanStrip(actor, body), Is.False);
                Assert.That(consent.CanOperate(actor, body), Is.False);
                Assert.That(consent.CanExamine(actor, body), Is.False);
            });
        });

        await server.WaitPost(() => server.PlayerMan.SetAttachedEntity(player.Session, body));
        await pair.RunTicksSync(2);
        await server.WaitAssertion(() =>
            Assert.That(consent.TargetConsents(body), Is.True, "Reattaching the session restores presence."));

        // The mind moves to a ghost: ConsentSystem clears the body's toggles.
        await server.WaitAssertion(() =>
        {
            var ghost = entMan.SpawnEntity(GameTicker.ObserverPrototypeName, entMan.GetComponent<TransformComponent>(body).Coordinates);
            mindSys.TransferTo(player.Mind, ghost);
            Assert.Multiple(() =>
            {
                Assert.That(consent.HasStrictConsent(body, MasterToggle), Is.False, "A ghosted body keeps no toggles.");
                Assert.That(consent.HasMaster(body), Is.False);
                Assert.That(consent.TargetConsents(body), Is.False);
                Assert.That(consent.CanStrip(actor, body), Is.False);
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The client gate: with the viewer's master switch, preview dolls and the own body pass and other bodies need
    /// TargetConsents. Without it, or with the kill switch off, nothing passes.
    /// </summary>
    [Test]
    public async Task ViewerGateTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;
        var sEntMan = server.EntMan;
        var cEntMan = client.EntMan;
        var viewer = client.System<ClientGenitalConsentSystem>();

        var player = await GenitalConsentTestHelpers.AttachToNewBody(pair);
        await GenitalConsentTestHelpers.SetPlayerConsent(pair, MasterToggle);

        // An opted-in body beside the player, SSD as any mob without a player is.
        EntityUid target = default;
        await server.WaitPost(() =>
        {
            target = sEntMan.SpawnEntity(GenitalConsentTestHelpers.HumanMob, player.Map.GridCoords.Offset(new Vector2(1f, 0f)));
            GrantConsent(sEntMan, target);
            GenitalConsentTestHelpers.SetSsd(sEntMan, target, true);
        });
        await pair.RunTicksSync(5);

        var clientBody = pair.ToClientUid(player.Body);
        var clientTarget = pair.ToClientUid(target);
        EntityUid doll = default;
        EntityUid plain = default;
        await client.WaitPost(() =>
        {
            doll = cEntMan.SpawnEntity(null, MapCoordinates.Nullspace);
            cEntMan.EnsureComponent<GenitalsComponent>(doll).IsPreview = true;
            plain = cEntMan.SpawnEntity(null, MapCoordinates.Nullspace);
        });

        NetEntity part = default;
        await client.WaitAssertion(() =>
        {
            Assert.That(client.AttachedEntity, Is.EqualTo(clientBody), "Precondition: the client controls the body.");
            Assert.That(cEntMan.EntityExists(clientTarget), Is.True, "Precondition: the client sees the target.");
            part = cEntMan.GetNetEntity(clientBody);
            Assert.Multiple(() =>
            {
                Assert.That(viewer.ViewerHasMaster(), Is.True);
                Assert.That(viewer.CanViewerSee(clientBody), Is.True, "Own body.");
                Assert.That(viewer.CanViewerSee(doll), Is.True, "Preview doll.");
                Assert.That(viewer.CanViewerSee(plain), Is.False, "A client-side entity that is not a preview doll.");
                Assert.That(viewer.CanViewerSee(clientTarget), Is.False, "An SSD target.");
                Assert.That(FilterOne(viewer, part), Is.EquivalentTo(new EntProtoId[] { PlainSurgeryProto, AnatomySurgeryProto }),
                    "A viewer with adult content keeps anatomy surgeries.");
            });
        });

        // The target reconnects, then disconnects again.
        await server.WaitPost(() => GenitalConsentTestHelpers.SetSsd(sEntMan, target, false));
        await pair.RunTicksSync(5);
        await client.WaitAssertion(() =>
            Assert.That(viewer.CanViewerSee(clientTarget), Is.True, "A present, opted-in target."));

        await server.WaitPost(() => GenitalConsentTestHelpers.SetSsd(sEntMan, target, true));
        await pair.RunTicksSync(5);
        await client.WaitAssertion(() =>
            Assert.That(viewer.CanViewerSee(clientTarget), Is.False, "The target went SSD."));

        // Present, but opted out.
        await server.WaitPost(() =>
        {
            GenitalConsentTestHelpers.SetSsd(sEntMan, target, false);
            RevokeConsent(sEntMan, target);
        });
        await pair.RunTicksSync(5);
        await client.WaitAssertion(() =>
            Assert.That(viewer.CanViewerSee(clientTarget), Is.False, "A target without the master switch."));

        try
        {
            await server.WaitPost(() => server.CfgMan.SetCVar(WolfgateCVars.AnatomyEnabled, false));
            await pair.RunTicksSync(5);
            await client.WaitAssertion(() =>
            {
                Assert.Multiple(() =>
                {
                    Assert.That(viewer.AnatomyEnabled, Is.False, "The kill switch must replicate.");
                    Assert.That(viewer.CanViewerSee(clientBody), Is.False, "Own body with the kill switch off.");
                    Assert.That(viewer.CanViewerSee(doll), Is.False, "Preview doll with the kill switch off.");
                });
            });
        }
        finally
        {
            await server.WaitPost(() => server.CfgMan.SetCVar(WolfgateCVars.AnatomyEnabled, true));
            await pair.RunTicksSync(5);
        }

        // The viewer turns the master switch off.
        await GenitalConsentTestHelpers.SetPlayerConsent(pair);
        await client.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(viewer.ViewerHasMaster(), Is.False);
                Assert.That(viewer.CanViewerSee(clientBody), Is.False, "Own body without the viewer's master switch.");
                Assert.That(viewer.CanViewerSee(doll), Is.False, "Preview doll without the viewer's master switch.");
                Assert.That(FilterOne(viewer, part), Is.EquivalentTo(new EntProtoId[] { PlainSurgeryProto }),
                    "Anatomy surgeries are hidden from a viewer without adult content.");
            });
        });

        await client.WaitPost(() =>
        {
            cEntMan.DeleteEntity(doll);
            cEntMan.DeleteEntity(plain);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A viewer playing a humanoid under the adult age sees no anatomy, not even its own body or a preview doll. A
    /// viewer without a humanoid body (a ghost) follows its master switch, as ghost examiners do. Sprites follow at once.
    /// </summary>
    [Test]
    public async Task ViewerAdultGateTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;
        var sEntMan = server.EntMan;
        var cEntMan = client.EntMan;
        var viewer = client.System<ClientGenitalConsentSystem>();
        var sprites = client.System<SpriteSystem>();

        var player = await GenitalConsentTestHelpers.AttachToNewBody(pair);
        await GenitalConsentTestHelpers.SetPlayerConsent(pair, MasterToggle);

        // A present, opted-in adult beside the player. It wears nothing and reveals on clothing removal, so it is exposed.
        EntityUid target = default;
        await server.WaitPost(() =>
        {
            var coords = new MapCoordinates(player.Map.MapCoords.Position + new Vector2(1f, 0f), player.Map.MapCoords.MapId);
            target = SpawnWithAnatomy(sEntMan, coords, FullProfile().WithRevealMode(GenitalRevealMode.ClothingRemoval));
            GenitalConsentTestHelpers.SetSsd(sEntMan, target, false);
        });
        await pair.RunTicksSync(5);

        var clientBody = pair.ToClientUid(player.Body);
        var clientTarget = pair.ToClientUid(target);
        EntityUid doll = default;
        await client.WaitPost(() =>
        {
            doll = cEntMan.SpawnEntity(null, MapCoordinates.Nullspace);
            cEntMan.EnsureComponent<GenitalsComponent>(doll).IsPreview = true;
        });

        await client.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(viewer.ViewerIsAdult(), Is.True, "Precondition: the viewer's body is 30.");
                Assert.That(viewer.CanViewerSee(clientTarget), Is.True, "An adult viewer sees a present, opted-in target.");
                Assert.That(viewer.CanViewerSee(clientBody), Is.True, "An adult viewer's own body.");
                Assert.That(viewer.CanViewerSee(doll), Is.True, "A preview doll for an adult viewer.");
                Assert.That(AnyAnatomyDrawn(sprites, cEntMan, clientTarget), Is.True, "Precondition: the target's anatomy is drawn.");
            });
        });

        // The viewer's body becomes 17.
        await server.WaitPost(() =>
        {
            var humanoid = sEntMan.GetComponent<HumanoidAppearanceComponent>(player.Body);
            humanoid.Age = 17;
            sEntMan.Dirty(player.Body, humanoid);
        });
        await GenitalConsentTestHelpers.WaitFor(pair,
            () => cEntMan.GetComponent<HumanoidAppearanceComponent>(clientBody).Age == 17,
            "the client to see its body's new age");
        await pair.RunTicksSync(2);

        await client.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(viewer.ViewerHasMaster(), Is.True, "Precondition: the master switch is still on.");
                Assert.That(viewer.ViewerIsAdult(), Is.False);
                Assert.That(viewer.CanViewerSee(clientTarget), Is.False, "A minor viewer must not see another body's anatomy.");
                Assert.That(viewer.CanViewerSee(clientBody), Is.False, "A minor viewer's own body.");
                Assert.That(viewer.CanViewerSee(doll), Is.False, "A preview doll for a minor viewer.");
                Assert.That(AnyAnatomyDrawn(sprites, cEntMan, clientTarget), Is.False,
                    "The target's sprite must follow the viewer's age without any other change.");
            });
        });

        // The player ghosts. An observer has no humanoid body, so only its master switch counts.
        EntityUid ghost = default;
        await server.WaitPost(() =>
        {
            ghost = sEntMan.SpawnEntity(GameTicker.ObserverPrototypeName, player.Map.GridCoords);
            sEntMan.System<SharedMindSystem>().Visit(player.Mind, ghost);
        });
        await pair.RunTicksSync(5);

        var clientGhost = pair.ToClientUid(ghost);
        await client.WaitAssertion(() =>
        {
            Assert.That(client.AttachedEntity, Is.EqualTo(clientGhost), "Precondition: the client controls the ghost.");
            Assert.Multiple(() =>
            {
                Assert.That(viewer.ViewerIsAdult(), Is.True, "A viewer without a humanoid body passes the adult gate.");
                Assert.That(viewer.CanViewerSee(clientTarget), Is.True, "A ghost with adult content sees a present, opted-in target.");
                Assert.That(AnyAnatomyDrawn(sprites, cEntMan, clientTarget), Is.True, "The target's sprite follows the change of viewer.");
            });
        });

        await client.WaitPost(() => cEntMan.DeleteEntity(doll));
        await pair.CleanReturnAsync();
    }

    /// <summary>Whether any keyed anatomy layer of the body is drawn for the local viewer. Client thread only.</summary>
    private static bool AnyAnatomyDrawn(SpriteSystem sprites, IEntityManager entMan, EntityUid body)
    {
        var sprite = entMan.GetComponent<SpriteComponent>(body);
        foreach (var key in AnatomyLayerKeys)
        {
            if (sprites.TryGetLayer((body, sprite), key, out var layer, false) && layer.Visible)
                return true;
        }

        return false;
    }

    /// <summary>The visualizer's keyed layers.</summary>
    private static readonly string[] AnatomyLayerKeys =
    {
        "wf-gen-behind-breasts", "wf-gen-behind-testicles", "wf-gen-behind-penis",
        "wf-gen-under-vagina", "wf-gen-under-testicles", "wf-gen-under-breasts",
        "wf-gen-under-sheath-outer", "wf-gen-under-sheath-inner", "wf-gen-under-penis",
        "wf-gen-over-vagina", "wf-gen-over-testicles", "wf-gen-over-breasts",
        "wf-gen-over-sheath-outer", "wf-gen-over-sheath-inner", "wf-gen-over-penis",
    };

    /// <summary>Every consent gate for an adult actor and a present target with every toggle on.</summary>
    private static void AssertGates(GenitalConsentSystem consent, EntityUid actor, EntityUid target, bool expected)
    {
        // CanAdjustExternally takes any named toggle; the target has this one on.
        ProtoId<ConsentTogglePrototype> external = StripToggle;
        Assert.Multiple(() =>
        {
            Assert.That(consent.AnatomyEnabled, Is.EqualTo(expected), "AnatomyEnabled");
            Assert.That(consent.TargetConsents(target), Is.EqualTo(expected), "TargetConsents");
            Assert.That(consent.CanStrip(actor, target), Is.EqualTo(expected), "CanStrip");
            Assert.That(consent.CanOperate(actor, target), Is.EqualTo(expected), "CanOperate on another player");
            Assert.That(consent.CanOperate(actor, actor), Is.EqualTo(expected), "CanOperate on oneself");
            Assert.That(consent.CanExamine(actor, target), Is.EqualTo(expected), "CanExamine another player");
            Assert.That(consent.CanExamine(actor, actor), Is.EqualTo(expected), "CanExamine oneself");
            Assert.That(consent.CanAdjustExternally(target, external, actor), Is.EqualTo(expected), "CanAdjustExternally");
        });
    }

    /// <summary>Filters a single part that offers one plain and one anatomy surgery.</summary>
    private static List<EntProtoId> FilterOne(ClientGenitalConsentSystem viewer, NetEntity part)
    {
        var choices = new Dictionary<NetEntity, List<EntProtoId>>
        {
            [part] = new() { PlainSurgeryProto, AnatomySurgeryProto },
        };

        return viewer.FilterSurgeryChoices(choices).GetValueOrDefault(part) ?? new List<EntProtoId>();
    }
}

/// <summary>Fixtures for consent tests that go through a real player, mind and consent manager. Use a Dirty pair.</summary>
internal static class GenitalConsentTestHelpers
{
    public const string HumanMob = "MobHuman";

    /// <summary>Upper bound on single ticks to wait for a consent round trip.</summary>
    private const int MaxWaitTicks = 60;

    /// <summary>A MobHuman aged 30 that counts as present (not SSD). Server thread only.</summary>
    public static EntityUid SpawnPresentAdult(IEntityManager entMan, EntityCoordinates coords)
    {
        var uid = entMan.SpawnEntity(HumanMob, coords);
        var humanoid = entMan.GetComponent<HumanoidAppearanceComponent>(uid);
        humanoid.Age = 30;
        entMan.Dirty(uid, humanoid);
        SetSsd(entMan, uid, false);
        return uid;
    }

    /// <summary>
    /// Sets and dirties SSDIndicatorComponent.IsSSD, as a disconnect or reconnect would. A reconnect also resends the body's
    /// anatomy, as GenitalsSystem does on PlayerAttachedEvent. Server thread only.
    /// </summary>
    public static void SetSsd(IEntityManager entMan, EntityUid uid, bool ssd)
    {
        var comp = entMan.GetComponent<SSDIndicatorComponent>(uid);
        comp.IsSSD = ssd;
        entMan.Dirty(uid, comp);

        if (!ssd && entMan.TryGetComponent<GenitalsComponent>(uid, out var genitals))
            entMan.System<GenitalsSystem>().ResendAnatomy((uid, genitals));
    }

    /// <summary>Creates a test map and moves the client's player, with a new mind, into a fresh adult MobHuman.</summary>
    public static async Task<ConsentTestPlayer> AttachToNewBody(TestPair pair)
    {
        var server = pair.Server;
        var entMan = server.EntMan;
        var mindSys = entMan.System<SharedMindSystem>();
        var map = await pair.CreateTestMap();

        Assert.That(pair.Client.Session, Is.Not.Null, "These tests need a connected pair.");
        var session = server.PlayerMan.GetSessionById(pair.Client.Session!.UserId);

        EntityUid body = default;
        EntityUid mind = default;
        await server.WaitPost(() =>
        {
            mindSys.WipeMind(session.ContentData()?.Mind);
            body = SpawnPresentAdult(entMan, map.GridCoords);
            mind = mindSys.CreateMind(session.UserId).Owner;
            mindSys.TransferTo(mind, body);
        });

        await pair.RunTicksSync(5);
        Assert.That(session.AttachedEntity, Is.EqualTo(body), "The player did not attach to the new body.");
        return new ConsentTestPlayer(session, body, mind, map);
    }

    /// <summary>Saves exactly these toggles through the client consent manager, as the Options tab does, and waits for both sides.</summary>
    public static async Task SetPlayerConsent(TestPair pair, params string[] toggles)
    {
        var clientConsent = pair.Client.ResolveDependency<IClientConsentManager>();
        var serverConsent = pair.Server.ResolveDependency<IServerConsentManager>();
        var userId = pair.Client.Session!.UserId;

        await WaitFor(pair, () => clientConsent.HasLoaded, "the client to receive its consent settings");

        var settings = new Dictionary<ProtoId<ConsentTogglePrototype>, string>();
        foreach (var toggle in toggles)
        {
            settings[toggle] = "on";
        }

        await pair.Client.WaitPost(() => clientConsent.UpdateConsent(new PlayerConsentSettings(string.Empty, settings)));
        await WaitFor(pair,
            () => SameToggles(clientConsent.GetConsentSettings(), toggles)
                  && SameToggles(serverConsent.GetPlayerConsentSettings(userId), toggles),
            "the server to apply and echo the consent settings");

        // Lets the live update reach the attached body and its deferred reaction run.
        await pair.RunTicksSync(2);
    }

    /// <summary>Runs single ticks until the condition holds, failing after MaxWaitTicks.</summary>
    public static async Task WaitFor(TestPair pair, Func<bool> condition, string what)
    {
        for (var i = 0; i < MaxWaitTicks; i++)
        {
            if (condition())
                return;

            await pair.RunTicksSync(1);
        }

        Assert.That(condition(), Is.True, $"Timed out waiting for {what}.");
    }

    private static bool SameToggles(PlayerConsentSettings settings, string[] toggles)
    {
        var on = settings.Toggles.Where(t => t.Value == "on").Select(t => t.Key.Id).ToHashSet();
        return on.SetEquals(toggles);
    }
}

/// <summary>The client's player as set up by GenitalConsentTestHelpers.AttachToNewBody.</summary>
internal readonly record struct ConsentTestPlayer(ICommonSession Session, EntityUid Body, EntityUid Mind, TestMapData Map);
