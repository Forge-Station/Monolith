using System.Linq;
using Content.Server.Body.Components;
using Content.Shared.Emp;
using Content.Shared._Forge.Cybernetics.Components;
using Content.Shared._Forge.TTS;
using Content.Shared.Chat; // Forge-Change
using Content.Shared.Body.Components; // Forge-Change
using Content.Shared.Body.Organ; // Forge-Change
using Content.Shared.Body.Part; // Forge-Change
using Content.Shared.Body.Systems; // Forge-Change
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Implants;
using Content.Shared.Implants.Components;
using Content.Shared.Interaction.Events;
using Content.Shared.Popups;
using Content.Shared.Speech; // Forge-Change
using Content.Shared.VoiceMask;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Forge.Cybernetics;

public sealed class ForgeCyberneticsSystem : EntitySystem
{
    [Dependency] private readonly SharedBodySystem _body = default!; // Forge-Change
    [Dependency] private readonly DamageableSystem _damage = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solution = default!;
    [Dependency] private readonly SharedUserInterfaceSystem _ui = default!;

    private static readonly string[] MechanicalDamageTypes = { "Blunt", "Slash", "Piercing" };

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ForgeVoiceModuleImplantComponent, ImplantImplantedEvent>(OnVoiceImplanted);
        SubscribeLocalEvent<ForgeVoiceModuleImplantComponent, EntGotRemovedFromContainerMessage>(OnVoiceRemoved);
        SubscribeLocalEvent<ForgeVoiceModuleImplantComponent, UseInHandEvent>(OnVoiceUseInHand);
        SubscribeLocalEvent<ForgeVoiceModuleImplantComponent, VoiceMaskChangeVoiceMessage>(OnVoiceChanged);
        SubscribeLocalEvent<ForgeVoiceModuleImplantComponent, VoiceMaskChangeVerbMessage>(OnVoiceVerbChanged);
        SubscribeLocalEvent<ForgeSurgicalArmorImplantComponent, EntGotInsertedIntoContainerMessage>(OnSurgicalArmorInserted);
        SubscribeLocalEvent<ForgeSurgicalArmorImplantComponent, EntGotRemovedFromContainerMessage>(OnSurgicalArmorRemoved);
        SubscribeLocalEvent<ForgeSurgicalArmorImplantComponent, ComponentShutdown>(OnSurgicalArmorShutdown);
        SubscribeLocalEvent<BodyPartComponent, ContainerIsInsertingAttemptEvent>(OnCavityInsertAttempt);

        SubscribeLocalEvent<ForgeVoiceOverrideComponent, TransformSpeakerVoiceEvent>(OnTransformVoice);
        SubscribeLocalEvent<ForgeVoiceOverrideComponent, TransformSpeakerNameEvent>(OnTransformName);
        SubscribeLocalEvent<ForgeSubdermalArmorComponent, DamageModifyEvent>(OnDamageModify);
        SubscribeLocalEvent<DamageableComponent, EmpPulseEvent>(OnEmpPulse); // Forge-Change
        SubscribeLocalEvent<ForgeUnarmedDamageComponent, EmpPulseEvent>(OnEmpDisableUnarmed);
        SubscribeLocalEvent<BodyComponent, GetMeleeDamageEvent>(OnGetUnarmedDamage); // Forge-Change
        SubscribeLocalEvent<ForgeInjectOnUnarmedHitComponent, MeleeHitEvent>(OnInjectOnUnarmedHit);
        SubscribeLocalEvent<ForgeBloodstreamCleanerComponent, ComponentStartup>(OnBloodstreamCleanerStartup);
        SubscribeLocalEvent<ForgeRadiationCleanerComponent, ComponentStartup>(OnRadiationCleanerStartup);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;

        var toxinQuery = EntityQueryEnumerator<ForgeBloodstreamCleanerComponent, BloodstreamComponent>();
        while (toxinQuery.MoveNext(out var uid, out var cleaner, out var bloodstream))
        {
            if (now < cleaner.NextUpdate)
                continue;

            cleaner.NextUpdate = now + cleaner.UpdateInterval;
            CleanBloodstream((uid, cleaner, bloodstream));
        }

        var radQuery = EntityQueryEnumerator<ForgeRadiationCleanerComponent>();
        while (radQuery.MoveNext(out var uid, out var cleaner))
        {
            if (now < cleaner.NextUpdate)
                continue;

            cleaner.NextUpdate = now + cleaner.UpdateInterval;
            HealRadiation(uid, cleaner);
        }
    }

    private void OnVoiceImplanted(Entity<ForgeVoiceModuleImplantComponent> ent, ref ImplantImplantedEvent args)
    {
        if (args.Implanted is not { } implanted)
            return;

        var voice = EnsureComp<ForgeVoiceOverrideComponent>(implanted);
        voice.VoiceId = ent.Comp.VoiceId;
        voice.SpeechVerbId = ent.Comp.SpeechVerbId; // Forge-Change
        if (ent.Comp.SpeechVerbId != null && TryComp<SpeechComponent>(implanted, out var speech))
        {
            voice.OriginalSpeechVerbId = speech.SpeechVerb;
            speech.SpeechVerb = ent.Comp.SpeechVerbId.Value;
            Dirty(implanted, speech);
        }

        ent.Comp.AppliedEntity = implanted;
    }

    private void OnVoiceRemoved(Entity<ForgeVoiceModuleImplantComponent> ent, ref EntGotRemovedFromContainerMessage args)
    {
        if (ent.Comp.AppliedEntity is { } applied && !Terminating(applied))
        {
            if (TryComp<ForgeVoiceOverrideComponent>(applied, out var voice) &&
                voice.OriginalSpeechVerbId != null &&
                TryComp<SpeechComponent>(applied, out var speech))
            {
                speech.SpeechVerb = voice.OriginalSpeechVerbId.Value;
                Dirty(applied, speech);
            }

            RemComp<ForgeVoiceOverrideComponent>(applied);
        }

        ent.Comp.AppliedEntity = null;
    }

    private void OnVoiceUseInHand(Entity<ForgeVoiceModuleImplantComponent> ent, ref UseInHandEvent args)
    {
        if (TryComp<SubdermalImplantComponent>(ent, out var implant) && implant.ImplantedEntity != null)
        {
            _popup.PopupEntity(Loc.GetString("forge-voice-module-locked"), ent, args.User);
            return;
        }

        if (!_ui.HasUi(ent.Owner, VoiceMaskUIKey.Key))
            return;

        _ui.OpenUi(ent.Owner, VoiceMaskUIKey.Key, args.User);
        UpdateVoiceUi(ent);
        args.Handled = true;
    }

    private void OnSurgicalArmorInserted(Entity<ForgeSurgicalArmorImplantComponent> ent, ref EntGotInsertedIntoContainerMessage args)
    {
        if (!TryGetCavityBody(args.Container.Owner, out var body) ||
            !TryComp<SubdermalImplantComponent>(ent, out var subdermal) ||
            subdermal.ImplantedEntity != null)
        {
            return;
        }

        if (subdermal.OnAdd != null)
            EntityManager.AddComponents(body, subdermal.OnAdd, true);

        subdermal.ImplantedEntity = body;
        Dirty(ent.Owner, subdermal);
    }

    private void OnSurgicalArmorRemoved(Entity<ForgeSurgicalArmorImplantComponent> ent, ref EntGotRemovedFromContainerMessage args)
    {
        RemoveSurgicalArmorEffects(ent.Owner);
    }

    private void OnSurgicalArmorShutdown(Entity<ForgeSurgicalArmorImplantComponent> ent, ref ComponentShutdown args)
    {
        RemoveSurgicalArmorEffects(ent.Owner);
    }

    private void RemoveSurgicalArmorEffects(EntityUid uid)
    {
        if (!TryComp<SubdermalImplantComponent>(uid, out var subdermal) ||
            subdermal.ImplantedEntity is not { } body)
        {
            return;
        }

        if (!Terminating(body) && subdermal.OnAdd != null)
            EntityManager.RemoveComponents(body, subdermal.OnAdd);

        subdermal.ImplantedEntity = null;
        Dirty(uid, subdermal);
    }

    private void OnCavityInsertAttempt(Entity<BodyPartComponent> ent, ref ContainerIsInsertingAttemptEvent args)
    {
        if (args.Cancelled ||
            args.Container.ID != ent.Comp.ContainerName ||
            !HasComp<ForgeSurgicalArmorImplantComponent>(args.EntityUid) ||
            ent.Comp.Body is not { } body ||
            !HasInstalledArmorImplant(body))
        {
            return;
        }

        _popup.PopupEntity(Loc.GetString("forge-surgical-armor-already-installed"), body, PopupType.SmallCaution);
        args.Cancel();
    }

    private bool HasInstalledArmorImplant(EntityUid body)
    {
        if (HasComp<ForgeSubdermalArmorComponent>(body))
            return true;

        foreach (var (partUid, part) in _body.GetBodyChildren(body))
        {
            if (!part.Enabled)
                continue;

            if (part.ItemInsertionSlot.Item is { } item &&
                HasComp<ForgeSurgicalArmorImplantComponent>(item))
            {
                return true;
            }
        }

        return false;
    }

    private bool TryGetCavityBody(EntityUid partUid, out EntityUid body)
    {
        body = EntityUid.Invalid;
        if (!TryComp<BodyPartComponent>(partUid, out var part) ||
            part.PartType != BodyPartType.Torso ||
            part.Body is not { } bodyUid)
        {
            return false;
        }

        body = bodyUid;
        return true;
    }

    private void OnVoiceChanged(Entity<ForgeVoiceModuleImplantComponent> ent, ref VoiceMaskChangeVoiceMessage args)
    {
        if (TryComp<SubdermalImplantComponent>(ent, out var implant) && implant.ImplantedEntity != null)
        {
            _popup.PopupEntity(Loc.GetString("forge-voice-module-locked"), ent, args.Actor);
            return;
        }

        if (!_proto.HasIndex<TTSVoicePrototype>(args.Voice))
            return;

        ent.Comp.VoiceId = args.Voice;
        SyncImplanterVoiceModule(ent);
        _popup.PopupEntity(Loc.GetString("voice-mask-voice-popup-success"), ent, args.Actor);
        UpdateVoiceUi(ent);
    }

    private void OnVoiceVerbChanged(Entity<ForgeVoiceModuleImplantComponent> ent, ref VoiceMaskChangeVerbMessage args)
    {
        if (TryComp<SubdermalImplantComponent>(ent, out var implant) && implant.ImplantedEntity != null)
        {
            _popup.PopupEntity(Loc.GetString("forge-voice-module-locked"), ent, args.Actor);
            return;
        }

        if (args.Verb is { } id && !_proto.HasIndex<SpeechVerbPrototype>(id))
            return;

        ent.Comp.SpeechVerbId = args.Verb;
        SyncImplanterVoiceModule(ent);
        _popup.PopupEntity(Loc.GetString("voice-mask-popup-success"), ent, args.Actor);
        UpdateVoiceUi(ent);
    }

    private void SyncImplanterVoiceModule(Entity<ForgeVoiceModuleImplantComponent> ent)
    {
        if (!TryComp<ImplanterComponent>(ent, out var implanter))
            return;

        var implant = implanter.ImplanterSlot.ContainerSlot?.ContainedEntities.FirstOrDefault();
        if (implant is { } implantUid &&
            TryComp<ForgeVoiceModuleImplantComponent>(implantUid, out var voice))
        {
            voice.VoiceId = ent.Comp.VoiceId;
            voice.SpeechVerbId = ent.Comp.SpeechVerbId; // Forge-Change
        }
    }

    private void UpdateVoiceUi(Entity<ForgeVoiceModuleImplantComponent> ent)
    {
        _ui.SetUiState(ent.Owner, VoiceMaskUIKey.Key, new VoiceMaskBuiState(Loc.GetString("forge-voice-module-ui-name"), ent.Comp.VoiceId, ent.Comp.SpeechVerbId, true)); // Forge-Change
    }

    private void OnTransformVoice(Entity<ForgeVoiceOverrideComponent> ent, ref TransformSpeakerVoiceEvent args)
    {
        args.VoiceId = ent.Comp.VoiceId;
    }

    private void OnTransformName(Entity<ForgeVoiceOverrideComponent> ent, ref TransformSpeakerNameEvent args)
    {
        args.SpeechVerb = ent.Comp.SpeechVerbId ?? args.SpeechVerb;
    }

    private void OnDamageModify(Entity<ForgeSubdermalArmorComponent> ent, ref DamageModifyEvent args)
    {
        foreach (var damageType in MechanicalDamageTypes)
            ModifyDamage(args.Damage, damageType, ent.Comp.MechanicalMultiplier, ent.Comp.MechanicalFlat);

        ModifyDamage(args.Damage, "Heat", ent.Comp.HeatMultiplier, ent.Comp.HeatFlat);
        ModifyDamage(args.Damage, "Cold", ent.Comp.ColdMultiplier, ent.Comp.ColdFlat);
    }

    private static void ModifyDamage(DamageSpecifier damage, string damageType, float multiplier, FixedPoint2 flat)
    {
        if (!damage.DamageDict.TryGetValue(damageType, out var value))
            return;

        var modified = FixedPoint2.New(value.Float() * multiplier) + flat;
        damage.DamageDict[damageType] = FixedPoint2.Max(FixedPoint2.Zero, modified);
    }

    private void OnEmpPulse(Entity<DamageableComponent> ent, ref EmpPulseEvent args) // Forge-Change
    {
        var spec = new DamageSpecifier();

        AddImplantEmpDamage(ent.Owner, spec);
        AddOrganEmpDamage(ent.Owner, spec);
        AddSurgicalArmorEmpDamage(ent.Owner, spec);

        if (spec.Empty)
            return;

        _damage.TryChangeDamage(ent, spec, interruptsDoAfters: false);
        args.Affected = true;
    } // Forge-Change

    private void AddImplantEmpDamage(EntityUid wearer, DamageSpecifier spec) // Forge-Change
    {
        if (!TryComp<ImplantedComponent>(wearer, out var implanted))
            return;

        foreach (var implant in implanted.ImplantContainer.ContainedEntities)
        {
            if (TryComp<ForgeEmpDamageComponent>(implant, out var directEmp))
                AddEmpDamage(spec, directEmp);

            if (TryComp<SubdermalImplantComponent>(implant, out var subdermal))
                AddEmpDamageFromRegistry(spec, subdermal.OnAdd);
        }
    } // Forge-Change

    private void AddOrganEmpDamage(EntityUid body, DamageSpecifier spec) // Forge-Change
    {
        foreach (var (organUid, organ) in _body.GetBodyOrgans(body))
        {
            if (!organ.Enabled)
                continue;

            if (TryComp<ForgeEmpDamageComponent>(organUid, out var directEmp))
                AddEmpDamage(spec, directEmp);

            AddEmpDamageFromRegistry(spec, organ.OnAdd);
        }
    } // Forge-Change

    private void AddSurgicalArmorEmpDamage(EntityUid body, DamageSpecifier spec) // Forge-Change
    {
        foreach (var (_, part) in _body.GetBodyChildren(body))
        {
            if (!part.Enabled)
                continue;

            if (part.ItemInsertionSlot.Item is not { } item ||
                !HasComp<ForgeSurgicalArmorImplantComponent>(item) ||
                !TryComp<SubdermalImplantComponent>(item, out var subdermal))
            {
                continue;
            }

            AddEmpDamageFromRegistry(spec, subdermal.OnAdd);
        }
    } // Forge-Change

    private static void AddEmpDamageFromRegistry(DamageSpecifier spec, ComponentRegistry? registry) // Forge-Change
    {
        if (registry == null)
            return;

        foreach (var (_, component) in registry)
        {
            if (component.Component is ForgeEmpDamageComponent empDamage)
                AddEmpDamage(spec, empDamage);
        }
    } // Forge-Change

    private static void AddEmpDamage(DamageSpecifier spec, ForgeEmpDamageComponent empDamage) // Forge-Change
    {
        if (!spec.DamageDict.TryAdd(empDamage.DamageType, empDamage.Amount))
            spec.DamageDict[empDamage.DamageType] += empDamage.Amount;
    } // Forge-Change

    private void OnEmpDisableUnarmed(Entity<ForgeUnarmedDamageComponent> ent, ref EmpPulseEvent args)
    {
        ent.Comp.DisabledUntil = _timing.CurTime + (args.Duration > TimeSpan.Zero ? args.Duration : TimeSpan.FromSeconds(10));
        args.Affected = true;
    }

    private void OnGetUnarmedDamage(Entity<BodyComponent> ent, ref GetMeleeDamageEvent args) // Forge-Change
    {
        if (args.User != ent.Owner || args.Weapon != args.User)
            return;

        var bonus = new DamageSpecifier();

        if (TryComp<ForgeUnarmedDamageComponent>(ent, out var directDamage))
        {
            if (_timing.CurTime < directDamage.DisabledUntil)
                return;

            AddUnarmedDamageFromParts(ent.Owner, bonus);
            if (bonus.Empty)
                AddUnarmedDamage(bonus, directDamage);
        }
        else
        {
            AddUnarmedDamageFromParts(ent.Owner, bonus);
        }

        if (bonus.Empty)
            return;

        args.Damage += bonus;
    } // Forge-Change

    private void AddUnarmedDamageFromParts(EntityUid body, DamageSpecifier spec) // Forge-Change
    {
        foreach (var (_, part) in _body.GetBodyChildren(body))
        {
            if (!part.Enabled)
                continue;

            AddUnarmedDamageFromRegistry(spec, part.OnAdd);
        }
    } // Forge-Change

    private static void AddUnarmedDamageFromRegistry(DamageSpecifier spec, ComponentRegistry? registry) // Forge-Change
    {
        if (registry == null)
            return;

        foreach (var (_, component) in registry)
        {
            if (component.Component is ForgeUnarmedDamageComponent damage)
                AddUnarmedDamage(spec, damage);
        }
    } // Forge-Change

    private static void AddUnarmedDamage(DamageSpecifier spec, ForgeUnarmedDamageComponent damage) // Forge-Change
    {
        if (!spec.DamageDict.TryAdd(damage.DamageType, damage.Amount))
            spec.DamageDict[damage.DamageType] += damage.Amount;
    } // Forge-Change

    private void OnInjectOnUnarmedHit(Entity<ForgeInjectOnUnarmedHitComponent> ent, ref MeleeHitEvent args)
    {
        if (!args.IsHit || args.HitEntities.Count == 0 || args.User != ent.Owner || args.Weapon != args.User)
            return;

        var bonus = new DamageSpecifier();
        bonus.DamageDict[ent.Comp.DamageType] = ent.Comp.Amount;
        args.BonusDamage += bonus;
    }

    private void OnBloodstreamCleanerStartup(Entity<ForgeBloodstreamCleanerComponent> ent, ref ComponentStartup args)
    {
        ent.Comp.NextUpdate = _timing.CurTime + ent.Comp.UpdateInterval;
    }

    private void OnRadiationCleanerStartup(Entity<ForgeRadiationCleanerComponent> ent, ref ComponentStartup args)
    {
        ent.Comp.NextUpdate = _timing.CurTime + ent.Comp.UpdateInterval;
    }

    private void CleanBloodstream(Entity<ForgeBloodstreamCleanerComponent, BloodstreamComponent> ent)
    {
        if (!_solution.ResolveSolution(ent.Owner, ent.Comp2.ChemicalSolutionName, ref ent.Comp2.ChemicalSolution, out var chemSolution))
            return;

        for (var i = chemSolution.Contents.Count - 1; i >= 0; i--)
        {
            var (reagent, _) = chemSolution.Contents[i];
            if (!_proto.TryIndex<ReagentPrototype>(reagent.Prototype, out var proto) || proto.Metabolisms == null)
                continue;

            foreach (var group in ent.Comp1.MetabolismGroups)
            {
                if (!proto.Metabolisms.ContainsKey(group))
                    continue;

                _solution.RemoveReagent(ent.Comp2.ChemicalSolution.Value, reagent, ent.Comp1.Amount);
                break;
            }
        }

        var spec = new DamageSpecifier();
        spec.DamageDict["Poison"] = -ent.Comp1.Amount;
        _damage.TryChangeDamage(ent.Owner, spec, interruptsDoAfters: false);
    }

    private void HealRadiation(EntityUid uid, ForgeRadiationCleanerComponent component)
    {
        var spec = new DamageSpecifier();
        spec.DamageDict["Radiation"] = -component.Amount;
        _damage.TryChangeDamage(uid, spec, interruptsDoAfters: false);
    }
}
