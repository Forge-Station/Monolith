using System.Collections.Generic;
using System.Linq;
using Content.Server._WF.Genitals;
using Content.Server.Administration.Logs;
using Content.Server.Body.Systems;
using Content.Shared._Common.Consent;
using Content.Shared._WF.Genitals;
using Content.Shared._WF.Genitals.Components;
using Content.Shared.Administration.Logs;
using Content.Shared.Body.Organ;
using Content.Shared.Body.Systems;
using Content.Shared.Database;
using Robust.Shared.GameObjects;
using static Content.IntegrationTests.Tests._WF.Genitals.GenitalTestHelpers;

namespace Content.IntegrationTests.Tests._WF.Genitals;

/// <summary>Gibbing and non-surgical removal: only bodies whose owner is opted in leave genital organs behind.</summary>
[TestFixture]
[TestOf(typeof(GenitalOrganSystem))]
public sealed class GenitalGibTest
{
    /// <summary>How a body is not opted in.</summary>
    public enum OptOut
    {
        /// <summary>Other toggles on, the master switch off.</summary>
        MasterOff,

        /// <summary>Every toggle cleared, as when the mind leaves the body (ConsentSystem.OnMindRemoved).</summary>
        Ghosted,

        /// <summary>No ConsentComponent, as on a body that was never possessed.</summary>
        NoConsentComponent,
    }

    /// <summary>A consenting body drops its genital organs as loose items with the neutral name.</summary>
    [Test]
    public async Task ConsentingBodyDropsOrgansTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        EntityUid mob = default;
        var organs = new List<EntityUid>();
        await server.WaitPost(() =>
        {
            mob = SpawnWithAnatomy(entMan, map.MapCoords);
            organs = GenitalOrgans(entMan, mob).Select(o => o.Owner).ToList();
        });

        await server.WaitPost(() => entMan.System<BodySystem>().GibBody(mob));
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(organs, Has.Count.EqualTo(5));
            Assert.Multiple(() =>
            {
                foreach (var organ in organs)
                {
                    Assert.That(entMan.EntityExists(organ), $"Organ {organ} of a consenting body was deleted.");
                    if (!entMan.EntityExists(organ))
                        continue;

                    Assert.That(entMan.GetComponent<MetaDataComponent>(organ).EntityName, Is.EqualTo("organ tissue"));
                    Assert.That(entMan.GetComponent<OrganComponent>(organ).Body, Is.Null, $"Organ {organ} is still in a body.");
                }
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Gibbing a body that is not opted in leaves no genital organ anywhere on the map.</summary>
    [TestCase(OptOut.MasterOff)]
    [TestCase(OptOut.Ghosted)]
    [TestCase(OptOut.NoConsentComponent)]
    public async Task NotOptedInBodyLeavesNoOrgansTest(OptOut optOut)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        EntityUid mob = default;
        var organs = new List<EntityUid>();
        await server.WaitPost(() =>
        {
            mob = SpawnWithAnatomy(entMan, map.MapCoords);
            organs = GenitalOrgans(entMan, mob).Select(o => o.Owner).ToList();

            switch (optOut)
            {
                case OptOut.MasterOff:
                    SetConsent(entMan, mob, StripToggle);
                    break;
                case OptOut.Ghosted:
                    RevokeConsent(entMan, mob);
                    break;
                case OptOut.NoConsentComponent:
                    entMan.RemoveComponent<ConsentComponent>(mob);
                    break;
            }
        });

        await server.WaitPost(() => entMan.System<BodySystem>().GibBody(mob));
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(organs, Has.Count.EqualTo(5));
            Assert.Multiple(() =>
            {
                foreach (var organ in organs)
                {
                    Assert.That(entMan.EntityExists(organ), Is.False, $"Organ {organ} survived the gib.");
                }

                var query = entMan.EntityQueryEnumerator<GenitalOrganComponent, TransformComponent>();
                while (query.MoveNext(out var uid, out _, out var xform))
                {
                    Assert.That(xform.MapUid, Is.Not.EqualTo(map.MapUid), $"Genital organ {uid} remains on the map.");
                }
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>An organ removed outside surgery from a body that is not opted in is deleted; one from an opted-in body stays.</summary>
    [Test]
    public async Task NonSurgicalRemovalTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        EntityUid optedIn = default;
        EntityUid kept = default;
        EntityUid lost = default;
        await server.WaitPost(() =>
        {
            optedIn = SpawnWithAnatomy(entMan, map.MapCoords);
            var optedOut = SpawnWithAnatomy(entMan, map.MapCoords);
            RevokeConsent(entMan, optedOut);

            kept = GenitalOrgan(entMan, optedIn, GenitalSlot.Penis) ?? EntityUid.Invalid;
            lost = GenitalOrgan(entMan, optedOut, GenitalSlot.Penis) ?? EntityUid.Invalid;

            var body = entMan.System<SharedBodySystem>();
            body.RemoveOrgan(kept);
            body.RemoveOrgan(lost);
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var genitals = entMan.GetComponent<GenitalsComponent>(optedIn);
            Assert.Multiple(() =>
            {
                Assert.That(kept.IsValid() && lost.IsValid(), "A body had no penis organ.");
                Assert.That(entMan.EntityExists(kept), "An organ removed from an opted-in body was deleted.");
                Assert.That(entMan.EntityExists(lost), Is.False, "An organ removed from a body that is not opted in survived.");
                Assert.That(genitals.Penis, Is.Null, "The mirror still shows the removed penis.");
                Assert.That(genitals.Testicles, Is.Not.Null, "Removing the penis cleared another organ.");
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>An organ leaving a living body outside a rebuild gets a WFAnatomy admin log; organs a rebuild deletes do not.</summary>
    [Test]
    public async Task RemovalAdminLogTest()
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

        var rebuilt = new List<EntityUid>();
        EntityUid removed = default;
        await server.WaitPost(() =>
        {
            var mob = SpawnWithAnatomy(entMan, map.MapCoords);

            // A rebuild deletes the old organs; that is not a removal.
            rebuilt = GenitalOrgans(entMan, mob).Select(o => o.Owner).ToList();
            entMan.System<GenitalOrganSystem>().BuildOrgans(mob, FullProfile());

            removed = GenitalOrgan(entMan, mob, GenitalSlot.Penis) ?? EntityUid.Invalid;
            if (removed.IsValid())
                entMan.System<SharedBodySystem>().RemoveOrgan(removed);
        });

        Assert.That(removed.IsValid(), "The rebuilt body had no penis organ.");

        // ToPrettyString writes "name (uid/nNetEntity, prototype)".
        var removedTag = $"({removed}/n";
        List<SharedAdminLog> logs = new();
        await PoolManager.WaitUntil(server, async () =>
        {
            logs = await adminLog.CurrentRoundLogs(new LogFilter { Types = new HashSet<LogType> { LogType.WFAnatomy } });
            return logs.Any(l => l.Message.Contains(removedTag));
        });

        Assert.Multiple(() =>
        {
            var removal = logs.First(l => l.Message.Contains(removedTag));
            Assert.That(removal.Impact, Is.EqualTo(LogImpact.Medium));
            Assert.That(removal.Message, Does.Contain("removed from"));

            // Logs are written in order, so the rebuild's would be there by now.
            Assert.That(rebuilt, Has.Count.EqualTo(5));
            foreach (var organ in rebuilt)
            {
                Assert.That(logs.Any(l => l.Message.Contains($"({organ}/n")), Is.False, $"The rebuild logged organ {organ} as removed.");
            }
        });

        await pair.CleanReturnAsync();
    }
}
