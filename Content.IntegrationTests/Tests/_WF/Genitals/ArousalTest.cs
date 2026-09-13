using System.Collections.Generic;
using Content.Shared._Common.Consent;
using Content.Shared._WF.Genitals;
using Content.Shared._WF.Genitals.Components;
using Content.Shared._WF.Genitals.Prototypes;
using Content.Shared._WF.Genitals.Systems;
using Content.Shared.Body.Systems;
using Content.Shared.Humanoid;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using static Content.IntegrationTests.Tests._WF.Genitals.GenitalTestHelpers;

namespace Content.IntegrationTests.Tests._WF.Genitals;

/// <summary>Arousal thresholds, the owner and external write paths, and the resets, which all go through Apply.</summary>
[TestFixture]
[TestOf(typeof(SharedArousalSystem))]
public sealed class ArousalTest
{
    private static GenitalOrganState PenisState => new() { Shape = PenisHuman, Step = 1, LengthCm = 15, Color = Color.White };

    /// <summary>None below 30, Partial from 30, Full from 70, with the Default settings.</summary>
    [Test]
    public async Task ThresholdsTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var settings = pair.Server.ProtoMan.Index(GenitalSettingsPrototype.DefaultId);

        Assert.Multiple(() =>
        {
            Assert.That(settings.PartialArousal, Is.EqualTo(30));
            Assert.That(settings.FullArousal, Is.EqualTo(70));
            Assert.That(SharedArousalSystem.ToState(0, settings), Is.EqualTo(ArousalState.None));
            Assert.That(SharedArousalSystem.ToState(29, settings), Is.EqualTo(ArousalState.None));
            Assert.That(SharedArousalSystem.ToState(30, settings), Is.EqualTo(ArousalState.Partial));
            Assert.That(SharedArousalSystem.ToState(69, settings), Is.EqualTo(ArousalState.Partial));
            Assert.That(SharedArousalSystem.ToState(70, settings), Is.EqualTo(ArousalState.Full));
            Assert.That(SharedArousalSystem.ToState(100, settings), Is.EqualTo(ArousalState.Full));
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The owner path needs master consent and a living body, clamps to 100, and raises one event per change.</summary>
    [Test]
    public async Task OwnerWriteTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var arousal = entMan.System<SharedArousalSystem>();
        var mobState = entMan.System<MobStateSystem>();
        var probe = entMan.System<ArousalProbeSystem>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = GenitalConsentTestHelpers.SpawnPresentAdult(entMan, map.GridCoords);
            entMan.EnsureComponent<ArousalProbeComponent>(body);
            var genitals = SetMirror(entMan, body, penis: PenisState);
            probe.Clear();

            Assert.That(arousal.SetArousal((body, genitals), 50), Is.False, "An owner write without master consent must be refused.");
            Assert.That(genitals.Arousal, Is.EqualTo(0));

            // Consent recomputes the mirror from the body's organs (none here), so the penis is mirrored again after it.
            GrantConsent(entMan, body);
            SetMirror(entMan, body, penis: PenisState);

            Assert.That(arousal.SetArousal((body, genitals), 50), Is.True);
            Assert.That(genitals.Arousal, Is.EqualTo(50));
            Assert.That(arousal.GetState((body, genitals)), Is.EqualTo(ArousalState.Partial));

            Assert.That(arousal.SetArousal((body, genitals), 250), Is.True, "Values above 100 are clamped, not refused.");
            Assert.That(genitals.Arousal, Is.EqualTo(100));
            Assert.That(arousal.SetArousal((body, genitals), 100), Is.True, "An unchanged value is accepted.");

            Assert.That(probe.Events, Is.EqualTo(new[]
            {
                (body, new ArousalChangedEvent(0, 50, ArousalState.None, ArousalState.Partial, null)),
                (body, new ArousalChangedEvent(50, 100, ArousalState.Partial, ArousalState.Full, null)),
            }), "Each change raises one event with its values and states; an unchanged value raises none.");
            probe.Clear();

            mobState.ChangeMobState(body, MobState.Dead);
            Assert.That(genitals.Arousal, Is.EqualTo(0), "Death resets arousal.");
            Assert.That(probe.Events, Is.EqualTo(new[]
            {
                (body, new ArousalChangedEvent(100, 0, ArousalState.Full, ArousalState.None, null)),
            }), "The death reset goes through Apply.");

            Assert.That(arousal.SetArousal((body, genitals), 40), Is.False, "Owner writes are refused while dead.");
            Assert.That(genitals.Arousal, Is.EqualTo(0));

            entMan.DeleteEntity(body);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// External changes need the target's master switch and the named toggle, the target present, and an adult, opted-in
    /// humanoid source; non-humanoid or absent sources need nothing of their own. Values clamp to 0..100.
    /// </summary>
    [Test]
    public async Task ExternalWriteTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var arousal = entMan.System<SharedArousalSystem>();
        var probe = entMan.System<ArousalProbeSystem>();
        var map = await pair.CreateTestMap();

        // Any dedicated opt-in works; UndergarmentStrip is a real toggle.
        ProtoId<ConsentTogglePrototype> toggle = StripToggle;

        await server.WaitAssertion(() =>
        {
            var target = GenitalConsentTestHelpers.SpawnPresentAdult(entMan, map.GridCoords);
            entMan.EnsureComponent<ArousalProbeComponent>(target);
            GrantConsent(entMan, target);
            var genitals = SetMirror(entMan, target, penis: PenisState);
            var source = GenitalConsentTestHelpers.SpawnPresentAdult(entMan, map.GridCoords);
            probe.Clear();

            Assert.That(arousal.TryAdjustArousal((target, genitals), 40, toggle), Is.False,
                "The target has not turned on the named toggle.");

            // The named toggle alone: Adult content is still required.
            SetConsent(entMan, target, StripToggle);
            SetMirror(entMan, target, penis: PenisState);
            Assert.That(arousal.TryAdjustArousal((target, genitals), 40, toggle), Is.False,
                "The target has the named toggle but not Adult content.");

            GrantConsent(entMan, target, StripToggle);
            SetMirror(entMan, target, penis: PenisState);

            Assert.That(arousal.TryAdjustArousal((target, genitals), 40, toggle, source), Is.False,
                "A humanoid source without Adult content.");

            GrantConsent(entMan, source);
            SetAge(entMan, source, 17);
            Assert.That(arousal.TryAdjustArousal((target, genitals), 40, toggle, source), Is.False, "A source under 18.");
            SetAge(entMan, source, 30);

            GenitalConsentTestHelpers.SetSsd(entMan, target, true);
            Assert.That(arousal.TryAdjustArousal((target, genitals), 40, toggle, source), Is.False,
                "A target that is not actively present.");
            GenitalConsentTestHelpers.SetSsd(entMan, target, false);

            Assert.That(genitals.Arousal, Is.EqualTo(0));
            Assert.That(probe.Events, Is.Empty, "Refused changes raise nothing.");

            var item = entMan.SpawnEntity(null, map.GridCoords);
            Assert.That(arousal.TryAdjustArousal((target, genitals), 40, toggle, source), Is.True, "An adult, opted-in humanoid source.");
            Assert.That(arousal.TryAdjustArousal((target, genitals), 50, toggle, item), Is.True, "A non-humanoid source.");
            Assert.That(arousal.TryAdjustArousal((target, genitals), 50, toggle), Is.True, "No source.");
            Assert.That(genitals.Arousal, Is.EqualTo(100), "External changes clamp at 100.");
            Assert.That(arousal.TryAdjustArousal((target, genitals), -150, toggle), Is.True);
            Assert.That(genitals.Arousal, Is.EqualTo(0), "External changes clamp at 0.");

            Assert.That(probe.Events, Is.EqualTo(new[]
            {
                (target, new ArousalChangedEvent(0, 40, ArousalState.None, ArousalState.Partial, source)),
                (target, new ArousalChangedEvent(40, 90, ArousalState.Partial, ArousalState.Full, item)),
                (target, new ArousalChangedEvent(90, 100, ArousalState.Full, ArousalState.Full, null)),
                (target, new ArousalChangedEvent(100, 0, ArousalState.Full, ArousalState.None, null)),
            }), "Each change carries its old and new values, states and source.");

            entMan.DeleteEntity(target);
            entMan.DeleteEntity(source);
            entMan.DeleteEntity(item);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Master switch off, death and the last arousable organ removed all reset to 0 through Apply. Breasts alone cannot be aroused.</summary>
    [Test]
    public async Task ResetsTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var arousal = entMan.System<SharedArousalSystem>();
        var mobState = entMan.System<MobStateSystem>();
        var bodySystem = entMan.System<SharedBodySystem>();
        var probe = entMan.System<ArousalProbeSystem>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = SpawnWithAnatomy(entMan, map.MapCoords);
            entMan.EnsureComponent<ArousalProbeComponent>(body);
            var genitals = entMan.GetComponent<GenitalsComponent>(body);
            Assert.That(genitals.Penis, Is.Not.Null, "Precondition: the organs were built.");

            // Master switch off. The mirror empties as well; whichever reaction runs first resets, and only once.
            Assert.That(arousal.SetArousal((body, genitals), 80), Is.True);
            probe.Clear();
            RevokeConsent(entMan, body);
            Assert.That(genitals.Arousal, Is.EqualTo(0), "Turning the master switch off resets arousal.");
            Assert.That(probe.Events, Is.EqualTo(new[]
            {
                (body, new ArousalChangedEvent(80, 0, ArousalState.Full, ArousalState.None, null)),
            }), "The consent reset goes through Apply once.");

            // Death.
            GrantConsent(entMan, body);
            Assert.That(genitals.Penis, Is.Not.Null, "Precondition: the mirror is restored.");
            Assert.That(arousal.SetArousal((body, genitals), 80), Is.True);
            probe.Clear();
            mobState.ChangeMobState(body, MobState.Dead);
            Assert.That(genitals.Arousal, Is.EqualTo(0), "Death resets arousal.");
            Assert.That(probe.Events, Is.EqualTo(new[]
            {
                (body, new ArousalChangedEvent(80, 0, ArousalState.Full, ArousalState.None, null)),
            }), "The death reset goes through Apply.");

            entMan.DeleteEntity(body);
        });

        // The last organ that reacts to arousal leaves the body.
        EntityUid patient = default;
        await server.WaitPost(() =>
        {
            patient = SpawnWithAnatomy(entMan, map.MapCoords);
            entMan.EnsureComponent<ArousalProbeComponent>(patient);
            arousal.SetArousal((patient, entMan.GetComponent<GenitalsComponent>(patient)), 60);
            probe.Clear();
            bodySystem.RemoveOrgan(GenitalOrgan(entMan, patient, GenitalSlot.Penis)!.Value);
        });

        await pair.RunTicksSync(1);
        await server.WaitAssertion(() =>
        {
            var genitals = entMan.GetComponent<GenitalsComponent>(patient);
            Assert.That(genitals.Penis, Is.Null, "Precondition: the penis left the mirror.");
            Assert.That(genitals.Vagina, Is.Not.Null, "Precondition: the vagina remains.");
            Assert.That(genitals.Arousal, Is.EqualTo(60), "Arousal stays while an arousable organ remains.");
            Assert.That(probe.Events, Is.Empty);
        });

        await server.WaitPost(() => bodySystem.RemoveOrgan(GenitalOrgan(entMan, patient, GenitalSlot.Vagina)!.Value));
        await pair.RunTicksSync(1);
        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<GenitalsComponent>(patient).Arousal, Is.EqualTo(0),
                "Removing the last arousable organ resets arousal.");
            Assert.That(probe.Events, Is.EqualTo(new[]
            {
                (patient, new ArousalChangedEvent(60, 0, ArousalState.Partial, ArousalState.None, null)),
            }), "The organ reset goes through Apply.");
        });

        // Breasts alone.
        await server.WaitAssertion(() =>
        {
            var anatomy = GenitalProfile.Empty.WithBreasts(new BreastsProfile(BreastsPair, 3));
            var body = SpawnWithAnatomy(entMan, map.MapCoords, anatomy);
            var genitals = entMan.GetComponent<GenitalsComponent>(body);
            Assert.That(genitals.Breasts, Is.Not.Null, "Precondition: the breasts were built.");
            Assert.Multiple(() =>
            {
                Assert.That(arousal.CanBeAroused((body, genitals)), Is.False, "Breasts have no arousal effect.");
                Assert.That(arousal.SetArousal((body, genitals), 50), Is.False);
                Assert.That(arousal.TryAdjustArousal((body, genitals), 50, StripToggle), Is.False);
                Assert.That(genitals.Arousal, Is.EqualTo(0));
                Assert.That(arousal.GetState((body, genitals)), Is.EqualTo(ArousalState.None));
            });

            entMan.DeleteEntity(body);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Server thread only.</summary>
    private static void SetAge(IEntityManager entMan, EntityUid uid, int age)
    {
        var humanoid = entMan.GetComponent<HumanoidAppearanceComponent>(uid);
        humanoid.Age = age;
        entMan.Dirty(uid, humanoid);
    }
}

/// <summary>Test-only marker: ArousalProbeSystem records ArousalChangedEvent on bodies that carry it.</summary>
[RegisterComponent]
public sealed partial class ArousalProbeComponent : Component;

/// <summary>Test-only listener for ArousalChangedEvent on probed bodies, in the order raised.</summary>
public sealed partial class ArousalProbeSystem : EntitySystem
{
    public readonly List<(EntityUid Body, ArousalChangedEvent Event)> Events = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ArousalProbeComponent, ArousalChangedEvent>(OnArousalChanged);
    }

    public void Clear()
    {
        Events.Clear();
    }

    private void OnArousalChanged(Entity<ArousalProbeComponent> ent, ref ArousalChangedEvent args)
    {
        Events.Add((ent.Owner, args));
    }
}
