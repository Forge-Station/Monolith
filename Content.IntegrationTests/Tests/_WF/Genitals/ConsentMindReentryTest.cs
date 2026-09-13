using System.Collections.Generic;
using System.Linq;
using Content.Server.GameTicking;
using Content.Shared._Common.Consent;
using Content.Shared._WF.Genitals;
using Content.Shared._WF.Genitals.Components;
using Content.Shared._WF.Genitals.Systems;
using Content.Shared.Humanoid;
using Content.Shared.Mind;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using static Content.IntegrationTests.Tests._WF.Genitals.GenitalTestHelpers;
using ServerConsentSystem = Content.Server._Common.Consent.ConsentSystem;

namespace Content.IntegrationTests.Tests._WF.Genitals;

/// <summary>A mind re-entering a body that already has a ConsentComponent goes through ConsentSystem.UpdateConsent.</summary>
[TestFixture]
[TestOf(typeof(ServerConsentSystem))]
public sealed class ConsentMindReentryTest
{
    /// <summary>
    /// The mind leaves for a ghost beside the body, so the client keeps the body in view, then returns.
    /// Re-entry must dirty the component and raise the toggle event; the anatomy reaction follows one tick later.
    /// </summary>
    [Test]
    public async Task MindReentryTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;
        var entMan = server.EntMan;
        var mindSys = entMan.System<SharedMindSystem>();
        var consent = entMan.System<GenitalConsentSystem>();
        var probe = entMan.System<GenitalConsentProbeSystem>();
        ProtoId<ConsentTogglePrototype> master = MasterToggle;

        var player = await GenitalConsentTestHelpers.AttachToNewBody(pair);
        var body = player.Body;
        await server.WaitPost(() => entMan.AddComponent<GenitalConsentProbeComponent>(body));
        await GenitalConsentTestHelpers.SetPlayerConsent(pair, MasterToggle);

        await server.WaitAssertion(() =>
            Assert.That(consent.HasMaster(body), Is.True, "The saved master switch did not reach the attached body."));

        // The mind leaves: OnMindRemoved clears the toggles through UpdateConsent.
        await server.WaitAssertion(() =>
        {
            probe.Clear();
            var ghost = entMan.SpawnEntity(GameTicker.ObserverPrototypeName, entMan.GetComponent<TransformComponent>(body).Coordinates);
            mindSys.TransferTo(player.Mind, ghost);

            Assert.Multiple(() =>
            {
                Assert.That(probe.ToggleEvents.Any(e => e.Body == body && e.Toggle == master && e.Old == "on" && e.New == null),
                    Is.True, "Leaving the body must raise EntityConsentToggleUpdatedEvent for the master switch.");
                Assert.That(probe.ConsentChanged, Is.Empty, "GenitalConsentChangedEvent must wait for the next tick.");
                Assert.That(consent.HasMaster(body), Is.False, "A ghosted body keeps no toggles.");
            });
        });

        await pair.RunTicksSync(1);
        await server.WaitAssertion(() =>
        {
            Assert.That(probe.ConsentChanged, Has.Count.EqualTo(1), "One deferred event for the body.");
            Assert.Multiple(() =>
            {
                Assert.That(probe.ConsentChanged[0].Body, Is.EqualTo(body));
                Assert.That(probe.ConsentChanged[0].Master, Is.False, "The deferred handler must read the cleared toggles.");
            });
        });

        await pair.RunTicksSync(5);
        var clientBody = pair.ToClientUid(body);
        await client.WaitAssertion(() =>
        {
            var clientConsent = client.EntMan.GetComponent<ConsentComponent>(clientBody);
            Assert.That(clientConsent.ConsentSettings.Toggles.ContainsKey(master), Is.False, "The client should see the cleared toggles.");
        });

        // The mind returns to the body, which already has a ConsentComponent.
        GameTick before = default;
        await server.WaitPost(() => before = entMan.GetComponent<ConsentComponent>(body).LastModifiedTick);
        await server.WaitAssertion(() =>
        {
            probe.Clear();
            mindSys.TransferTo(player.Mind, body);
            var comp = entMan.GetComponent<ConsentComponent>(body);

            Assert.Multiple(() =>
            {
                Assert.That(comp.LastModifiedTick, Is.Not.EqualTo(before), "Re-entry must dirty the ConsentComponent.");
                Assert.That(comp.LastModifiedTick, Is.EqualTo(server.Timing.CurTick), "Re-entry must dirty the ConsentComponent now.");
                Assert.That(probe.ToggleEvents.Any(e => e.Body == body && e.Toggle == master && e.Old == null && e.New == "on"),
                    Is.True, "Re-entry must raise EntityConsentToggleUpdatedEvent for the master switch.");
                Assert.That(probe.ConsentChanged, Is.Empty, "GenitalConsentChangedEvent must wait for the next tick.");
                Assert.That(consent.HasMaster(body), Is.True, "Re-entry must restore the saved toggles.");
            });
        });

        await pair.RunTicksSync(1);
        await server.WaitAssertion(() =>
        {
            Assert.That(probe.ConsentChanged, Has.Count.EqualTo(1), "One deferred event for the body.");
            Assert.Multiple(() =>
            {
                Assert.That(probe.ConsentChanged[0].Body, Is.EqualTo(body));
                Assert.That(probe.ConsentChanged[0].Master, Is.True, "The deferred handler must read the new toggles.");
            });
        });

        await pair.RunTicksSync(5);
        await client.WaitAssertion(() =>
        {
            var clientConsent = client.EntMan.GetComponent<ConsentComponent>(clientBody);
            Assert.That(clientConsent.ConsentSettings.Toggles.GetValueOrDefault(master), Is.EqualTo("on"),
                "The client must see the toggles after re-entry.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A mind visiting another entity (a returnable ghost, the shipyard preview) still owns its body. A consent change made
    /// while visiting must reach the body, so the owner never returns to a body that still consents.
    /// </summary>
    [Test]
    public async Task VisitingMindTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var mindSys = entMan.System<SharedMindSystem>();
        var consent = entMan.System<GenitalConsentSystem>();

        var player = await GenitalConsentTestHelpers.AttachToNewBody(pair);
        var body = player.Body;
        await GenitalConsentTestHelpers.SetPlayerConsent(pair, MasterToggle);

        // The owner has adult content on, so the profile load builds the organs at once.
        await server.WaitPost(() =>
            entMan.System<SharedHumanoidAppearanceSystem>().LoadProfile(body, MakeProfile().WithGenitals(FullProfile())));
        await server.WaitAssertion(() =>
        {
            Assert.That(consent.HasMaster(body), Is.True, "The saved master switch did not reach the attached body.");
            Assert.That(entMan.GetComponent<GenitalsComponent>(body).Penis, Is.Not.Null, "Precondition: the mirror shows the organs.");
        });

        // Ghosting with the option to return visits the ghost; the body keeps its mind.
        EntityUid ghost = default;
        await server.WaitPost(() =>
        {
            ghost = entMan.SpawnEntity(GameTicker.ObserverPrototypeName, entMan.GetComponent<TransformComponent>(body).Coordinates);

            // A visited entity never gets a ConsentComponent, and ConsentSystem warns (failing the test) when it is missing.
            entMan.EnsureComponent<ConsentComponent>(ghost);
            mindSys.Visit(player.Mind, ghost);
        });

        await pair.RunTicksSync(2);
        await server.WaitAssertion(() =>
        {
            Assert.That(player.Session.AttachedEntity, Is.EqualTo(ghost), "Precondition: the session visits the ghost.");
            Assert.That(consent.HasMaster(body), Is.True, "Visiting leaves the body's toggles alone.");
        });

        // The owner turns adult content off while visiting.
        await GenitalConsentTestHelpers.SetPlayerConsent(pair);
        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(consent.HasMaster(body), Is.False, "A consent change while visiting must reach the owned body.");
                AssertMirrorEmpty(entMan.GetComponent<GenitalsComponent>(body));
            });
        });

        // Back in the body, present again: it must not consent.
        await server.WaitPost(() => mindSys.UnVisit(player.Mind));
        await pair.RunTicksSync(2);
        await server.WaitAssertion(() =>
        {
            Assert.That(player.Session.AttachedEntity, Is.EqualTo(body), "Precondition: the session returned to the body.");
            Assert.Multiple(() =>
            {
                Assert.That(consent.IsActivelyPresent(body), Is.True);
                Assert.That(consent.HasMaster(body), Is.False);
                Assert.That(consent.TargetConsents(body), Is.False);
                AssertMirrorEmpty(entMan.GetComponent<GenitalsComponent>(body));
            });
        });

        await pair.CleanReturnAsync();
    }
}

/// <summary>Test-only marker: GenitalConsentProbeSystem records consent events for bodies that carry it.</summary>
[RegisterComponent]
public sealed partial class GenitalConsentProbeComponent : Component;

/// <summary>Test-only listener for the consent toggle event and the deferred GenitalConsentChangedEvent on probed bodies.</summary>
public sealed partial class GenitalConsentProbeSystem : EntitySystem
{
    [Dependency] private GenitalConsentSystem _consent = default!;

    /// <summary>Toggle events on probed bodies, in the order raised. Old and New are null when the toggle was unset.</summary>
    public readonly List<(EntityUid Body, ProtoId<ConsentTogglePrototype> Toggle, string Old, string New)> ToggleEvents = new();

    /// <summary>Deferred events on probed bodies, with HasMaster as read inside the handler.</summary>
    public readonly List<(EntityUid Body, bool Master)> ConsentChanged = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GenitalConsentProbeComponent, EntityConsentToggleUpdatedEvent>(OnToggleUpdated);
        SubscribeLocalEvent<GenitalConsentChangedEvent>(OnConsentChanged);
    }

    public void Clear()
    {
        ToggleEvents.Clear();
        ConsentChanged.Clear();
    }

    private void OnToggleUpdated(Entity<GenitalConsentProbeComponent> ent, ref EntityConsentToggleUpdatedEvent args)
    {
        ToggleEvents.Add((ent.Owner, args.ConsentToggleProtoId, args.OldState, args.NewState));
    }

    private void OnConsentChanged(ref GenitalConsentChangedEvent ev)
    {
        if (HasComp<GenitalConsentProbeComponent>(ev.Body))
            ConsentChanged.Add((ev.Body, _consent.HasMaster(ev.Body)));
    }
}
