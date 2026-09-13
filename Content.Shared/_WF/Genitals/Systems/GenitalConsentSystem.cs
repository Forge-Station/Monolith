using Content.Shared._Common.Consent;
using Content.Shared.Humanoid;
using Content.Shared.Mobs.Systems;
using Content.Shared.SSDIndicator;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Genitals.Systems;

/// <summary>Strict consent and presence checks for anatomy: a missing ConsentComponent, cleared toggles or SSD mean no.</summary>
/// <remarks>
/// SharedConsentSystem.HasConsent treats a missing component as consent, so it is never used here.
/// Abstract because EntitySystemManager runs a concrete base beside its subclass; each side has one subclass.
/// </remarks>
public abstract partial class GenitalConsentSystem : EntitySystem
{
    [Dependency] private SharedGenitalsSystem _genitals = default!;
    [Dependency] private MobStateSystem _mobState = default!;

    /// <summary>The replicated kill switch wf.anatomy_enabled.</summary>
    public bool AnatomyEnabled => _genitals.AnatomyEnabled;

    /// <summary>The entity has a ConsentComponent with the toggle on. No component means no.</summary>
    public bool HasStrictConsent(Entity<ConsentComponent?> ent, ProtoId<ConsentTogglePrototype> toggle)
    {
        return Resolve(ent, ref ent.Comp, false)
               && ent.Comp.ConsentSettings.Toggles.TryGetValue(toggle, out var value)
               && value == "on";
    }

    /// <summary>Strict consent to adult content (the master switch).</summary>
    public bool HasMaster(EntityUid uid)
    {
        return HasStrictConsent(uid, _genitals.Settings.MasterConsent);
    }

    /// <summary>
    /// Whether the body's player is connected (SSDIndicatorComponent.IsSSD false). A body without the component counts as
    /// absent, since its presence is unknown; the owner's own paths never ask.
    /// </summary>
    public bool IsActivelyPresent(EntityUid uid)
    {
        return TryComp<SSDIndicatorComponent>(uid, out var ssd) && !ssd.IsSSD;
    }

    /// <summary>Master consent and actively present: the test for any action on, or display of, another body.</summary>
    public bool TargetConsents(EntityUid target)
    {
        return AnatomyEnabled && HasMaster(target) && IsActivelyPresent(target);
    }

    /// <summary>Whether the actor may remove or put back the target's undergarments.</summary>
    public bool CanStrip(EntityUid actor, EntityUid target)
    {
        return AnatomyEnabled
               && _genitals.IsAdult(actor)
               && HasMaster(actor)
               && TargetConsents(target)
               && _genitals.Settings.StripConsent is { } strip
               && HasStrictConsent(target, strip)
               && !_mobState.IsDead(target);
    }

    /// <summary>Whether the surgeon may perform anatomy surgery on the patient. Self-surgery needs only the master switch.</summary>
    public bool CanOperate(EntityUid surgeon, EntityUid patient)
    {
        if (!AnatomyEnabled || !_genitals.IsAdult(surgeon) || !HasMaster(surgeon) || !HasMaster(patient))
            return false;

        return surgeon == patient
               || (IsActivelyPresent(patient)
                   && _genitals.Settings.SurgeryConsent is { } surgery
                   && HasStrictConsent(patient, surgery));
    }

    /// <summary>The examiner half of CanExamine: kill switch, strict master consent, and adult when humanoid.</summary>
    public bool ExaminerMayRead(EntityUid examiner)
    {
        return AnatomyEnabled
               && HasMaster(examiner)
               && (!HasComp<HumanoidAppearanceComponent>(examiner) || _genitals.IsAdult(examiner));
    }

    /// <summary>Whether the examiner may read anatomy lines about the target. Humanoid examiners must be adult.</summary>
    public bool CanExamine(EntityUid examiner, EntityUid target)
    {
        return ExaminerMayRead(examiner) && (examiner == target || TargetConsents(target));
    }

    /// <summary>Whether anything other than the owner's panel may change the target's arousal, under a dedicated toggle.</summary>
    public bool CanAdjustExternally(EntityUid target, ProtoId<ConsentTogglePrototype> consent, EntityUid? source)
    {
        if (!TargetConsents(target) || !HasStrictConsent(target, consent))
            return false;

        return source is not { } src
               || !HasComp<HumanoidAppearanceComponent>(src)
               || (_genitals.IsAdult(src) && HasMaster(src));
    }

    /// <summary>Whether the local viewer may see this body's anatomy. Only the client has a viewer.</summary>
    public virtual bool CanViewerSee(EntityUid target)
    {
        return false;
    }

    /// <summary>Removes anatomy surgeries from a surgery list for viewers without adult content. Client override.</summary>
    public virtual Dictionary<NetEntity, List<EntProtoId>> FilterSurgeryChoices(Dictionary<NetEntity, List<EntProtoId>> choices)
    {
        return choices;
    }
}
