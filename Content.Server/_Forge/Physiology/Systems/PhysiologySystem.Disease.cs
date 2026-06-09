using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Content.Shared.Body.Events;
using Content.Shared.Body.Organ;
using Content.Shared.Body.Systems;
using Content.Shared.StatusEffect;
using Content.Shared._Shitmed.Body.Organ;
using Content.Shared._Forge.Physiology.Disease.Symptoms;
using Content.Server._Forge.Physiology.Disease.Symptoms;
using Content.Shared._Forge.Physiology;
using Content.Shared._Forge.Physiology.Disease;

namespace Content.Server._Forge.Physiology.Disease;

public sealed partial class PhysiologyDiseaseSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private StatusEffectsSystem _statusEffects = default!;
    [Dependency] private PhysiologyTriggerSystem _triggers = default!;
    [Dependency] private SharedBodySystem _body = default!;

    private ISawmill _sawmill = default!;
    private readonly HashSet<EntityUid> _activeEntities = new();
    private readonly List<EntityUid> _toRemoveEntities = new();
    private readonly List<string> _toRemoveDiseases = new();

    public override void Initialize()
    {
        base.Initialize();

        _sawmill = Logger.GetSawmill("forge.physiology.disease");

        SubscribeLocalEvent<BodyPhysiologyComponent, ComponentShutdown>(OnBodyShutdown);
        SubscribeLocalEvent<OrganPhysiologyComponent, ComponentShutdown>(OnOrganShutdown);
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);
        SubscribeLocalEvent<OrganPhysiologyComponent, OrganEnableChangedEvent>(OnOrganEnableChanged);
    }

    private void OnBodyShutdown(EntityUid uid, BodyPhysiologyComponent comp, ComponentShutdown args)
    {
        _activeEntities.Remove(uid);
    }

    private void OnOrganShutdown(EntityUid uid, OrganPhysiologyComponent comp, ComponentShutdown args)
    {
        _activeEntities.Remove(uid);
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (args.WasModified<DiseasePrototype>())
            _triggers.RebuildCache();
    }

    private void OnOrganEnableChanged(EntityUid uid, OrganPhysiologyComponent comp, ref OrganEnableChangedEvent args)
    {
        if (args.Enabled)
        {
            // Organ inserted: unfreeze diseases and schedule next tick for each
            foreach (var (protoId, disease) in comp.Diseases)
            {
                disease.Frozen = false;
                disease.NextUpdate = _timing.CurTime + GetInterval(protoId);
            }

            if (comp.Diseases.Count > 0)
                _activeEntities.Add(uid);
        }
        else
        {
            // Organ removed: freeze all diseases and stop processing this entity
            foreach (var disease in comp.Diseases.Values)
                disease.Frozen = true;

            _activeEntities.Remove(uid);
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _toRemoveEntities.Clear();

        foreach (var uid in _activeEntities)
        {
            if (!TryGetHolder(uid, out var holder))
            {
                _toRemoveEntities.Add(uid);
                continue;
            }

            ProcessDiseases(uid, holder);

            if (holder.Diseases.Count == 0)
                _toRemoveEntities.Add(uid);
        }

        foreach (var uid in _toRemoveEntities)
            _activeEntities.Remove(uid);
    }

    private void ProcessDiseases(EntityUid uid, IPhysiologyHolder holder)
    {
        var now = _timing.CurTime;
        _toRemoveDiseases.Clear();

        foreach (var (protoId, disease) in holder.Diseases)
        {
            if (now < disease.NextUpdate)
                continue;

            if (!_prototypes.TryIndex<DiseasePrototype>(protoId, out var proto))
            {
                _toRemoveDiseases.Add(protoId);
                continue;
            }

            disease.Severity -= proto.SeverityDecay;
            if (disease.Severity <= 0f)
            {
                if (proto.Permanent)
                {
                    disease.Severity = 0f;
                }
                else
                {
                    ClearDiseaseStatuses(uid, disease);
                    _toRemoveDiseases.Add(protoId);
                }
                continue;
            }

            UpdateStage(disease, proto);

            // Stage changed: clear old status effects so new ones are applied cleanly
            if (disease.Stage != disease.LastStage)
            {
                ClearDiseaseStatuses(uid, disease);
                disease.LastStage = disease.Stage;
            }

            ApplySymptoms(uid, disease, proto);
            disease.NextUpdate = now + GetInterval(protoId);
        }

        foreach (var id in _toRemoveDiseases)
            holder.Diseases.Remove(id);
    }


    private TimeSpan GetInterval(string protoId)
    {
        var interval = _prototypes.TryIndex<DiseasePrototype>(protoId, out var proto) ? proto.Interval : 5f;
        return TimeSpan.FromSeconds(interval);
    }

    private void UpdateStage(DiseaseData disease, DiseasePrototype proto)
    {
        if (proto.Stages.Count == 0)
            return;
        var stage = 0;
        for (var i = 0; i < proto.Stages.Count; i++)
        {
            if (disease.Severity >= proto.Stages[i].MinSeverity)
                stage = i;
        }
        disease.Stage = stage;
    }

    private void ApplySymptoms(EntityUid holderUid, DiseaseData disease, DiseasePrototype proto)
    {
        if (disease.Stage >= proto.Stages.Count)
            return;

        var stage = proto.Stages[disease.Stage];
        var isFlaring = IsFlaring(disease, proto);
        var bodyUid = holderUid;

        if (TryComp<OrganComponent>(holderUid, out var organ) && organ.Body is { Valid: true } ownerBody)
            bodyUid = ownerBody;

        foreach (var symptomInterface in stage.Symptoms)
        {
            if (symptomInterface is not DiseaseSymptom symptom)
                continue;
            if (symptom.FlareOnly && !isFlaring)
                continue;
            if (symptom.Chance < 1f && !_random.Prob(symptom.Chance))
                continue;

            symptom.Apply(holderUid, bodyUid, disease, EntityManager);
        }
    }

    private void ClearDiseaseStatuses(EntityUid uid, DiseaseData disease)
    {
        foreach (var key in disease.ActiveStatus)
            _statusEffects.TryRemoveStatusEffect(uid, key);

        disease.ActiveStatus.Clear();
    }

    public bool IsFlaring(DiseaseData disease, DiseasePrototype proto)
    {
        if (proto.FlareDelay <= 0)
            return false;
        if (_timing.CurTime < disease.FlareSupressed)
            return false;

        return (_timing.CurTime - disease.LastProgression).TotalSeconds >= proto.FlareDelay;
    }

    /// <summary>
    /// Adds a disease to all valid targets resolved by the prototype's Organ flag.
    /// </summary>
    public void AddDisease(EntityUid uid, string protoId, float severity = 10f, IPhysiologyHolder? hint = null, bool refreshFlare = true)
    {
        if (!_prototypes.TryIndex<DiseasePrototype>(protoId, out var proto))
        {
            _sawmill.Error($"Disease prototype not found: {protoId}");
            return;
        }

        var targets = PhysiologyOrganResolver.Resolve(uid, proto.Organ, _body, EntityManager);
        _sawmill.Debug($"Disease {protoId} targets={targets.Count} organ={proto.Organ}");

        foreach (var target in targets)
        {
            IPhysiologyHolder? holder = null;
            if (hint != null && target == uid)
                holder = hint;
            if (holder == null && !TryGetHolder(target, out holder))
                continue;

            AddDiseaseToHolder(target, protoId, severity, refreshFlare, holder);
        }
    }

    private void AddDiseaseToHolder(EntityUid target, string protoId, float severity, bool refreshFlare, IPhysiologyHolder holder)
    {
        _sawmill.Debug($"Add disease {protoId} to {ToPrettyString(target)}");

        if (holder.Diseases.TryGetValue(protoId, out var existing))
        {
            existing.Severity = Math.Clamp(existing.Severity + severity, 0f, 100f);
            if (refreshFlare)
                existing.LastProgression = _timing.CurTime;
            return;
        }

        holder.Diseases[protoId] = new DiseaseData
        {
            PrototypeId = protoId,
            Severity = Math.Clamp(severity, 0f, 100f),
            NextUpdate = _timing.CurTime,
            LastProgression = refreshFlare ? _timing.CurTime : TimeSpan.Zero,
            LastStage = -1,
        };

        _activeEntities.Add(target);
    }

    // Removes a disease from all targets resolved by the prototype's Organ flag.
    public void RemoveDisease(EntityUid uid, string protoId)
    {
        if (!_prototypes.TryIndex<DiseasePrototype>(protoId, out var proto))
            return;

        var targets = PhysiologyOrganResolver.Resolve(uid, proto.Organ, _body, EntityManager);

        foreach (var target in targets)
        {
            if (!TryGetHolder(target, out var holder))
                continue;
            if (holder.Diseases.TryGetValue(protoId, out var disease))
                ClearDiseaseStatuses(target, disease);

            holder.Diseases.Remove(protoId);
        }
    }

    public void SuppressFlare(EntityUid uid, string protoId, float duration)
    {
        if (!TryGetHolder(uid, out var holder))
            return;
        if (!holder.Diseases.TryGetValue(protoId, out var disease))
            return;

        disease.FlareSupressed = _timing.CurTime + TimeSpan.FromSeconds(duration);
    }

    public void ModifySeverity(EntityUid uid, string protoId, float delta)
    {
        if (!_prototypes.TryIndex<DiseasePrototype>(protoId, out var proto))
            return;

        var targets = PhysiologyOrganResolver.Resolve(uid, proto.Organ, _body, EntityManager);

        foreach (var target in targets)
        {
            if (!TryGetHolder(target, out var holder))
                continue;
            if (!holder.Diseases.TryGetValue(protoId, out var disease))
                continue;
            disease.Severity = Math.Clamp(disease.Severity + delta, 0f, 100f);
        }
    }

    public bool HasDisease(EntityUid uid, string protoId)
    {
        if (!_prototypes.TryIndex<DiseasePrototype>(protoId, out var proto))
            return false;

        var targets = PhysiologyOrganResolver.Resolve(uid, proto.Organ, _body, EntityManager);

        foreach (var target in targets)
        {
            if (!TryGetHolder(target, out var holder))
                continue;
            if (holder.Diseases.ContainsKey(protoId))
                return true;
        }

        return false;
    }

    private bool TryGetHolder(EntityUid uid, out IPhysiologyHolder holder)
    {
        if (TryComp<BodyPhysiologyComponent>(uid, out var body))
        {
            holder = body;
            return true;
        }
        if (TryComp<OrganPhysiologyComponent>(uid, out var organ))
        {
            holder = organ;
            return true;
        }

        holder = default!;
        return false;
    }
}
