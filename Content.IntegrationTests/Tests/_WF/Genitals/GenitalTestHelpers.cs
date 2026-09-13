using System.Collections.Generic;
using System.Linq;
using Content.Server._WF.Genitals;
using Content.Shared._Common.Consent;
using Content.Shared._WF.Genitals;
using Content.Shared._WF.Genitals.Components;
using Content.Shared._WF.Genitals.Prototypes;
using Content.Shared.Body.Components;
using Content.Shared.Body.Organ;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Humanoid;
using Content.Shared.Preferences;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Genitals;

/// <summary>Shared fixtures for the anatomy tests. Entity helpers must run on the server thread (WaitPost/WaitAssertion).</summary>
public static class GenitalTestHelpers
{
    public const string MasterToggle = "GenitalMarkings";
    public const string StripToggle = "UndergarmentStrip";
    public const string SurgeryToggle = "AnatomySurgery";

    public static readonly ProtoId<GenitalShapePrototype> PenisHuman = "GenitalShapePenisHuman";
    public static readonly ProtoId<GenitalShapePrototype> PenisKnotted = "GenitalShapePenisKnotted";
    public static readonly ProtoId<GenitalShapePrototype> PenisHemi = "GenitalShapePenisHemi";
    public static readonly ProtoId<GenitalShapePrototype> VaginaHuman = "GenitalShapeVaginaHuman";
    public static readonly ProtoId<GenitalShapePrototype> VaginaSlit = "GenitalShapeVaginaSlit";
    public static readonly ProtoId<GenitalShapePrototype> BreastsPair = "GenitalShapeBreastsPair";

    /// <summary>Every organ, all valid: knotted penis with a sheath, external testicles, vagina with womb, breasts.</summary>
    public static GenitalProfile FullProfile()
    {
        return GenitalProfile.Empty
            .WithPenis(new PenisProfile(PenisKnotted, 15, SheathType.Sheath))
            .WithTesticles(new TesticlesProfile(TesticleType.External, 2))
            .WithVagina(new VaginaProfile(VaginaHuman))
            .WithBreasts(new BreastsProfile(BreastsPair, 3));
    }

    /// <summary>Sets exactly these consent toggles on (all others off), dirties them and raises GenitalConsentChangedEvent.</summary>
    /// <remarks>Bypasses ConsentSystem, so a test needs no player.</remarks>
    public static void SetConsent(IEntityManager entMan, EntityUid uid, params string[] toggles)
    {
        var consent = entMan.EnsureComponent<ConsentComponent>(uid);
        var settings = new Dictionary<ProtoId<ConsentTogglePrototype>, string>();
        foreach (var toggle in toggles)
        {
            settings[toggle] = "on";
        }

        consent.ConsentSettings = new PlayerConsentSettings(string.Empty, settings);
        entMan.Dirty(uid, consent);

        var ev = new GenitalConsentChangedEvent(uid);
        entMan.EventBus.RaiseEvent(EventSource.Local, ref ev);
    }

    /// <summary>Turns on the master switch plus any extra toggles.</summary>
    public static void GrantConsent(IEntityManager entMan, EntityUid uid, params string[] extraToggles)
    {
        var toggles = new List<string> { MasterToggle };
        toggles.AddRange(extraToggles);
        SetConsent(entMan, uid, toggles.ToArray());
    }

    /// <summary>Turns every toggle off.</summary>
    public static void RevokeConsent(IEntityManager entMan, EntityUid uid)
    {
        SetConsent(entMan, uid);
    }

    /// <summary>Ensures a GenitalsComponent and fills its mirror fields directly, without organs.</summary>
    public static GenitalsComponent SetMirror(IEntityManager entMan, EntityUid uid,
        GenitalOrganState? penis = null,
        GenitalOrganState? testicles = null,
        GenitalOrganState? vagina = null,
        bool womb = false,
        GenitalOrganState? breasts = null)
    {
        var genitals = entMan.EnsureComponent<GenitalsComponent>(uid);
        genitals.Penis = penis;
        genitals.Testicles = testicles;
        genitals.Vagina = vagina;
        genitals.Womb = womb;
        genitals.Breasts = breasts;
        entMan.Dirty(uid, genitals);
        return genitals;
    }

    /// <summary>A default profile for the species, age and sex; add anatomy with WithGenitals.</summary>
    public static HumanoidCharacterProfile MakeProfile(string species = "Human", int age = 30, Sex sex = Sex.Male)
    {
        return HumanoidCharacterProfile.DefaultWithSpecies(species).WithAge(age).WithSex(sex);
    }

    /// <summary>Detaches a session from its entity without removing the mind, which marks the body SSD.</summary>
    public static void Detach(ISharedPlayerManager playerMan, ICommonSession session)
    {
        playerMan.SetAttachedEntity(session, null);
    }

    /// <summary>Spawns an adult MobHuman, turns on the master switch and loads the anatomy (FullProfile by default), which builds the organs.</summary>
    public static EntityUid SpawnWithAnatomy(IEntityManager entMan, MapCoordinates coords, GenitalProfile anatomy = null)
    {
        var mob = entMan.Spawn("MobHuman", coords);
        GrantConsent(entMan, mob);
        entMan.System<SharedHumanoidAppearanceSystem>().LoadProfile(mob, MakeProfile().WithGenitals(anatomy ?? FullProfile()));
        return mob;
    }

    /// <summary>The body's genital organs; none without a body.</summary>
    public static List<Entity<GenitalOrganComponent, OrganComponent>> GenitalOrgans(IEntityManager entMan, EntityUid body)
    {
        if (!entMan.TryGetComponent<BodyComponent>(body, out var bodyComp))
            return new List<Entity<GenitalOrganComponent, OrganComponent>>();

        return entMan.System<SharedBodySystem>().GetBodyOrganEntityComps<GenitalOrganComponent>((body, bodyComp));
    }

    /// <summary>The body's organ in one slot, or null.</summary>
    public static EntityUid? GenitalOrgan(IEntityManager entMan, EntityUid body, GenitalSlot slot)
    {
        foreach (var organ in GenitalOrgans(entMan, body))
        {
            if (organ.Comp1.Slot == slot)
                return organ.Owner;
        }

        return null;
    }

    /// <summary>State of the organ of one slot among the given organs, or null.</summary>
    public static GenitalOrganState? OrganState(List<Entity<GenitalOrganComponent, OrganComponent>> organs, GenitalSlot slot)
    {
        foreach (var organ in organs)
        {
            if (organ.Comp1.Slot == slot)
                return organ.Comp1.State;
        }

        return null;
    }

    /// <summary>The body's root part (the torso for humanoids), or null.</summary>
    public static EntityUid? RootPart(IEntityManager entMan, EntityUid body)
    {
        if (!entMan.TryGetComponent<BodyComponent>(body, out var bodyComp))
            return null;

        return entMan.System<SharedBodySystem>().GetRootPartOrNull(body, bodyComp)?.Entity;
    }

    /// <summary>Whether the root part has all five genital slots.</summary>
    public static bool HasAllSlots(IEntityManager entMan, EntityUid body)
    {
        return RootPart(entMan, body) is { } root
               && entMan.TryGetComponent<BodyPartComponent>(root, out var part)
               && GenitalOrganSystem.Slots.All(slot => part.Organs.ContainsKey(GenitalOrganSystem.SlotId(slot)));
    }

    /// <summary>Whether the root part has any genital slot.</summary>
    public static bool HasAnySlot(IEntityManager entMan, EntityUid body)
    {
        return RootPart(entMan, body) is { } root
               && entMan.TryGetComponent<BodyPartComponent>(root, out var part)
               && GenitalOrganSystem.Slots.Any(slot => part.Organs.ContainsKey(GenitalOrganSystem.SlotId(slot)));
    }

    /// <summary>Asserts that every organ field of the mirror is empty.</summary>
    public static void AssertMirrorEmpty(GenitalsComponent genitals)
    {
        Assert.That(genitals.Penis, Is.Null, "Mirror penis");
        Assert.That(genitals.Testicles, Is.Null, "Mirror testicles");
        Assert.That(genitals.Vagina, Is.Null, "Mirror vagina");
        Assert.That(genitals.Womb, Is.False, "Mirror womb");
        Assert.That(genitals.Breasts, Is.Null, "Mirror breasts");
    }
}
