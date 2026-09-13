using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Client.UserInterface.Systems.Chat;
using Content.IntegrationTests.Pair;
using Content.Server._WF.Genitals;
using Content.Server.Administration.Managers;
using Content.Server.GameTicking;
using Content.Server.Preferences.Managers;
using Content.Shared._Common.Consent;
using Content.Shared._WF.Genitals;
using Content.Shared._WF.Genitals.Components;
using Content.Shared._WF.Genitals.Systems;
using Content.Shared.Administration;
using Content.Shared.Chat;
using Content.Shared.Humanoid;
using Content.Shared.Mind;
using Content.Shared.Verbs;
using Robust.Client.UserInterface;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Timing;
using static Content.IntegrationTests.Tests._WF.Genitals.GenitalTestHelpers;

namespace Content.IntegrationTests.Tests._WF.Genitals;

/// <summary>
/// GenitalsComponent state reaches its owner always, and other sessions only with Adult content on, for a humanoid viewer
/// an adult body, and a present target. Becoming allowed delivers every body's current state; while not allowed nothing
/// new arrives. Also the anatomy admin verbs.
/// </summary>
[TestFixture]
[TestOf(typeof(GenitalsSystem))]
public sealed class GenitalPrivacyTest
{
    private const string ReapplyVerb = "wf-anatomy-admin-verb-reapply";
    private const string ClearVerb = "wf-anatomy-admin-verb-clear";
    private const string DumpVerb = "wf-anatomy-admin-verb-dump";
    private const string LockedVerb = "wf-anatomy-admin-verb-locked";

    /// <summary>Ticks for a state change to reach the client.</summary>
    private const int SyncTicks = 10;

    /// <summary>Nothing without the viewer's master switch, everything current once it is on.</summary>
    [Test]
    public async Task ViewerConsentTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var sEntMan = server.EntMan;
        var cEntMan = pair.Client.EntMan;
        var arousal = sEntMan.System<SharedArousalSystem>();

        var player = await GenitalConsentTestHelpers.AttachToNewBody(pair);

        // A viewer without adult content whose own body has anatomy, beside an opted-in adult with every organ.
        EntityUid target = default;
        await server.WaitPost(() =>
        {
            target = SpawnWithAnatomy(sEntMan, Beside(player));
            GenitalConsentTestHelpers.SetSsd(sEntMan, target, false);
            sEntMan.System<GenitalOrganSystem>().BuildOrgans(player.Body, FullProfile());

            var own = sEntMan.GetComponent<GenitalsComponent>(player.Body);
            own.RevealMode = GenitalRevealMode.ClothingRemoval;
            sEntMan.DirtyField(player.Body, own, nameof(GenitalsComponent.RevealMode));
        });
        await pair.RunTicksSync(SyncTicks);

        var clientBody = pair.ToClientUid(player.Body);
        var clientTarget = pair.ToClientUid(target);
        await pair.Client.WaitAssertion(() =>
        {
            Assert.That(cEntMan.EntityExists(clientTarget), Is.True, "Precondition: the client sees the target.");
            Assert.Multiple(() =>
            {
                Assert.That(cEntMan.HasComponent<GenitalsComponent>(clientTarget), Is.False,
                    "A viewer without adult content must not receive another body's anatomy.");
                Assert.That(cEntMan.TryGetComponent<GenitalsComponent>(clientBody, out var own)
                            && own.RevealMode == GenitalRevealMode.ClothingRemoval,
                    Is.True,
                    "The owner always receives their own body's state.");
            });
        });

        // A later change stays private too.
        await server.WaitAssertion(() => SetArousal(sEntMan, arousal, target, 20));
        await pair.RunTicksSync(SyncTicks);
        await pair.Client.WaitAssertion(() =>
            Assert.That(cEntMan.HasComponent<GenitalsComponent>(clientTarget), Is.False, "A change on the target must not leak."));

        // Turning adult content on delivers the target's full current state.
        await GenitalConsentTestHelpers.SetPlayerConsent(pair, MasterToggle);
        await GenitalConsentTestHelpers.WaitFor(pair,
            () => cEntMan.HasComponent<GenitalsComponent>(clientTarget),
            "the client to receive the target's anatomy");
        await AssertClientMatches(pair, target, "once adult content is on");

        // A save that leaves adult content on (another toggle changed) resends nothing: only a newly allowed viewer needs it.
        GameTick sent = default;
        await server.WaitPost(() => sent = sEntMan.GetComponent<GenitalsComponent>(target).LastModifiedTick);
        await GenitalConsentTestHelpers.SetPlayerConsent(pair, MasterToggle, StripToggle);
        await pair.RunTicksSync(SyncTicks);
        await server.WaitAssertion(() =>
            Assert.That(sEntMan.GetComponent<GenitalsComponent>(target).LastModifiedTick, Is.EqualTo(sent),
                "A consent save that leaves adult content on must not resend other bodies' anatomy."));

        // Off: nothing new arrives. A copy the client already has stays behind the client gate.
        await GenitalConsentTestHelpers.SetPlayerConsent(pair);
        await server.WaitAssertion(() => SetArousal(sEntMan, arousal, target, 40));
        await pair.RunTicksSync(SyncTicks);
        await pair.Client.WaitAssertion(() =>
            Assert.That(!cEntMan.TryGetComponent<GenitalsComponent>(clientTarget, out var stale) || stale.Arousal != 40,
                Is.True,
                "A viewer who turned adult content off must not receive new changes."));

        // On again: the change made meanwhile arrives with the rest.
        await GenitalConsentTestHelpers.SetPlayerConsent(pair, MasterToggle);
        await GenitalConsentTestHelpers.WaitFor(pair,
            () => cEntMan.TryGetComponent<GenitalsComponent>(clientTarget, out var genitals) && genitals.Arousal == 40,
            "the client to receive the change made while adult content was off");
        await AssertClientMatches(pair, target, "once adult content is on again");

        await pair.CleanReturnAsync();
    }

    /// <summary>A ghost visiting from its body has no toggles of its own; the body's master switch still lets it receive anatomy.</summary>
    [Test]
    public async Task GhostViewerTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var sEntMan = server.EntMan;
        var cEntMan = pair.Client.EntMan;
        var arousal = sEntMan.System<SharedArousalSystem>();

        var player = await GenitalConsentTestHelpers.AttachToNewBody(pair);
        await GenitalConsentTestHelpers.SetPlayerConsent(pair, MasterToggle);

        EntityUid target = default;
        await server.WaitPost(() =>
        {
            target = SpawnWithAnatomy(sEntMan, Beside(player));
            GenitalConsentTestHelpers.SetSsd(sEntMan, target, false);
        });
        await pair.RunTicksSync(SyncTicks);

        var clientTarget = pair.ToClientUid(target);
        await GenitalConsentTestHelpers.WaitFor(pair,
            () => cEntMan.HasComponent<GenitalsComponent>(clientTarget),
            "the client to receive the target's anatomy");

        EntityUid ghost = default;
        await server.WaitPost(() =>
        {
            ghost = sEntMan.SpawnEntity(GameTicker.ObserverPrototypeName, player.Map.GridCoords);
            sEntMan.System<SharedMindSystem>().Visit(player.Mind, ghost);
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(player.Session.AttachedEntity, Is.EqualTo(ghost), "Precondition: the player visits the ghost.");
            Assert.That(sEntMan.HasComponent<ConsentComponent>(ghost), Is.False, "Precondition: a visiting ghost carries no toggles.");
            SetArousal(sEntMan, arousal, target, 60);
        });

        await GenitalConsentTestHelpers.WaitFor(pair,
            () => cEntMan.TryGetComponent<GenitalsComponent>(clientTarget, out var genitals) && genitals.Arousal == 60,
            "the ghost's client to receive a change on the target");

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A viewer on a humanoid under AdultAge receives no other body's anatomy, even with adult content on. A ghost is
    /// not humanoid, so visiting one delivers the current state.
    /// </summary>
    [Test]
    public async Task MinorViewerTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var sEntMan = server.EntMan;
        var cEntMan = pair.Client.EntMan;
        var arousal = sEntMan.System<SharedArousalSystem>();

        var player = await GenitalConsentTestHelpers.AttachToNewBody(pair);

        EntityUid target = default;
        await server.WaitPost(() =>
        {
            var humanoid = sEntMan.GetComponent<HumanoidAppearanceComponent>(player.Body);
            humanoid.Age = 17;
            sEntMan.Dirty(player.Body, humanoid);
            target = SpawnWithAnatomy(sEntMan, Beside(player));
            GenitalConsentTestHelpers.SetSsd(sEntMan, target, false);
        });
        await GenitalConsentTestHelpers.SetPlayerConsent(pair, MasterToggle);
        await server.WaitAssertion(() => SetArousal(sEntMan, arousal, target, 20));
        await pair.RunTicksSync(SyncTicks);

        var clientTarget = pair.ToClientUid(target);
        await pair.Client.WaitAssertion(() =>
        {
            Assert.That(cEntMan.EntityExists(clientTarget), Is.True, "Precondition: the client sees the target.");
            Assert.That(cEntMan.HasComponent<GenitalsComponent>(clientTarget), Is.False,
                "A viewer under the adult age must not receive another body's anatomy.");
        });

        // Leaving the minor body for a ghost makes the player a viewer; the resend delivers the target's current state.
        await server.WaitPost(() =>
        {
            var ghost = sEntMan.SpawnEntity(GameTicker.ObserverPrototypeName, player.Map.GridCoords);
            sEntMan.System<SharedMindSystem>().Visit(player.Mind, ghost);
        });
        await GenitalConsentTestHelpers.WaitFor(pair,
            () => cEntMan.HasComponent<GenitalsComponent>(clientTarget),
            "the ghost's client to receive the target's anatomy");
        await AssertClientMatches(pair, target, "once the viewer visits a ghost");

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A disconnected (SSD) target counts as not consenting: viewers other than the owner are refused, so its changes stay
    /// on the server until the player is back, and then its full state arrives.
    /// </summary>
    [Test]
    public async Task SsdTargetTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var sEntMan = server.EntMan;
        var cEntMan = pair.Client.EntMan;
        var arousal = sEntMan.System<SharedArousalSystem>();
        var privacy = sEntMan.System<GenitalsSystem>();

        var player = await GenitalConsentTestHelpers.AttachToNewBody(pair);
        await GenitalConsentTestHelpers.SetPlayerConsent(pair, MasterToggle);

        EntityUid target = default;
        await server.WaitPost(() =>
        {
            target = SpawnWithAnatomy(sEntMan, Beside(player));
            GenitalConsentTestHelpers.SetSsd(sEntMan, target, false);
        });
        await pair.RunTicksSync(SyncTicks);

        var clientTarget = pair.ToClientUid(target);
        await GenitalConsentTestHelpers.WaitFor(pair,
            () => cEntMan.HasComponent<GenitalsComponent>(clientTarget),
            "the client to receive the present target's anatomy");

        // Disconnected: viewers are refused, the owner's own session never is.
        await server.WaitAssertion(() =>
        {
            GenitalConsentTestHelpers.SetSsd(sEntMan, target, true);
            Assert.Multiple(() =>
            {
                Assert.That(privacy.CanReceiveAnatomy(player.Session, target), Is.False, "An SSD target must not reach viewers.");
                Assert.That(privacy.CanReceiveAnatomy(player.Session, player.Body), Is.True, "The owner always receives their own body.");
            });

            SetArousal(sEntMan, arousal, target, 30);
        });
        await pair.RunTicksSync(SyncTicks);
        await pair.Client.WaitAssertion(() =>
            Assert.That(!cEntMan.TryGetComponent<GenitalsComponent>(clientTarget, out var stale) || stale.Arousal != 30,
                Is.True,
                "A change on an SSD target must not reach viewers."));

        // Back: the full current state arrives.
        await server.WaitPost(() => GenitalConsentTestHelpers.SetSsd(sEntMan, target, false));
        await GenitalConsentTestHelpers.WaitFor(pair,
            () => cEntMan.TryGetComponent<GenitalsComponent>(clientTarget, out var genitals) && genitals.Arousal == 30,
            "the client to receive the change made while the target was SSD");
        await AssertClientMatches(pair, target, "once the target is present again");

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Admin verbs: none for players, one locked verb for admins without adult content and the three anatomy verbs with it,
    /// of which reapply and clear ask for confirmation. Clear resets runtime state to the template, the summary reaches the
    /// admin's client, and reapply rebuilds from the controlling player's selected profile, or stores it until the owner
    /// consents.
    /// </summary>
    [Test]
    [TestOf(typeof(GenitalAdminVerbSystem))]
    public async Task AdminVerbTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var sEntMan = server.EntMan;
        var loc = server.ResolveDependency<ILocalizationManager>();
        var admins = server.ResolveDependency<IAdminManager>();
        var prefs = server.ResolveDependency<IServerPreferencesManager>();
        var verbs = sEntMan.System<SharedVerbSystem>();
        var adminVerbs = sEntMan.System<GenitalAdminVerbSystem>();
        var arousal = sEntMan.System<SharedArousalSystem>();

        var player = await GenitalConsentTestHelpers.AttachToNewBody(pair);
        var session = player.Session;
        var admin = player.Body;

        EntityUid target = default;
        await server.WaitPost(() =>
        {
            target = SpawnWithAnatomy(sEntMan, Beside(player));
            admins.PromoteHost(session);
        });
        await GenitalConsentTestHelpers.WaitFor(pair,
            () => admins.HasAdminFlag(session, AdminFlags.Admin),
            "the player to become an admin");

        // Not an admin: nothing.
        await server.WaitPost(() => admins.DeAdmin(session));
        await server.WaitAssertion(() =>
            Assert.That(AnatomyVerbs(verbs, loc, admin, target), Is.Empty, "A player who is not an admin gets no anatomy verbs."));
        await server.WaitPost(() => admins.ReAdmin(session));

        // An admin without adult content: one disabled, neutral verb.
        await server.WaitAssertion(() =>
        {
            var found = AnatomyVerbs(verbs, loc, admin, target);
            Assert.That(found.Select(v => v.Text), Is.EquivalentTo(new[] { loc.GetString(LockedVerb) }));
            Assert.That(found.All(v => v.Disabled), Is.True, "The locked verb must be disabled.");
        });

        // With adult content: the three anatomy verbs. Reapply and clear change the body, so they ask first.
        await GenitalConsentTestHelpers.SetPlayerConsent(pair, MasterToggle);
        await server.WaitAssertion(() =>
        {
            var found = AnatomyVerbs(verbs, loc, admin, target).ToDictionary(v => v.Text);
            Assert.That(found.Keys, Is.EquivalentTo(new[] { ReapplyVerb, ClearVerb, DumpVerb }.Select(id => loc.GetString(id))));
            Assert.Multiple(() =>
            {
                Assert.That(found[loc.GetString(ReapplyVerb)].ConfirmationPopup, Is.True, "Reapply must ask for confirmation.");
                Assert.That(found[loc.GetString(ClearVerb)].ConfirmationPopup, Is.True, "Clear must ask for confirmation.");
                Assert.That(found[loc.GetString(DumpVerb)].ConfirmationPopup, Is.False, "Dump changes nothing, so it does not ask.");
            });
        });

        // Clear: runtime state returns to the template's defaults; organs and template stay.
        GenitalProfile template = null;
        await server.WaitAssertion(() =>
        {
            var genitals = sEntMan.GetComponent<GenitalsComponent>(target);
            template = genitals.SourceProfile;
            Assert.That(template, Is.Not.Null, "Precondition: the target has a template.");

            SetArousal(sEntMan, arousal, target, 50);
            genitals.RevealMode = GenitalRevealMode.ClothingRemoval;
            genitals.Visibility = genitals.Visibility.With(GenitalSlot.Penis, GenitalVisibility.ShowThroughClothing);
            genitals.Undergarments = UndergarmentFlags.TopRemoved | UndergarmentFlags.TopByOther;
            sEntMan.Dirty(target, genitals);

            Execute(verbs, loc, admin, target, ClearVerb);

            Assert.Multiple(() =>
            {
                Assert.That(genitals.Arousal, Is.Zero, "Arousal");
                Assert.That(genitals.RevealMode, Is.EqualTo(template.RevealMode), "RevealMode");
                Assert.That(genitals.Visibility, Is.EqualTo(GenitalStateBuilder.VisibilityFromProfile(template)), "Visibility");
                Assert.That(genitals.Undergarments, Is.EqualTo(UndergarmentFlags.None), "Undergarments");
                Assert.That(genitals.Penis, Is.Not.Null, "Clearing runtime state keeps the organs.");
                Assert.That(genitals.SourceProfile, Is.SameAs(template), "Clearing runtime state keeps the template.");
            });
        });

        // Dump: a summary that lists the organs, delivered to the admin's client as a server message.
        string summary = null;
        await server.WaitAssertion(() =>
        {
            summary = adminVerbs.BuildSummary((target, sEntMan.GetComponent<GenitalsComponent>(target)));
            Assert.Multiple(() =>
            {
                Assert.That(summary, Does.Contain(loc.GetString("wf-genitals-shape-penis-knotted")), "The penis shape.");
                Assert.That(summary, Does.Contain(loc.GetString("wf-anatomy-admin-dump-womb")), "The womb.");
            });

            Execute(verbs, loc, admin, target, DumpVerb);
        });
        await WaitForServerMessage(pair, summary, "the summary");

        // Reapply on a body without a player: nothing changes.
        await server.WaitAssertion(() =>
        {
            Execute(verbs, loc, admin, target, ReapplyVerb);

            var genitals = sEntMan.GetComponent<GenitalsComponent>(target);
            Assert.That(genitals.SourceProfile, Is.SameAs(template), "A body without a player keeps its template.");
            Assert.That(genitals.Penis, Is.Not.Null, "A body without a player keeps its organs.");
        });

        // Reapply on the admin's own body: the selected profile becomes the template and the organs are built.
        var userId = session.UserId;
        Assert.That(prefs.GetPreferences(userId).SelectedCharacterIndex, Is.EqualTo(0), "Precondition: slot 0 is selected.");
        var profile = MakeProfile().WithGenitals(GenitalProfile.Empty.WithBreasts(new BreastsProfile(BreastsPair, 5)));
        await server.WaitPost(() => prefs.SetProfile(userId, 0, profile).Wait());

        await server.WaitAssertion(() =>
        {
            Assert.That(sEntMan.HasComponent<GenitalsComponent>(admin), Is.True,
                "Precondition: adult content gave the admin's own body anatomy.");

            Execute(verbs, loc, admin, admin, ReapplyVerb);

            var genitals = sEntMan.GetComponent<GenitalsComponent>(admin);
            Assert.Multiple(() =>
            {
                Assert.That(genitals.SourceProfile?.Breasts?.Cup, Is.EqualTo(5), "The selected profile is the new template.");
                Assert.That(genitals.SourceProfile?.Penis, Is.Null, "The new template has no penis.");
                Assert.That(genitals.OrgansBuilt, Is.True, "The owner consents, so the organs are built.");
                Assert.That(genitals.Breasts, Is.Not.Null, "The mirror follows the new organs.");
                Assert.That(genitals.Penis, Is.Null, "No penis organ was built.");
            });
        });

        // Reapply while the body's own adult content is off: the new template is stored and the organs wait for consent.
        var pendingProfile = MakeProfile().WithGenitals(GenitalProfile.Empty.WithVagina(new VaginaProfile(VaginaHuman)));
        await server.WaitPost(() => prefs.SetProfile(userId, 0, pendingProfile).Wait());

        string pendingReply = null;
        await server.WaitAssertion(() =>
        {
            // Only the body's toggles; the admin's saved settings keep the verbs available.
            RevokeConsent(sEntMan, admin);
            var organsBefore = GenitalOrgans(sEntMan, admin).Select(o => o.Owner).ToList();
            Assert.That(organsBefore, Is.Not.Empty, "Precondition: the body has organs.");

            Execute(verbs, loc, admin, admin, ReapplyVerb);
            pendingReply = loc.GetString("wf-anatomy-admin-reapplied-pending", ("target", admin));

            var genitals = sEntMan.GetComponent<GenitalsComponent>(admin);
            Assert.Multiple(() =>
            {
                Assert.That(genitals.SourceProfile?.Vagina, Is.Not.Null, "The selected profile is the new template.");
                Assert.That(genitals.SourceProfile?.Breasts, Is.Null, "The new template has no breasts.");
                Assert.That(genitals.OrgansBuilt, Is.False, "Without the owner's consent nothing is built.");
                Assert.That(GenitalOrgans(sEntMan, admin).Select(o => o.Owner), Is.EquivalentTo(organsBefore),
                    "The old organs stay until the owner consents.");
            });
        });
        await WaitForServerMessage(pair, pendingReply, "the pending reapply reply");

        // Consent arrives: the organs are built from the stored template.
        await server.WaitPost(() => GrantConsent(sEntMan, admin));
        await pair.RunTicksSync(1);
        await server.WaitAssertion(() =>
        {
            var genitals = sEntMan.GetComponent<GenitalsComponent>(admin);
            Assert.Multiple(() =>
            {
                Assert.That(genitals.OrgansBuilt, Is.True, "Consent builds the stored template.");
                Assert.That(GenitalOrgan(sEntMan, admin, GenitalSlot.Vagina), Is.Not.Null, "The template's vagina was built.");
                Assert.That(GenitalOrgan(sEntMan, admin, GenitalSlot.Breasts), Is.Null, "The old breasts were replaced.");
                Assert.That(genitals.Vagina, Is.Not.Null, "The mirror follows the new organs.");
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A point one tile from the player, well inside its view.</summary>
    private static MapCoordinates Beside(ConsentTestPlayer player)
    {
        return new MapCoordinates(player.Map.MapCoords.Position + new Vector2(1f, 0f), player.Map.MapCoords.MapId);
    }

    /// <summary>Sets arousal through the owner path, which the target's consent allows. Server thread only.</summary>
    private static void SetArousal(IEntityManager entMan, SharedArousalSystem arousal, EntityUid body, byte value)
    {
        var genitals = entMan.GetComponent<GenitalsComponent>(body);
        Assert.That(arousal.SetArousal((body, genitals), value), Is.True, "Precondition: the arousal change was accepted.");
    }

    /// <summary>The client's copy of the target's GenitalsComponent equals the server's.</summary>
    private static async Task AssertClientMatches(TestPair pair, EntityUid target, string when)
    {
        MirrorSnapshot expected = default;
        await pair.Server.WaitPost(() => expected = MirrorSnapshot.Of(pair.Server.EntMan.GetComponent<GenitalsComponent>(target)));

        var clientTarget = pair.ToClientUid(target);
        await pair.Client.WaitAssertion(() =>
        {
            var actual = MirrorSnapshot.Of(pair.Client.EntMan.GetComponent<GenitalsComponent>(clientTarget));
            Assert.That(actual, Is.EqualTo(expected), $"The client's copy differs from the server's {when}.");
        });
    }

    /// <summary>Waits for the client's chat history to hold a server message with exactly this text.</summary>
    private static async Task WaitForServerMessage(TestPair pair, string text, string what)
    {
        var chat = pair.Client.ResolveDependency<IUserInterfaceManager>().GetUIController<ChatUIController>();
        await GenitalConsentTestHelpers.WaitFor(pair,
            () => chat.History.Any(h => h.Msg.Channel == ChatChannel.Server && h.Msg.Message == text),
            $"the admin's client to receive {what}");
    }

    /// <summary>The anatomy admin verbs the user gets on the target. Server thread only.</summary>
    private static List<Verb> AnatomyVerbs(SharedVerbSystem verbs, ILocalizationManager loc, EntityUid user, EntityUid target)
    {
        var texts = new[] { ReapplyVerb, ClearVerb, DumpVerb, LockedVerb }.Select(id => loc.GetString(id)).ToHashSet();
        return verbs.GetLocalVerbs(target, user, typeof(Verb))
            .Where(v => v.Category == VerbCategory.Admin && texts.Contains(v.Text))
            .ToList();
    }

    /// <summary>Executes one anatomy admin verb on the server, as the client's request would. Server thread only.</summary>
    private static void Execute(SharedVerbSystem verbs, ILocalizationManager loc, EntityUid user, EntityUid target, string verbId)
    {
        var text = loc.GetString(verbId);
        var verb = AnatomyVerbs(verbs, loc, user, target).FirstOrDefault(v => v.Text == text);
        Assert.That(verb, Is.Not.Null, $"Missing verb {verbId}.");
        verbs.ExecuteVerb(verb, user, target);
    }

    /// <summary>The networked fields compared between server and client.</summary>
    private readonly record struct MirrorSnapshot(
        GenitalOrganState? Penis,
        GenitalOrganState? Testicles,
        GenitalOrganState? Vagina,
        bool Womb,
        GenitalOrganState? Breasts,
        byte Arousal,
        GenitalRevealMode RevealMode,
        GenitalVisibilitySet Visibility,
        UndergarmentFlags Undergarments)
    {
        public static MirrorSnapshot Of(GenitalsComponent genitals)
        {
            return new MirrorSnapshot(genitals.Penis,
                genitals.Testicles,
                genitals.Vagina,
                genitals.Womb,
                genitals.Breasts,
                genitals.Arousal,
                genitals.RevealMode,
                genitals.Visibility,
                genitals.Undergarments);
        }
    }
}
