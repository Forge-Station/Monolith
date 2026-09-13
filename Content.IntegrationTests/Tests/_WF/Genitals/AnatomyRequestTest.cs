using Content.IntegrationTests.Pair;
using Content.Server._WF.Genitals;
using Content.Shared._WF.Genitals;
using Content.Shared._WF.Genitals.Components;
using Content.Shared._WF.Genitals.Systems;
using Content.Shared.Humanoid;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Shared.GameObjects;
using static Content.IntegrationTests.Tests._WF.Genitals.GenitalTestHelpers;

namespace Content.IntegrationTests.Tests._WF.Genitals;

/// <summary>
/// Owner requests go from the client through RaisePredictiveEvent, are predicted there and validated again on the
/// server: master switch, death, the value range and the rate limit per request type and slot (latest wins for arousal).
/// </summary>
[TestFixture]
[TestOf(typeof(SharedGenitalsSystem))]
public sealed class AnatomyRequestTest
{
    private const string TopMarking = "UndergarmentTopTanktop";
    private const string BottomMarking = "UndergarmentBottomBoxers";

    /// <summary>Several of the server's RequestIntervals (0.1 s), so every request budget is full again before the next send.</summary>
    private const float RequestGap = 0.3f;

    /// <summary>Ticks for a request to reach the server and its state to come back.</summary>
    private const int RoundTripTicks = 15;

    [Test]
    public async Task ArousalRequestTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;
        var sEntMan = server.EntMan;
        var cEntMan = client.EntMan;
        var visuals = client.System<AnatomyVisualsProbeSystem>();

        var (body, clientBody) = await SetUpPlayer(pair, FullProfile());

        // Predicted: the client changes at once and raises the visuals event itself; the server then applies it.
        await client.WaitPost(() => cEntMan.EnsureComponent<AnatomyVisualsProbeComponent>(clientBody));
        await pair.RunTicksSync(5);
        await client.WaitAssertion(() =>
        {
            Assert.That(client.Timing.InPrediction, Is.True, "Precondition: the client is predicting ahead of the server.");
            visuals.Raised = 0;
            cEntMan.RaisePredictiveEvent(new AnatomySetArousalRequestEvent(50));
            Assert.That(cEntMan.GetComponent<GenitalsComponent>(clientBody).Arousal, Is.EqualTo(50), "The client did not predict the change.");
            Assert.That(visuals.Raised, Is.GreaterThan(0), "The predicting client must raise GenitalsVisualsChangedEvent.");
        });

        await pair.RunTicksSync(RoundTripTicks);
        await server.WaitAssertion(() =>
            Assert.That(ServerGenitals(sEntMan, body).Arousal, Is.EqualTo(50), "The request did not reach the server."));

        await Send(pair, new AnatomySetArousalRequestEvent(200));
        await server.WaitAssertion(() =>
            Assert.That(ServerGenitals(sEntMan, body).Arousal, Is.EqualTo(100), "Values above 100 must clamp."));

        // One request more in a tick than RequestBurst allows: the server applies the first RequestBurst at once and the
        // last once the budget allows, so the final value is never lost, and the client follows the server.
        var burst = sEntMan.System<SharedGenitalsSystem>().Settings.RequestBurst;
        var last = (byte) (10 * (burst + 1));
        await pair.RunSeconds(RequestGap);
        await client.WaitPost(() =>
        {
            for (var i = 1; i <= burst + 1; i++)
            {
                cEntMan.RaisePredictiveEvent(new AnatomySetArousalRequestEvent((byte) (10 * i)));
            }
        });
        await pair.RunTicksSync(RoundTripTicks);
        await server.WaitAssertion(() =>
            Assert.That(ServerGenitals(sEntMan, body).Arousal, Is.EqualTo(last), "The last request beyond RequestBurst must wait, not be dropped."));
        await client.WaitAssertion(() =>
            Assert.That(cEntMan.GetComponent<GenitalsComponent>(clientBody).Arousal, Is.EqualTo(last), "The client must follow the server."));

        // Master switch off: the reset sets 0, and requests change nothing.
        await GenitalConsentTestHelpers.SetPlayerConsent(pair);
        await server.WaitAssertion(() =>
            Assert.That(ServerGenitals(sEntMan, body).Arousal, Is.EqualTo(0), "Turning the master switch off resets arousal."));
        await Send(pair, new AnatomySetArousalRequestEvent(40));
        await server.WaitAssertion(() =>
            Assert.That(ServerGenitals(sEntMan, body).Arousal, Is.EqualTo(0), "A request without the master switch must be refused."));

        // Dead: refused.
        await GenitalConsentTestHelpers.SetPlayerConsent(pair, MasterToggle);
        await Send(pair, new AnatomySetArousalRequestEvent(40));
        await server.WaitAssertion(() =>
            Assert.That(ServerGenitals(sEntMan, body).Arousal, Is.EqualTo(40), "Precondition: requests work again with the master switch."));
        await server.WaitPost(() => sEntMan.System<MobStateSystem>().ChangeMobState(body, MobState.Dead));
        await Send(pair, new AnatomySetArousalRequestEvent(70));
        await server.WaitAssertion(() =>
            Assert.That(ServerGenitals(sEntMan, body).Arousal, Is.EqualTo(0), "A request while dead must be refused."));

        await pair.CleanReturnAsync();
    }

    /// <summary>Reveal mode, visibility and undergarment requests: applied for the sender's own organs and markings, ignored otherwise.</summary>
    [Test]
    public async Task OtherRequestsTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var sEntMan = server.EntMan;
        var cEntMan = pair.Client.EntMan;

        // No breasts, so a breasts visibility request has no organ to act on.
        var anatomy = GenitalProfile.Empty
            .WithPenis(new PenisProfile(PenisHuman))
            .WithVagina(new VaginaProfile(VaginaHuman));
        var (body, _) = await SetUpPlayer(pair, anatomy);

        await Send(pair, new AnatomySetRevealModeRequestEvent(GenitalRevealMode.ClothingRemoval));
        await server.WaitAssertion(() =>
            Assert.That(ServerGenitals(sEntMan, body).RevealMode, Is.EqualTo(GenitalRevealMode.ClothingRemoval)));

        await Send(pair, new AnatomySetVisibilityRequestEvent(GenitalSlot.Penis, GenitalVisibility.ShowThroughClothing));
        await server.WaitAssertion(() =>
            Assert.That(ServerGenitals(sEntMan, body).Visibility.Penis, Is.EqualTo(GenitalVisibility.ShowThroughClothing)));

        await Send(pair, new AnatomySetVisibilityRequestEvent(GenitalSlot.Breasts, GenitalVisibility.AlwaysHidden));
        await server.WaitAssertion(() =>
            Assert.That(ServerGenitals(sEntMan, body).Visibility.Breasts, Is.EqualTo(GenitalVisibility.Normal), "A slot without an organ must be ignored."));

        // Budgets are per slot: spending the penis row's whole budget in one tick leaves the vagina row's request alone.
        var burst = sEntMan.System<SharedGenitalsSystem>().Settings.RequestBurst;
        var lastPenis = (burst - 1) % 2 == 0 ? GenitalVisibility.Normal : GenitalVisibility.ShowThroughClothing;
        await pair.RunSeconds(RequestGap);
        await pair.Client.WaitPost(() =>
        {
            for (var i = 0; i < burst; i++)
            {
                var visibility = i % 2 == 0 ? GenitalVisibility.Normal : GenitalVisibility.ShowThroughClothing;
                cEntMan.RaisePredictiveEvent(new AnatomySetVisibilityRequestEvent(GenitalSlot.Penis, visibility));
            }

            cEntMan.RaisePredictiveEvent(new AnatomySetVisibilityRequestEvent(GenitalSlot.Vagina, GenitalVisibility.AlwaysHidden));
        });
        await pair.RunTicksSync(RoundTripTicks);
        await server.WaitAssertion(() =>
        {
            var genitals = ServerGenitals(sEntMan, body);
            Assert.That(genitals.Visibility.Penis, Is.EqualTo(lastPenis), "A burst within RequestBurst must be applied in full.");
            Assert.That(genitals.Visibility.Vagina, Is.EqualTo(GenitalVisibility.AlwaysHidden),
                "Another slot's request must not share the penis row's budget.");
        });

        // Without an undergarment marking there is nothing to remove.
        await Send(pair, new AnatomySetUndergarmentRequestEvent(UndergarmentSlot.Top, false));
        await server.WaitAssertion(() =>
            Assert.That(ServerGenitals(sEntMan, body).Undergarments, Is.EqualTo(UndergarmentFlags.None), "No top is worn."));

        await server.WaitPost(() =>
        {
            var humanoids = sEntMan.System<SharedHumanoidAppearanceSystem>();
            var humanoid = sEntMan.GetComponent<HumanoidAppearanceComponent>(body);
            humanoids.AddMarking(body, TopMarking, forced: true, humanoid: humanoid);
            humanoids.AddMarking(body, BottomMarking, forced: true, humanoid: humanoid);
            sEntMan.Dirty(body, humanoid);
        });

        await Send(pair, new AnatomySetUndergarmentRequestEvent(UndergarmentSlot.Top, false));
        await server.WaitAssertion(() =>
            Assert.That(ServerGenitals(sEntMan, body).Undergarments, Is.EqualTo(UndergarmentFlags.TopRemoved),
                "The owner's removal sets only the Removed bit."));

        // A removal by another player, then the owner's put-back.
        await server.WaitPost(() =>
        {
            var genitals = ServerGenitals(sEntMan, body);
            genitals.Undergarments |= UndergarmentFlags.BottomRemoved | UndergarmentFlags.BottomByOther;
            sEntMan.Dirty(body, genitals);
        });

        await Send(pair, new AnatomySetUndergarmentRequestEvent(UndergarmentSlot.Bottom, true));
        await server.WaitAssertion(() =>
        {
            var genitals = ServerGenitals(sEntMan, body);
            Assert.That(genitals.Undergarments, Is.EqualTo(UndergarmentFlags.TopRemoved), "A put-back clears Removed and ByOther.");
            Assert.That(genitals.StripCooldownUntil, Is.GreaterThan(server.Timing.CurTime), "A put-back starts the strip cooldown.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Puts the client's player in a fresh adult body with Adult content on and the given organs; waits until the client sees them.</summary>
    private static async Task<(EntityUid Server, EntityUid Client)> SetUpPlayer(TestPair pair, GenitalProfile anatomy)
    {
        var server = pair.Server;
        var player = await GenitalConsentTestHelpers.AttachToNewBody(pair);
        var body = player.Body;

        await GenitalConsentTestHelpers.SetPlayerConsent(pair, MasterToggle);
        await GenitalConsentTestHelpers.WaitFor(pair,
            () => server.EntMan.HasComponent<GenitalsComponent>(body),
            "the opted-in body to get anatomy");

        await server.WaitPost(() => server.EntMan.System<GenitalOrganSystem>().BuildOrgans(body, anatomy));

        var clientBody = pair.ToClientUid(body);
        var cEntMan = pair.Client.EntMan;
        var clientConsent = pair.Client.System<GenitalConsentSystem>();
        await GenitalConsentTestHelpers.WaitFor(pair,
            () => cEntMan.TryGetComponent<GenitalsComponent>(clientBody, out var genitals)
                  && genitals.Penis != null
                  && clientConsent.HasMaster(clientBody),
            "the client to see its own anatomy and master switch");

        return (body, clientBody);
    }

    /// <summary>Waits past the request interval, sends one predicted request from the client and lets it round-trip.</summary>
    private static async Task Send<T>(TestPair pair, T request) where T : EntityEventArgs
    {
        await pair.RunSeconds(RequestGap);
        await pair.Client.WaitPost(() => pair.Client.EntMan.RaisePredictiveEvent(request));
        await pair.RunTicksSync(RoundTripTicks);
    }

    private static GenitalsComponent ServerGenitals(IEntityManager entMan, EntityUid body)
    {
        return entMan.GetComponent<GenitalsComponent>(body);
    }
}

/// <summary>Test-only marker: AnatomyVisualsProbeSystem counts GenitalsVisualsChangedEvent on entities that carry it.</summary>
[RegisterComponent]
public sealed partial class AnatomyVisualsProbeComponent : Component;

/// <summary>Test-only listener: counts GenitalsVisualsChangedEvent raised on probed entities.</summary>
public sealed partial class AnatomyVisualsProbeSystem : EntitySystem
{
    public int Raised;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<AnatomyVisualsProbeComponent, GenitalsVisualsChangedEvent>(OnVisualsChanged);
    }

    private void OnVisualsChanged(Entity<AnatomyVisualsProbeComponent> ent, ref GenitalsVisualsChangedEvent args)
    {
        Raised++;
    }
}
