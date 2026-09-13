using System.Diagnostics.CodeAnalysis;
using Content.Shared._Shitmed.Humanoid.Events;
using Content.Shared._WF.CCVar;
using Content.Shared._WF.Genitals;
using Content.Shared._WF.Genitals.Components;
using Content.Shared._WF.Genitals.Systems;
using Content.Shared.Administration.Logs;
using Content.Shared.Body.Components;
using Content.Shared.Body.Events;
using Content.Shared.Body.Systems;
using Content.Shared.Database;
using Content.Shared.Humanoid;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Genitals;

/// <summary>Builds genital organs from the profile once the owner's consent is known, and keeps the body mirror current.</summary>
/// <remarks>
/// Server only: the client never builds organs and receives the mirror through GenitalsComponent. With the kill switch
/// off nothing is created or built and every mirror is empty; switching it back on restores them.
/// </remarks>
public sealed partial class GenitalOrganSystem : EntitySystem
{
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedGenitalsSystem _genitals = default!;
    [Dependency] private GenitalConsentSystem _consent = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private ISharedAdminLogManager _adminLog = default!;

    /// <summary>Set while DeleteOrgans runs, so OnOrganRemoved does not log those deletions as removals.</summary>
    private bool _deletingOrgans;

    private readonly List<EntityUid> _refreshBodies = new();

    /// <summary>Every genital slot, in build order.</summary>
    public static readonly GenitalSlot[] Slots =
    {
        GenitalSlot.Penis,
        GenitalSlot.Testicles,
        GenitalSlot.Vagina,
        GenitalSlot.Womb,
        GenitalSlot.Breasts,
    };

    public override void Initialize()
    {
        base.Initialize();

        // Shitmed owns (BodyComponent, ProfileLoadFinishedEvent); this pair is ours alone.
        SubscribeLocalEvent<HumanoidAppearanceComponent, ProfileLoadFinishedEvent>(OnProfileLoadFinished);
        SubscribeLocalEvent<GenitalOrganComponent, OrganAddedToBodyEvent>(OnOrganAdded);
        SubscribeLocalEvent<GenitalOrganComponent, OrganRemovedFromBodyEvent>(OnOrganRemoved);
        SubscribeLocalEvent<GenitalConsentChangedEvent>(OnConsentChanged);
        Subs.CVar(_cfg, WolfgateCVars.AnatomyEnabled, OnAnatomyEnabledChanged);

        InitializeTransfer();
    }

    /// <summary>Torso organ slot id of a genital slot. None of them contains, or is contained in, another organ slot id.</summary>
    public static string SlotId(GenitalSlot slot)
    {
        return slot switch
        {
            GenitalSlot.Penis => "wf_genital_penis",
            GenitalSlot.Testicles => "wf_genital_testicles",
            GenitalSlot.Vagina => "wf_genital_vagina",
            GenitalSlot.Womb => "wf_genital_womb",
            GenitalSlot.Breasts => "wf_genital_breasts",
            _ => throw new ArgumentOutOfRangeException(nameof(slot), slot, null),
        };
    }

    /// <summary>Organ entity prototype of a genital slot (organs.yml).</summary>
    public static EntProtoId OrganPrototype(GenitalSlot slot)
    {
        return slot switch
        {
            GenitalSlot.Penis => "OrganWFPenis",
            GenitalSlot.Testicles => "OrganWFTesticles",
            GenitalSlot.Vagina => "OrganWFVagina",
            GenitalSlot.Womb => "OrganWFWomb",
            GenitalSlot.Breasts => "OrganWFBreasts",
            _ => throw new ArgumentOutOfRangeException(nameof(slot), slot, null),
        };
    }

    /// <summary>The first player-profile load stores the configuration and slots; later loads only re-tint skin-matched organs.</summary>
    private void OnProfileLoadFinished(Entity<HumanoidAppearanceComponent> ent, ref ProfileLoadFinishedEvent args)
    {
        var uid = ent.Owner;
        if (args.Profile is not { } profile || !_genitals.AnatomyEnabled || TerminatingOrDeleted(uid))
            return;

        // Skips the ComponentInit default load, which runs before MapInit gives the body its parts.
        if (LifeStage(uid) < EntityLifeStage.MapInitialized
            || !TryComp<BodyComponent>(uid, out var body)
            || _body.GetRootPartOrNull(uid, body) == null)
            return;

        TryComp<GenitalsComponent>(uid, out var genitals);

        var species = profile.Species.Id;
        if (!_genitals.IsEligible(profile.Age, species))
        {
            if (genitals != null)
            {
                RemoveAnatomy(uid);
                _adminLog.Add(LogType.WFAnatomy, LogImpact.Low,
                    $"{ToPrettyString(uid):entity} anatomy removed: body no longer eligible");
            }

            return;
        }

        // Defence in depth: admin tools may pass profiles that never went through EnsureValid.
        var config = GenitalProfileValidator.EnsureValid(profile.Genitals, profile.Age, species, _proto);

        // Later loads (DNA scrambler, random re-rolls, antag reloads) carry no anatomy, so the character's own stays.
        if (genitals != null && (genitals.OrgansBuilt || genitals.SourceProfile != null))
        {
            Retint(uid);
            Recompute(uid);
            return;
        }

        // Random, default and NPC profiles never get a component or any networked state.
        if (config.IsEmpty || !EnsureAnatomy(uid))
            return;

        ApplyTemplate((uid, Comp<GenitalsComponent>(uid)), config);

        // At a station spawn the mind arrives later; OnConsentChanged builds the organs then.
        if (_consent.HasMaster(uid))
            BuildOrgans(uid, config);

        Recompute(uid);
    }

    /// <summary>Raised one tick after an anatomy toggle changed: creates anatomy for opted-in owners, builds pending organs, refreshes the mirror.</summary>
    private void OnConsentChanged(ref GenitalConsentChangedEvent ev)
    {
        var body = ev.Body;
        if (TerminatingOrDeleted(body))
            return;

        // With the kill switch off nothing is created or built; an existing mirror is still refreshed (and stays empty).
        var enabled = _genitals.AnatomyEnabled;
        var master = _consent.HasMaster(body);
        if (!TryComp<GenitalsComponent>(body, out var genitals))
        {
            // Opted-in players get the panel, reveal mode and transplant slots even without configured organs.
            if (!enabled || !master || !EnsureAnatomy(body))
                return;

            genitals = Comp<GenitalsComponent>(body);
        }

        if (enabled && master && !genitals.OrgansBuilt && genitals.SourceProfile is { } template)
            BuildOrgans(body, template);

        Recompute(body);
    }

    /// <summary>The kill switch flipped: empties or restores every mirror, and builds organs that waited while it was off.</summary>
    /// <remarks>Uses the new value directly: SharedGenitalsSystem's own callback may not have run yet.</remarks>
    private void OnAnatomyEnabledChanged(bool enabled)
    {
        _refreshBodies.Clear();
        var query = EntityQueryEnumerator<GenitalsComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            _refreshBodies.Add(uid);
        }

        foreach (var body in _refreshBodies)
        {
            if (TerminatingOrDeleted(body) || !TryComp<GenitalsComponent>(body, out var genitals))
                continue;

            if (enabled && !genitals.OrgansBuilt && genitals.SourceProfile is { } template && _consent.HasMaster(body))
                BuildOrgans(body, template);

            Recompute(body, enabled);
        }

        _refreshBodies.Clear();
    }

    private void OnOrganAdded(Entity<GenitalOrganComponent> ent, ref OrganAddedToBodyEvent args)
    {
        Recompute(args.Body);
    }

    /// <summary>Refreshes the old body's mirror and logs removals from living bodies. An organ leaving a body that is not opted in does not survive.</summary>
    private void OnOrganRemoved(Entity<GenitalOrganComponent> ent, ref OrganRemovedFromBodyEvent args)
    {
        var organ = ent.Owner;
        var body = args.OldBody;
        Recompute(body);

        // Deletions (rebuilds, gibs of bodies that are not opted in, deleted bodies) are not removals.
        if (_deletingOrgans || TerminatingOrDeleted(organ) || EntityManager.IsQueuedForDeletion(organ))
            return;

        if (!TerminatingOrDeleted(body) && !_mobState.IsDead(body))
        {
            _adminLog.Add(LogType.WFAnatomy, LogImpact.Medium,
                $"{ToPrettyString(organ):organ} removed from {ToPrettyString(body):entity}");
        }

        // Surgery needs the patient's consent, so this only catches dropped parts, admin tools and ghosted bodies.
        if (_consent.HasMaster(body))
            return;

        QueueDel(organ);
    }

    /// <summary>Adds GenitalsComponent and the five torso slots to an eligible body. False if not eligible.</summary>
    public bool EnsureAnatomy(EntityUid body)
    {
        if (!_genitals.IsEligible(body)
            || !TryComp<BodyComponent>(body, out var bodyComp)
            || _body.GetRootPartOrNull(body, bodyComp) is not { } root)
            return false;

        // Slots exist even when empty, so transplants into a character without that organ work.
        var added = false;
        foreach (var slot in Slots)
        {
            var slotId = SlotId(slot);
            if (root.BodyPart.Organs.ContainsKey(slotId))
                continue;

            if (_body.TryCreateOrganSlot(root.Entity, slotId, out _, root.BodyPart))
                added = true;
        }

        // TryCreateOrganSlot adds to the networked Organs map without dirtying it.
        if (added)
            Dirty(root.Entity, root.BodyPart);

        EnsureComp<GenitalsComponent>(body);
        return true;
    }

    /// <summary>Replaces the body's genital organs with ones built from the configuration.</summary>
    public void BuildOrgans(EntityUid body, GenitalProfile config)
    {
        if (!EnsureAnatomy(body) || !TryComp<HumanoidAppearanceComponent>(body, out var humanoid))
            return;

        // A rebuild never duplicates organs.
        DeleteOrgans(body);

        var settings = _genitals.Settings;
        foreach (var slot in Slots)
        {
            if (GenitalStateBuilder.FromProfile(config, slot, humanoid.SkinColor, settings) is not { } state)
                continue;

            // TesticleType.None means no organ; the validator normally drops it before this.
            if (slot == GenitalSlot.Testicles && state.Testicles == TesticleType.None)
                continue;

            TrySpawnOrgan(body, slot, state, out _);
        }

        Comp<GenitalsComponent>(body).OrgansBuilt = true;
    }

    /// <summary>Deletes all genital organs and removes GenitalsComponent (body no longer eligible). The empty slots stay.</summary>
    public void RemoveAnatomy(EntityUid body)
    {
        DeleteOrgans(body);
        RemComp<GenitalsComponent>(body);
    }

    /// <summary>Rebuilds the GenitalsComponent mirror from the organs currently in the body. Empty while the owner's master switch or the kill switch is off.</summary>
    public void Recompute(EntityUid body)
    {
        Recompute(body, _genitals.AnatomyEnabled);
    }

    private void Recompute(EntityUid body, bool enabled)
    {
        if (TerminatingOrDeleted(body) || !TryComp<GenitalsComponent>(body, out var comp))
            return;

        GenitalOrganState? penis = null;
        GenitalOrganState? testicles = null;
        GenitalOrganState? vagina = null;
        GenitalOrganState? breasts = null;
        var womb = false;

        // The organs stay in the body while either switch is off; only the mirror empties.
        if (enabled && _consent.HasMaster(body) && TryComp<BodyComponent>(body, out var bodyComp))
        {
            foreach (var organ in _body.GetBodyOrganEntityComps<GenitalOrganComponent>((body, bodyComp)))
            {
                var data = organ.Comp1;
                switch (data.Slot)
                {
                    case GenitalSlot.Penis:
                        penis ??= data.State;
                        break;
                    case GenitalSlot.Testicles:
                        testicles ??= data.State;
                        break;
                    case GenitalSlot.Vagina:
                        vagina ??= data.State;
                        break;
                    case GenitalSlot.Womb:
                        womb = true;
                        break;
                    case GenitalSlot.Breasts:
                        breasts ??= data.State;
                        break;
                }
            }
        }

        var changed = false;

        if (comp.Penis != penis)
        {
            comp.Penis = penis;
            DirtyField(body, comp, nameof(GenitalsComponent.Penis));
            changed = true;
        }

        if (comp.Testicles != testicles)
        {
            comp.Testicles = testicles;
            DirtyField(body, comp, nameof(GenitalsComponent.Testicles));
            changed = true;
        }

        if (comp.Vagina != vagina)
        {
            comp.Vagina = vagina;
            DirtyField(body, comp, nameof(GenitalsComponent.Vagina));
            changed = true;
        }

        if (comp.Womb != womb)
        {
            comp.Womb = womb;
            DirtyField(body, comp, nameof(GenitalsComponent.Womb));
            changed = true;
        }

        if (comp.Breasts != breasts)
        {
            comp.Breasts = breasts;
            DirtyField(body, comp, nameof(GenitalsComponent.Breasts));
            changed = true;
        }

        if (!changed)
            return;

        // Arousal resets react to this; Recompute never writes Arousal itself.
        var ev = new GenitalsChangedEvent();
        RaiseLocalEvent(body, ref ev);
    }

    /// <summary>Spawns one organ with the given state and inserts it. False if the slot is missing or full.</summary>
    public bool TrySpawnOrgan(EntityUid body, GenitalSlot slot, GenitalOrganState state, [NotNullWhen(true)] out EntityUid? organ)
    {
        organ = null;
        if (!TryComp<BodyComponent>(body, out var bodyComp)
            || _body.GetRootPartOrNull(body, bodyComp) is not { } root)
            return false;

        var slotId = SlotId(slot);
        if (!_body.CanInsertOrgan(root.Entity, slotId, root.BodyPart)
            || !_container.TryGetContainer(root.Entity, SharedBodySystem.GetOrganContainerId(slotId), out var container)
            || container.ContainedEntities.Count > 0)
            return false;

        var uid = Spawn(OrganPrototype(slot).Id, MapCoordinates.Nullspace);

        // Set before insertion: OrganAddedToBodyEvent recomputes the mirror from this state.
        var genitalOrgan = EnsureComp<GenitalOrganComponent>(uid);
        genitalOrgan.Slot = slot;
        genitalOrgan.State = state;

        if (!_body.InsertOrgan(root.Entity, uid, slotId, root.BodyPart))
        {
            Del(uid);
            return false;
        }

        organ = uid;
        return true;
    }

    /// <summary>Deletes every genital organ in the body at once, so none of them can drop.</summary>
    private void DeleteOrgans(EntityUid body)
    {
        if (!TryComp<BodyComponent>(body, out var bodyComp))
            return;

        var wasDeleting = _deletingOrgans;
        _deletingOrgans = true;
        try
        {
            foreach (var organ in _body.GetBodyOrganEntityComps<GenitalOrganComponent>((body, bodyComp)))
            {
                Del(organ.Owner);
            }
        }
        finally
        {
            _deletingOrgans = wasDeleting;
        }
    }

    /// <summary>Re-tints skin-matched organs to the body's applied skin colour; custom colours stay.</summary>
    private void Retint(EntityUid body)
    {
        if (!TryComp<HumanoidAppearanceComponent>(body, out var humanoid) || !TryComp<BodyComponent>(body, out var bodyComp))
            return;

        foreach (var organ in _body.GetBodyOrganEntityComps<GenitalOrganComponent>((body, bodyComp)))
        {
            organ.Comp1.State = GenitalStateBuilder.Retint(organ.Comp1.State, humanoid.SkinColor);
        }
    }

    /// <summary>Stores a configuration as the body's genetic template and resets runtime state to the template's defaults.</summary>
    private void ApplyTemplate(Entity<GenitalsComponent> ent, GenitalProfile? config)
    {
        ent.Comp.SourceProfile = config;
        ent.Comp.OrgansBuilt = false;
        (ent.Comp.RevealMode, ent.Comp.Visibility) = GenitalStateBuilder.RuntimeDefaults(config);
        ent.Comp.Arousal = 0;
        ent.Comp.Undergarments = UndergarmentFlags.None;

        // Marked per field: a field delta carries only marked fields, so a plain Dirty beside one organ DirtyField this
        // tick would drop these. Several fields at once always make a full state.
        DirtyFields(ent.Owner, ent.Comp, null,
            nameof(GenitalsComponent.RevealMode),
            nameof(GenitalsComponent.Visibility),
            nameof(GenitalsComponent.Arousal),
            nameof(GenitalsComponent.Undergarments));
    }
}
