#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Tests.Interaction;
using Content.Server.GameTicking;
using Content.Shared._Common.Consent;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.Genitals;
using Content.Shared._WF.Genitals.Components;
using Content.Shared._WF.Genitals.Systems;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.IdentityManagement;
using Content.Shared.Inventory;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.SSDIndicator;
using Content.Shared.Verbs;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Localization;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using ClientVerbSystem = Content.Client.Verbs.VerbSystem;

namespace Content.IntegrationTests.Tests._WF.Genitals;

/// <summary>
/// The undergarment strip verbs follow the consent matrix, clothing and the cooldown disable them, completion applies and
/// cancellation changes nothing, revocation puts undergarments back, and bystanders receive no popups.
/// </summary>
/// <remarks>The player (client) is the actor unless a test moves the session: SSD and disconnects need a real session on the target.</remarks>
[TestOf(typeof(SharedUndergarmentStripSystem))]
public sealed class UndergarmentStripTest : InteractionTest
{
    protected override string PlayerPrototype => "MobHuman";

    private const string TopMarking = "UndergarmentTopTanktop";
    private const string BottomMarking = "UndergarmentBottomBoxers";
    private const string Jumpsuit = "ClothingUniformJumpsuitColorGrey";
    private const string Vest = "ClothingOuterVest";
    private const string NonHumanoidMob = "InteractionTestMob";
    private const string JumpsuitSlot = "jumpsuit";
    private const string OuterSlot = "outerClothing";

    private const string RemoveTop = "wf-undergarment-verb-remove-top";
    private const string RemoveBottom = "wf-undergarment-verb-remove-bottom";
    private const string ReplaceTop = "wf-undergarment-verb-replace-top";
    private const string ReplaceBottom = "wf-undergarment-verb-replace-bottom";
    private const string FailCovered = "wf-undergarment-fail-covered";
    private const string FailConsent = "wf-undergarment-fail-consent";
    private const string FailCooldown = "wf-undergarment-fail-cooldown";

    private static readonly string[] VerbIds = { RemoveTop, RemoveBottom, ReplaceTop, ReplaceBottom };

    private SharedUndergarmentStripSystem _strip = default!;
    private ILocalizationManager _sLoc = default!;
    private ILocalizationManager _cLoc = default!;
    private float _stripDelay;

    /// <summary>Gravity and air, so neither drifting nor pressure damage breaks a DoAfter.</summary>
    [SetUp]
    public override async Task Setup()
    {
        await base.Setup();
        await AddGravity();
        await AddAtmosphere();

        _strip = SEntMan.System<SharedUndergarmentStripSystem>();
        _sLoc = Server.ResolveDependency<ILocalizationManager>();
        _cLoc = Client.ResolveDependency<ILocalizationManager>();
        _stripDelay = SEntMan.System<SharedGenitalsSystem>().Settings.StripDelay;
    }

    /// <summary>The verbs appear only for the full consent matrix; every other row shows nothing at all, on both sides.</summary>
    [Test]
    public async Task ConsentMatrixTest()
    {
        var target = await SpawnConsentingTarget();
        var mobState = SEntMan.System<MobStateSystem>();

        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                AssertVerbs(SPlayer, target, "allowed", RemoveTop, RemoveBottom);

                // Actor rows.
                GenitalTestHelpers.RevokeConsent(SEntMan, SPlayer);
                AssertVerbs(SPlayer, target, "actor without adult content");
                GenitalTestHelpers.GrantConsent(SEntMan, SPlayer);

                SetAge(SPlayer, 17);
                AssertVerbs(SPlayer, target, "minor actor");
                SetAge(SPlayer, 30);

                var nonHumanoid = SEntMan.SpawnEntity(NonHumanoidMob, SEntMan.GetCoordinates(PlayerCoords));
                GenitalTestHelpers.GrantConsent(SEntMan, nonHumanoid);
                AssertVerbs(nonHumanoid, target, "actor that is not humanoid");
                SEntMan.DeleteEntity(nonHumanoid);

                AssertVerbs(target, target, "the target on themself");

                // Target rows.
                GenitalTestHelpers.SetConsent(SEntMan, target, GenitalTestHelpers.StripToggle);
                AssertVerbs(SPlayer, target, "target without adult content");

                SEntMan.RemoveComponent<ConsentComponent>(target);
                AssertVerbs(SPlayer, target, "target without a ConsentComponent (NPC)");

                GenitalTestHelpers.GrantConsent(SEntMan, target);
                AssertVerbs(SPlayer, target, "target without UndergarmentStrip");

                GenitalTestHelpers.RevokeConsent(SEntMan, target);
                AssertVerbs(SPlayer, target, "target with cleared toggles (ghosted)");

                GenitalTestHelpers.GrantConsent(SEntMan, target, GenitalTestHelpers.StripToggle);
                GenitalConsentTestHelpers.SetSsd(SEntMan, target, true);
                AssertVerbs(SPlayer, target, "SSD target");
                GenitalConsentTestHelpers.SetSsd(SEntMan, target, false);

                // Consent covers unconsciousness; death ends it.
                mobState.ChangeMobState(target, MobState.Critical);
                AssertVerbs(SPlayer, target, "critical target", RemoveTop, RemoveBottom);
                mobState.ChangeMobState(target, MobState.Dead);
                AssertVerbs(SPlayer, target, "dead target");
                mobState.ChangeMobState(target, MobState.Alive);

                // Undergarments worn.
                SetUndergarments(target, top: false, bottom: true);
                AssertVerbs(SPlayer, target, "only a bottom undergarment", RemoveBottom);
                SetUndergarments(target, top: false, bottom: false);
                AssertVerbs(SPlayer, target, "no undergarments");
                SetUndergarments(target, top: true, bottom: true);

                AssertVerbs(SPlayer, target, "allowed again", RemoveTop, RemoveBottom);
            });
        });

        try
        {
            await Server.WaitPost(() => Server.CfgMan.SetCVar(WolfgateCVars.AnatomyEnabled, false));
            await Server.WaitAssertion(() => AssertVerbs(SPlayer, target, "kill switch off"));
        }
        finally
        {
            await Server.WaitPost(() => Server.CfgMan.SetCVar(WolfgateCVars.AnatomyEnabled, true));
        }

        // The client's own handlers (prediction) agree.
        await RunTicks(5);
        await Client.WaitAssertion(() =>
            Assert.That(Ids(ClientVerbs(), _cLoc), Is.EquivalentTo(new[] { RemoveTop, RemoveBottom }),
                "The client must offer the same verbs."));

        await Server.WaitPost(() => GenitalTestHelpers.GrantConsent(SEntMan, target));
        await RunTicks(5);
        await Client.WaitAssertion(() =>
            Assert.That(ClientVerbs(), Is.Empty, "The client must drop the verbs once the target turns UndergarmentStrip off."));
    }

    /// <summary>Clothing over the region and the removal cooldown disable the verbs with a reason; put-back ignores the cooldown.</summary>
    [Test]
    public async Task DisabledVerbsTest()
    {
        var target = await SpawnConsentingTarget();
        var inventory = SEntMan.System<InventorySystem>();

        await Server.WaitAssertion(() =>
        {
            var jumpsuit = SEntMan.SpawnEntity(Jumpsuit, SEntMan.GetCoordinates(TargetCoords));
            var vest = SEntMan.SpawnEntity(Vest, SEntMan.GetCoordinates(TargetCoords));
            var genitals = SEntMan.GetComponent<GenitalsComponent>(target);

            Assert.Multiple(() =>
            {
                // A jumpsuit covers both regions.
                Assert.That(inventory.TryEquip(target, jumpsuit, JumpsuitSlot, silent: true, force: true), "Could not equip the jumpsuit.");
                AssertStates(SPlayer, target, "jumpsuit", (RemoveTop, FailCovered), (RemoveBottom, FailCovered));
                Assert.That(_strip.TryStartStrip(SPlayer, target, UndergarmentSlot.Bottom, true), Is.False,
                    "A covered undergarment cannot be removed.");
                Assert.That(inventory.TryUnequip(target, JumpsuitSlot, silent: true, force: true), "Could not remove the jumpsuit.");

                // A vest covers the chest only.
                Assert.That(inventory.TryEquip(target, vest, OuterSlot, silent: true, force: true), "Could not equip the vest.");
                AssertStates(SPlayer, target, "vest", (RemoveTop, FailCovered), (RemoveBottom, null));
                Assert.That(inventory.TryUnequip(target, OuterSlot, silent: true, force: true), "Could not remove the vest.");

                // The cooldown blocks removals by anyone, never a put-back.
                genitals.StripCooldownUntil = STiming.CurTime + TimeSpan.FromSeconds(30);
                AssertStates(SPlayer, target, "cooldown", (RemoveTop, FailCooldown), (RemoveBottom, FailCooldown));
                Assert.That(_strip.TryStartStrip(SPlayer, target, UndergarmentSlot.Bottom, true), Is.False,
                    "Removal during the cooldown.");

                genitals.Undergarments = UndergarmentFlags.TopRemoved | UndergarmentFlags.TopByOther;
                AssertStates(SPlayer, target, "cooldown after a removal", (ReplaceTop, null), (RemoveBottom, FailCooldown));
            });

            Assert.That(ActiveDoAfters, Is.Empty, "A blocked action must not start a DoAfter.");
        });
    }

    /// <summary>
    /// The player removes and puts back the target's bottom through the client's predicted verbs. Removal sets Removed and
    /// ByOther and starts the cooldown; put-back clears both bits and leaves the cooldown. The client receives the flags.
    /// </summary>
    [Test]
    public async Task CompletionTest()
    {
        var target = await SpawnConsentingTarget();
        var genitals = SEntMan.GetComponent<GenitalsComponent>(target);
        var removed = UndergarmentFlags.BottomRemoved | UndergarmentFlags.BottomByOther;

        await ClientExecute(RemoveBottom);
        await AwaitStrip();

        var cooldown = TimeSpan.Zero;
        await Server.WaitAssertion(() =>
        {
            var humanoid = SEntMan.GetComponent<HumanoidAppearanceComponent>(target);
            var coverage = SEntMan.System<GenitalCoverageSystem>().GetCoverage((target, genitals, humanoid));

            Assert.Multiple(() =>
            {
                Assert.That(genitals.Undergarments, Is.EqualTo(removed), "Removal sets Removed and ByOther.");
                Assert.That(genitals.StripCooldownUntil, Is.GreaterThan(STiming.CurTime), "A removal by another player starts the cooldown.");
                Assert.That(_strip.GetRemovalCount(SPlayer, target), Is.EqualTo(1), "The server counts the removal once.");
                Assert.That(coverage.Undergarments, Is.EqualTo(GenitalRegion.Chest), "The removed bottom no longer covers the groin.");
                AssertStates(SPlayer, target, "after the removal", (RemoveTop, FailCooldown), (ReplaceBottom, null));
            });

            cooldown = genitals.StripCooldownUntil;
        });

        await Client.WaitAssertion(() =>
            Assert.That(CEntMan.GetComponent<GenitalsComponent>(CTarget!.Value).Undergarments, Is.EqualTo(removed),
                "The client must receive the removal."));

        await ClientExecute(ReplaceBottom);
        await AwaitStrip();

        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(genitals.Undergarments, Is.EqualTo(UndergarmentFlags.None), "Put-back clears Removed and ByOther.");
                Assert.That(genitals.StripCooldownUntil, Is.EqualTo(cooldown), "A put-back by another player leaves the cooldown alone.");
                Assert.That(_strip.GetRemovalCount(SPlayer, target), Is.EqualTo(1), "A put-back is not a removal.");
            });
        });

        await Client.WaitAssertion(() =>
            Assert.That(CEntMan.GetComponent<GenitalsComponent>(CTarget!.Value).Undergarments, Is.EqualTo(UndergarmentFlags.None),
                "The client must receive the put-back."));
    }

    /// <summary>Turning UndergarmentStrip off puts back only what others removed; turning adult content off puts back everything.</summary>
    [Test]
    public async Task RevocationTest()
    {
        var target = await SpawnConsentingTarget();
        var genitals = SEntMan.GetComponent<GenitalsComponent>(target);

        await ServerStart(target, UndergarmentSlot.Bottom, true);
        await AwaitStrip();

        await Server.WaitAssertion(() =>
        {
            Assert.That(genitals.Undergarments, Is.EqualTo(UndergarmentFlags.BottomRemoved | UndergarmentFlags.BottomByOther),
                "Precondition: the player removed the bottom.");

            // The owner's own removal (the Anatomy panel path) carries no ByOther bit.
            genitals.Undergarments |= UndergarmentFlags.TopRemoved;

            GenitalTestHelpers.GrantConsent(SEntMan, target);
            Assert.That(genitals.Undergarments, Is.EqualTo(UndergarmentFlags.TopRemoved),
                "Turning UndergarmentStrip off must put back only the removal by another player.");

            GenitalTestHelpers.GrantConsent(SEntMan, target, GenitalTestHelpers.StripToggle);
            Assert.That(genitals.Undergarments, Is.EqualTo(UndergarmentFlags.TopRemoved),
                "Consent that still allows removal puts nothing back.");
        });

        await RunTicks(5);
        await Client.WaitAssertion(() =>
            Assert.That(CEntMan.GetComponent<GenitalsComponent>(CTarget!.Value).Undergarments, Is.EqualTo(UndergarmentFlags.TopRemoved),
                "The client must receive the put-back."));

        await Server.WaitAssertion(() =>
        {
            genitals.Undergarments |= UndergarmentFlags.BottomRemoved | UndergarmentFlags.BottomByOther;
            GenitalTestHelpers.SetConsent(SEntMan, target, GenitalTestHelpers.StripToggle);
            Assert.That(genitals.Undergarments, Is.EqualTo(UndergarmentFlags.None),
                "Turning adult content off must put back every removal, the owner's own included.");
        });

        await RunTicks(5);
        await Client.WaitAssertion(() =>
            Assert.That(CEntMan.GetComponent<GenitalsComponent>(CTarget!.Value).Undergarments, Is.EqualTo(UndergarmentFlags.None),
                "The client must receive the put-back."));
    }

    /// <summary>
    /// A running removal cancels, changing nothing, when the target puts on a jumpsuit, turns UndergarmentStrip off or dies,
    /// and the actor's client is told why.
    /// </summary>
    [Test]
    public async Task CancellationTest()
    {
        var target = await SpawnConsentingTarget();
        var inventory = SEntMan.System<InventorySystem>();
        var mobState = SEntMan.System<MobStateSystem>();

        EntityUid jumpsuit = default;
        await Server.WaitPost(() => jumpsuit = SEntMan.SpawnEntity(Jumpsuit, SEntMan.GetCoordinates(TargetCoords)));

        await ServerStart(target, UndergarmentSlot.Bottom, true);
        await AssertCancelledBy(target, "a jumpsuit put on", FailCovered,
            () => Assert.That(inventory.TryEquip(target, jumpsuit, JumpsuitSlot, silent: true, force: true), "Could not equip the jumpsuit."));
        await Server.WaitAssertion(() =>
            Assert.That(inventory.TryUnequip(target, JumpsuitSlot, silent: true, force: true), "Could not remove the jumpsuit."));

        await ServerStart(target, UndergarmentSlot.Bottom, true);
        await AssertCancelledBy(target, "UndergarmentStrip turned off", FailConsent, () => GenitalTestHelpers.GrantConsent(SEntMan, target));
        await Server.WaitPost(() => GenitalTestHelpers.GrantConsent(SEntMan, target, GenitalTestHelpers.StripToggle));

        await ServerStart(target, UndergarmentSlot.Bottom, true);
        await AssertCancelledBy(target, "the target dying", FailConsent, () => mobState.ChangeMobState(target, MobState.Dead));
    }

    /// <summary>
    /// With the client's session in the target: detaching it (SSD, mind kept) hides the verbs and cancels a running removal,
    /// reattaching restores them, and ghosting clears the toggles and puts the undergarments back one tick later.
    /// </summary>
    [Test]
    public async Task TargetSessionTest()
    {
        var target = await SpawnConsentingTarget();
        var actor = SPlayer;
        var genitals = SEntMan.GetComponent<GenitalsComponent>(target);
        var minds = SEntMan.System<SharedMindSystem>();

        // The client's player takes the target over with a new mind; ConsentSystem gives it the saved toggles (none).
        EntityUid mind = default;
        await Server.WaitPost(() =>
        {
            mind = minds.CreateMind(ServerSession.UserId).Owner;
            minds.TransferTo(mind, target);
        });

        await RunTicks(5);
        await Server.WaitAssertion(() =>
        {
            Assert.That(ServerSession.AttachedEntity, Is.EqualTo(target), "Precondition: the session controls the target.");
            GenitalTestHelpers.GrantConsent(SEntMan, target, GenitalTestHelpers.StripToggle);
        });

        await RunTicks(2);
        await Server.WaitAssertion(() => AssertVerbs(actor, target, "present player", RemoveTop, RemoveBottom));

        // Session detached, mind kept: the body is SSD.
        await Server.WaitPost(() => GenitalTestHelpers.Detach(Server.PlayerMan, ServerSession));
        await RunTicks(2);
        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(SEntMan.GetComponent<SSDIndicatorComponent>(target).IsSSD, Is.True, "Precondition: the detached body is SSD.");
                Assert.That(SEntMan.GetComponent<MindContainerComponent>(target).Mind, Is.EqualTo(mind), "Precondition: the mind stays.");
                AssertVerbs(actor, target, "SSD target (session detached, mind kept)");
            });
        });

        await Server.WaitPost(() => Server.PlayerMan.SetAttachedEntity(ServerSession, target));
        await RunTicks(2);
        await Server.WaitAssertion(() => AssertVerbs(actor, target, "reattached player", RemoveTop, RemoveBottom));

        // The target disconnects during the removal.
        await ServerStart(target, UndergarmentSlot.Bottom, true);
        await AssertCancelledBy(target, "the target disconnecting", null, () => GenitalTestHelpers.Detach(Server.PlayerMan, ServerSession));

        // Back, and removed for real; then ghosting clears the toggles.
        await Server.WaitPost(() => Server.PlayerMan.SetAttachedEntity(ServerSession, target));
        await RunTicks(2);
        await ServerStart(target, UndergarmentSlot.Bottom, true);
        await AwaitStrip();

        await Server.WaitAssertion(() =>
        {
            Assert.That(genitals.Undergarments, Is.EqualTo(UndergarmentFlags.BottomRemoved | UndergarmentFlags.BottomByOther),
                "Precondition: the player removed the bottom.");

            var ghost = SEntMan.SpawnEntity(GameTicker.ObserverPrototypeName, SEntMan.GetComponent<TransformComponent>(target).Coordinates);
            minds.TransferTo(mind, ghost);
            AssertVerbs(actor, target, "ghosted target (toggles cleared)");
        });

        await RunTicks(2);
        await Server.WaitAssertion(() =>
            Assert.That(genitals.Undergarments, Is.EqualTo(UndergarmentFlags.None), "Ghosting must put back every removal."));
    }

    /// <summary>
    /// A third client in range with adult content on receives none of the strip popups. Moved into the target, the same
    /// client receives exactly the target's lines, which shows the probe works and that the actor's lines never leave the actor.
    /// </summary>
    [Test]
    public async Task BystanderPopupTest()
    {
        var target = await SpawnConsentingTarget();
        var actor = SPlayer;
        var probe = CEntMan.System<UndergarmentPopupProbeSystem>();

        var bystanderCoords = SEntMan.GetNetCoordinates(SEntMan.GetCoordinates(TargetCoords).Offset(new Vector2(0f, 1f)));
        await SetTile(Plating, bystanderCoords, MapData.Grid);

        EntityUid bystander = default;
        await Server.WaitPost(() =>
        {
            bystander = GenitalConsentTestHelpers.SpawnPresentAdult(SEntMan, SEntMan.GetCoordinates(bystanderCoords));
            GenitalTestHelpers.GrantConsent(SEntMan, bystander, GenitalTestHelpers.StripToggle);
            Server.PlayerMan.SetAttachedEntity(ServerSession, bystander);
        });

        await RunTicks(5);
        await Client.WaitAssertion(() =>
        {
            Assert.That(Client.AttachedEntity, Is.EqualTo(ToClient(bystander)), "Precondition: the client is the bystander.");
            Assert.That(CEntMan.EntityExists(ToClient(target)), Is.True, "Precondition: the bystander has the target in view.");
            probe.Received.Clear();
        });

        // Remove and put back the top, next to the bystander.
        await ServerStart(target, UndergarmentSlot.Top, true);
        await AwaitStrip();
        await ServerStart(target, UndergarmentSlot.Top, false);
        await AwaitStrip();
        await RunTicks(5);

        await Server.WaitAssertion(() =>
            Assert.That(SEntMan.GetComponent<GenitalsComponent>(target).Undergarments, Is.EqualTo(UndergarmentFlags.None),
                "Precondition: both actions completed."));
        await Client.WaitAssertion(() =>
            Assert.That(probe.Received.Where(IsStripText), Is.Empty, "A bystander must not receive any strip popup."));

        // The same client as the target, with the cooldown cleared.
        var expected = new List<string>();
        await Server.WaitPost(() =>
        {
            SEntMan.GetComponent<GenitalsComponent>(target).StripCooldownUntil = TimeSpan.Zero;
            Server.PlayerMan.SetAttachedEntity(ServerSession, target);

            var user = Identity.Entity(actor, SEntMan);
            foreach (var id in new[]
                     {
                         "wf-undergarment-start-target",
                         "wf-undergarment-done-target",
                         "wf-undergarment-replace-start-target",
                         "wf-undergarment-replace-done-target",
                     })
            {
                expected.Add(_sLoc.GetString(id, ("user", user)));
            }
        });

        await RunTicks(5);
        await Client.WaitPost(() => probe.Received.Clear());

        await ServerStart(target, UndergarmentSlot.Top, true);
        await AwaitStrip();
        await ServerStart(target, UndergarmentSlot.Top, false);
        await AwaitStrip();
        await RunTicks(5);

        await Client.WaitAssertion(() =>
            Assert.That(probe.Received.Where(IsStripText), Is.EqualTo(expected),
                "The target must receive exactly its own four lines, and none of the actor's."));
    }

    /// <summary>
    /// Makes the player an adult with adult content on (the actor), and spawns a present adult target one tile away with
    /// adult content, UndergarmentStrip, anatomy and both undergarments.
    /// </summary>
    private async Task<EntityUid> SpawnConsentingTarget()
    {
        EntityUid target = default;
        await Server.WaitAssertion(() =>
        {
            SetAge(SPlayer, 30);
            GenitalTestHelpers.GrantConsent(SEntMan, SPlayer);

            target = GenitalConsentTestHelpers.SpawnPresentAdult(SEntMan, SEntMan.GetCoordinates(TargetCoords));
            GenitalTestHelpers.GrantConsent(SEntMan, target, GenitalTestHelpers.StripToggle);
            SEntMan.EnsureComponent<GenitalsComponent>(target);
            SetUndergarments(target, top: true, bottom: true);
        });

        Target = SEntMan.GetNetEntity(target);
        await RunTicks(5);
        return target;
    }

    /// <summary>Server thread only.</summary>
    private void SetAge(EntityUid uid, int age)
    {
        var humanoid = SEntMan.GetComponent<HumanoidAppearanceComponent>(uid);
        humanoid.Age = age;
        SEntMan.Dirty(uid, humanoid);
    }

    /// <summary>Replaces the undergarment markings. Server thread only.</summary>
    private void SetUndergarments(EntityUid uid, bool top, bool bottom)
    {
        var humanoids = SEntMan.System<SharedHumanoidAppearanceSystem>();
        var humanoid = SEntMan.GetComponent<HumanoidAppearanceComponent>(uid);
        humanoid.MarkingSet.RemoveCategory(MarkingCategories.UndergarmentTop);
        humanoid.MarkingSet.RemoveCategory(MarkingCategories.UndergarmentBottom);

        if (top)
            humanoids.AddMarking(uid, TopMarking, forced: true, humanoid: humanoid);

        if (bottom)
            humanoids.AddMarking(uid, BottomMarking, forced: true, humanoid: humanoid);

        SEntMan.Dirty(uid, humanoid);
        Assert.That(humanoid.MarkingSet.TryGetMarking(MarkingCategories.UndergarmentTop, TopMarking, out _), Is.EqualTo(top),
            $"Marking {TopMarking}.");
        Assert.That(humanoid.MarkingSet.TryGetMarking(MarkingCategories.UndergarmentBottom, BottomMarking, out _), Is.EqualTo(bottom),
            $"Marking {BottomMarking}.");
    }

    /// <summary>Undergarment verbs the server's handlers build for the user on the target. Server thread only.</summary>
    private List<Verb> ServerVerbs(EntityUid user, EntityUid target)
    {
        return SEntMan.System<SharedVerbSystem>().GetLocalVerbs(target, user, typeof(Verb))
            .Where(v => v.Category == SharedUndergarmentStripSystem.UndergarmentCategory)
            .ToList();
    }

    /// <summary>Undergarment verbs the client's own handlers build for the player on the target. Client thread only.</summary>
    private List<Verb> ClientVerbs()
    {
        return CEntMan.System<SharedVerbSystem>().GetLocalVerbs(CTarget!.Value, CPlayer, typeof(Verb))
            .Where(v => v.Category == SharedUndergarmentStripSystem.UndergarmentCategory)
            .ToList();
    }

    /// <summary>Loc ids of the verbs; a text that matches no id is kept, so a failure shows it.</summary>
    private static List<string> Ids(IEnumerable<Verb> verbs, ILocalizationManager loc)
    {
        return verbs.Select(v => VerbIds.FirstOrDefault(id => loc.GetString(id) == v.Text) ?? v.Text).ToList();
    }

    /// <summary>Asserts exactly these verbs, all enabled. Server thread only.</summary>
    private void AssertVerbs(EntityUid user, EntityUid target, string row, params string[] expected)
    {
        var verbs = ServerVerbs(user, target);
        Assert.That(Ids(verbs, _sLoc), Is.EquivalentTo(expected), row);
        Assert.That(verbs.Where(v => v.Disabled).Select(v => v.Text), Is.Empty, $"{row}: no verb may be disabled.");
    }

    /// <summary>Asserts exactly these verbs; a null reason means enabled, otherwise disabled with that message. Server thread only.</summary>
    private void AssertStates(EntityUid user, EntityUid target, string row, params (string Id, string? Reason)[] expected)
    {
        var verbs = ServerVerbs(user, target);
        Assert.That(Ids(verbs, _sLoc), Is.EquivalentTo(expected.Select(e => e.Id)), row);

        foreach (var (id, reason) in expected)
        {
            var verb = verbs.FirstOrDefault(v => v.Text == _sLoc.GetString(id));
            if (verb == null)
                continue;

            Assert.That(verb.Disabled, Is.EqualTo(reason != null), $"{row}: {id} disabled");
            Assert.That(verb.Message, Is.EqualTo(reason == null ? null : _sLoc.GetString(reason)), $"{row}: {id} message");
        }
    }

    /// <summary>Starts a strip by the player on the server, as the verb's action does.</summary>
    private async Task ServerStart(EntityUid target, UndergarmentSlot slot, bool remove)
    {
        await Server.WaitAssertion(() =>
            Assert.That(_strip.TryStartStrip(SPlayer, target, slot, remove), Is.True, $"Could not start ({slot}, remove: {remove})."));
    }

    /// <summary>Executes a verb from the client as the context menu does: predicted locally, then run by the server.</summary>
    private async Task ClientExecute(string id)
    {
        await Client.WaitAssertion(() =>
        {
            var verb = ClientVerbs().FirstOrDefault(v => v.Text == _cLoc.GetString(id));
            Assert.That(verb, Is.Not.Null, $"The client offers no {id} verb.");
            Assert.That(verb!.Disabled, Is.False, $"{id} is disabled on the client.");
            CEntMan.System<ClientVerbSystem>().ExecuteVerb(CTarget!.Value, verb!);
        });
    }

    /// <summary>Waits for the server to run the player's strip DoAfter, which must finish uncancelled.</summary>
    private async Task AwaitStrip()
    {
        await RunTicks(5);
        Assert.That(ActiveDoAfters.Count(), Is.EqualTo(1), "The server is not running the strip DoAfter.");
        await AwaitDoAfters();
    }

    /// <summary>
    /// Lets the started DoAfter run for a second, applies the change on the server, and checks it cancelled and changed
    /// nothing. With a reason, the player's client (the actor) must receive exactly that reason once.
    /// </summary>
    private async Task AssertCancelledBy(EntityUid target, string what, string? reason, Action change)
    {
        await RunTicks(3);
        Assert.That(ActiveDoAfters.Count(), Is.EqualTo(1), $"{what}: the strip DoAfter did not start.");
        var doAfter = ActiveDoAfters.Single();
        var genitals = SEntMan.GetComponent<GenitalsComponent>(target);
        var before = genitals.Undergarments;
        var probe = CEntMan.System<UndergarmentPopupProbeSystem>();

        await RunSeconds(1);
        await Client.WaitPost(() => probe.Received.Clear());
        await Server.WaitAssertion(change);
        await RunTicks(3);
        Assert.Multiple(() =>
        {
            Assert.That(doAfter.Cancelled, Is.True, $"{what} must cancel the DoAfter.");
            Assert.That(ActiveDoAfters, Is.Empty, $"{what}: no DoAfter may remain.");
        });

        await RunSeconds(_stripDelay);
        Assert.That(genitals.Undergarments, Is.EqualTo(before), $"{what}: the undergarments must not change.");

        if (reason == null)
            return;

        var text = _cLoc.GetString(reason);
        await Client.WaitAssertion(() =>
            Assert.That(probe.Received.Count(m => m == text), Is.EqualTo(1), $"{what}: the actor must be told why, once ({text})."));
    }

    private static bool IsStripText(string message)
    {
        return message.Contains("undergarment", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Every anatomy-eligible roundstart species can wear an undergarment top and bottom, and its body draws them, so the
/// UndergarmentStrip toggle protects something. Exactly the listed exceptions (no fitting art) have none.
/// </summary>
[TestFixture]
[TestOf(typeof(SharedUndergarmentStripSystem))]
public sealed class UndergarmentOptionsTest
{
    /// <summary>Species whose bodies no undergarment art fits; the creator warns them.</summary>
    private static readonly string[] Exceptions = { "Resomi", "ProtoResomi" };

    private static readonly MarkingCategories[] Categories = { MarkingCategories.UndergarmentTop, MarkingCategories.UndergarmentBottom };

    [Test]
    public async Task EligibleSpeciesHaveUndergarmentsTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var proto = server.ResolveDependency<IPrototypeManager>();
        var markings = server.ResolveDependency<MarkingManager>();

        await server.WaitAssertion(() =>
        {
            var settings = server.System<SharedGenitalsSystem>().Settings;
            var without = new List<string>();

            Assert.Multiple(() =>
            {
                foreach (var species in proto.EnumeratePrototypes<SpeciesPrototype>())
                {
                    if (!species.RoundStart || pair.IsTestPrototype(species) || settings.ExcludedSpecies.Contains(species.ID))
                        continue;

                    var counts = Categories.Select(c => markings.MarkingsByCategoryAndSpecies(c, species.ID).Count).ToArray();
                    if (counts.All(c => c == 0))
                    {
                        without.Add(species.ID);
                        continue;
                    }

                    var sprites = proto.Index<HumanoidSpeciesBaseSpritesPrototype>(species.SpriteSet).Sprites;
                    var points = proto.Index<MarkingPointsPrototype>(species.MarkingPoints).Points;
                    for (var i = 0; i < Categories.Length; i++)
                    {
                        var category = Categories[i];
                        Assert.That(counts[i], Is.GreaterThan(0), $"{species.ID} has no {category} marking.");
                        Assert.That(sprites.ContainsKey(category == MarkingCategories.UndergarmentTop
                                ? HumanoidVisualLayers.UndergarmentTop
                                : HumanoidVisualLayers.UndergarmentBottom),
                            $"{species.ID}: base sprites {species.SpriteSet} lack the {category} layer, so the marking never draws.");
                        if (points.TryGetValue(category, out var limit))
                            Assert.That(limit.Points, Is.GreaterThan(0), $"{species.ID}: {species.MarkingPoints} allows no {category}.");
                    }
                }
            });

            Assert.That(without, Is.EquivalentTo(Exceptions),
                "Exactly the species in Exceptions may lack undergarments; update that list when undergarment art changes.");
        });

        await pair.CleanReturnAsync();
    }
}

/// <summary>Test-only: records the text of every popup the client receives from the server.</summary>
public sealed partial class UndergarmentPopupProbeSystem : EntitySystem
{
    [Dependency] private INetManager _net = default!;

    public readonly List<string> Received = new();

    public override void Initialize()
    {
        base.Initialize();

        // Server-to-client events: only the client listens.
        if (!_net.IsClient)
            return;

        SubscribeNetworkEvent<PopupEntityEvent>(OnPopup);
        SubscribeNetworkEvent<PopupCursorEvent>(OnPopup);
        SubscribeNetworkEvent<PopupCoordinatesEvent>(OnPopup);
    }

    private void OnPopup(PopupEvent ev)
    {
        Received.Add(ev.Message);
    }
}
