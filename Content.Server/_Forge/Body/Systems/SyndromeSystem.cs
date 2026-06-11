using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Content.Shared.Body.Events;
using Content.Shared.Body.Organ;
using Content.Shared.Body.Systems;
using Content.Shared.StatusEffect;
using Content.Shared._Shitmed.Body.Organ;
using Content.Shared._Forge.Body.Components;
using Content.Shared._Forge.Body.Syndromes;

namespace Content.Server._Forge.Body.Syndromes;

public sealed partial class SyndromeSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private StatusEffectsSystem _statusEffects = default!;
    [Dependency] private SyndromeTriggerSystem _triggers = default!;
    [Dependency] private SharedBodySystem _body = default!;

    private ISawmill _sawmill = default!;
    private readonly HashSet<EntityUid> _activeEntities = new();
    private readonly List<EntityUid> _toAddEntities = new();
    private readonly List<EntityUid> _toRemoveEntities = new();
    private readonly List<string> _toRemoveSyndromes = new();

    public override void Initialize()
    {
        base.Initialize();

        _sawmill = Logger.GetSawmill("forge.physiology.syndrome");

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
        if (args.WasModified<SyndromePrototype>())
            _triggers.RebuildCache();
    }

    private void OnOrganEnableChanged(EntityUid uid, OrganPhysiologyComponent comp, ref OrganEnableChangedEvent args)
    {
        if (args.Enabled)
        {
            // Organ inserted: unfreeze syndromes and schedule next tick for each
            foreach (var (protoId, syndrome) in comp.Syndromes)
            {
                syndrome.Frozen = false;
                syndrome.NextUpdate = _timing.CurTime + GetInterval(protoId);
            }

            if (comp.Syndromes.Count > 0)
                _activeEntities.Add(uid);
        }
        else
        {
            // Organ removed: freeze all syndromes and stop processing this entity
            foreach (var syndrome in comp.Syndromes.Values)
                syndrome.Frozen = true;

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

            ProcessSyndromes(uid, holder);

            if (holder.Syndromes.Count == 0)
                _toRemoveEntities.Add(uid);
        }

        foreach (var uid in _toRemoveEntities)
            _activeEntities.Remove(uid);
        foreach (var uid in _toAddEntities)
            _activeEntities.Add(uid);

        _toAddEntities.Clear();
    }

    private void ProcessSyndromes(EntityUid uid, IPhysiologyHolder holder)
    {
        var now = _timing.CurTime;
        _toRemoveSyndromes.Clear();

        foreach (var (protoId, syndrome) in holder.Syndromes)
        {
            if (now < syndrome.NextUpdate)
                continue;

            if (!_prototypes.TryIndex<SyndromePrototype>(protoId, out var proto))
            {
                _toRemoveSyndromes.Add(protoId);
                continue;
            }

            syndrome.Severity -= proto.SeverityDecay;
            if (syndrome.Severity <= 0f)
            {
                if (proto.Permanent)
                {
                    syndrome.Severity = 0f;
                }
                else
                {
                    ClearSyndromeStatuses(uid, syndrome);
                    _toRemoveSyndromes.Add(protoId);
                }
                continue;
            }

            UpdateStage(syndrome, proto);

            // Stage changed: clear old status effects so new ones are applied cleanly
            if (syndrome.Stage != syndrome.LastStage)
            {
                ClearSyndromeStatuses(uid, syndrome);
                syndrome.LastStage = syndrome.Stage;
            }

            ApplySymptoms(uid, syndrome, proto);
            syndrome.NextUpdate = now + GetInterval(protoId);
        }

        foreach (var id in _toRemoveSyndromes)
            holder.Syndromes.Remove(id);
    }


    private TimeSpan GetInterval(string protoId)
    {
        var interval = _prototypes.TryIndex<SyndromePrototype>(protoId, out var proto) ? proto.Interval : 5f;
        return TimeSpan.FromSeconds(interval);
    }

    private void UpdateStage(SyndromeData syndrome, SyndromePrototype proto)
    {
        if (proto.Stages.Count == 0)
            return;
        var stage = 0;
        for (var i = 0; i < proto.Stages.Count; i++)
        {
            if (syndrome.Severity >= proto.Stages[i].MinSeverity)
                stage = i;
        }
        syndrome.Stage = stage;
    }

    private void ApplySymptoms(EntityUid holderUid, SyndromeData syndrome, SyndromePrototype proto)
    {
        if (syndrome.Stage >= proto.Stages.Count)
            return;

        var stage = proto.Stages[syndrome.Stage];
        var isFlaring = IsFlaring(syndrome, proto);
        var bodyUid = holderUid;

        if (TryComp<OrganComponent>(holderUid, out var organ) && organ.Body is { Valid: true } ownerBody)
            bodyUid = ownerBody;

        foreach (var symptomInterface in stage.Symptoms)
        {
            if (symptomInterface is not SyndromeSymptom symptom)
                continue;
            if (symptom.FlareOnly && !isFlaring)
                continue;
            if (symptom.Chance < 1f && !_random.Prob(symptom.Chance))
                continue;

            symptom.Apply(holderUid, bodyUid, syndrome, EntityManager);
        }
    }

    private void ClearSyndromeStatuses(EntityUid uid, SyndromeData syndrome)
    {
        foreach (var key in syndrome.ActiveStatus)
            _statusEffects.TryRemoveStatusEffect(uid, key);

        syndrome.ActiveStatus.Clear();
    }

    public bool IsFlaring(SyndromeData syndrome, SyndromePrototype proto)
    {
        if (proto.FlareDelay <= 0)
            return false;
        if (_timing.CurTime < syndrome.FlareSupressed)
            return false;

        return (_timing.CurTime - syndrome.LastProgression).TotalSeconds >= proto.FlareDelay;
    }

    /// <summary>
    /// Adds a syndrome to all valid targets resolved by the prototype's Organ flag.
    /// </summary>
    public void AddSyndrome(EntityUid uid, string protoId, float severity = 10f, IPhysiologyHolder? hint = null, bool refreshFlare = true)
    {
        if (!_prototypes.TryIndex<SyndromePrototype>(protoId, out var proto))
        {
            _sawmill.Error($"Syndrome prototype not found: {protoId}");
            return;
        }

        var targets = OrganResolver.Resolve(uid, proto.Organ, _body, EntityManager);
        _sawmill.Debug($"Syndrome {protoId} targets={targets.Count} organ={proto.Organ}");

        foreach (var target in targets)
        {
            IPhysiologyHolder? holder = null;
            if (hint != null && target == uid)
                holder = hint;
            if (holder == null && !TryGetHolder(target, out holder))
                continue;

            AddSyndromeToHolder(target, protoId, severity, refreshFlare, holder);
        }
    }

    private void AddSyndromeToHolder(EntityUid target, string protoId, float severity, bool refreshFlare, IPhysiologyHolder holder)
    {
        _sawmill.Debug($"Add syndrome {protoId} to {ToPrettyString(target)}");

        if (holder.Syndromes.TryGetValue(protoId, out var existing))
        {
            existing.Severity = Math.Clamp(existing.Severity + severity, 0f, 100f);
            if (refreshFlare)
                existing.LastProgression = _timing.CurTime;
            return;
        }

        holder.Syndromes[protoId] = new SyndromeData
        {
            PrototypeId = protoId,
            Severity = Math.Clamp(severity, 0f, 100f),
            NextUpdate = _timing.CurTime,
            LastProgression = refreshFlare ? _timing.CurTime : TimeSpan.Zero,
            LastStage = -1,
        };

        _toAddEntities.Add(target);
    }

    // Removes a syndrome from all targets resolved by the prototype's Organ flag.
    public void RemoveSyndrome(EntityUid uid, string protoId)
    {
        if (!_prototypes.TryIndex<SyndromePrototype>(protoId, out var proto))
            return;

        var targets = OrganResolver.Resolve(uid, proto.Organ, _body, EntityManager);

        foreach (var target in targets)
        {
            if (!TryGetHolder(target, out var holder))
                continue;
            if (holder.Syndromes.TryGetValue(protoId, out var syndrome))
                ClearSyndromeStatuses(target, syndrome);

            holder.Syndromes.Remove(protoId);
        }
    }

    public void SuppressFlare(EntityUid uid, string protoId, float duration)
    {
        if (!TryGetHolder(uid, out var holder))
            return;
        if (!holder.Syndromes.TryGetValue(protoId, out var syndrome))
            return;

        syndrome.FlareSupressed = _timing.CurTime + TimeSpan.FromSeconds(duration);
    }

    public void ModifySeverity(EntityUid uid, string protoId, float delta)
    {
        if (!_prototypes.TryIndex<SyndromePrototype>(protoId, out var proto))
            return;

        var targets = OrganResolver.Resolve(uid, proto.Organ, _body, EntityManager);

        foreach (var target in targets)
        {
            if (!TryGetHolder(target, out var holder))
                continue;
            if (!holder.Syndromes.TryGetValue(protoId, out var syndrome))
                continue;
            syndrome.Severity = Math.Clamp(syndrome.Severity + delta, 0f, 100f);
        }
    }

    public bool HasSyndrome(EntityUid uid, string protoId)
    {
        if (!_prototypes.TryIndex<SyndromePrototype>(protoId, out var proto))
            return false;

        var targets = OrganResolver.Resolve(uid, proto.Organ, _body, EntityManager);

        foreach (var target in targets)
        {
            if (!TryGetHolder(target, out var holder))
                continue;
            if (holder.Syndromes.ContainsKey(protoId))
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
