using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Content.Shared.Damage;
using Content.Shared._Forge.Body.Components;
using Content.Shared._Forge.Body.Syndromes;
using Content.Server._Forge.Body.Syndromes.Triggers;

namespace Content.Server._Forge.Body.Syndromes;

public sealed partial class SyndromeTriggerSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SyndromeSystem _disease = default!;
    private readonly Dictionary<(EntityUid, string, int), TimeSpan> _cooldowns = new();
    private readonly Dictionary<string, List<(int index, DamageTrigger trigger)>> _triggerCache = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BodyPhysiologyComponent, DamageChangedEvent>(OnBodyDamageChanged);
        SubscribeLocalEvent<OrganPhysiologyComponent, DamageChangedEvent>(OnOrganDamageChanged);
        SubscribeLocalEvent<BodyPhysiologyComponent, EntityTerminatingEvent>(OnBodyTerminating);
        SubscribeLocalEvent<OrganPhysiologyComponent, EntityTerminatingEvent>(OnOrganTerminating);
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);

        RebuildCache();
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _cooldowns.Clear();
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (args.WasModified<SyndromePrototype>())
            RebuildCache();
    }

    private void OnBodyTerminating(EntityUid uid, BodyPhysiologyComponent comp, ref EntityTerminatingEvent args)
    {
        CleanupEntity(uid);
    }

    private void OnOrganTerminating(EntityUid uid, OrganPhysiologyComponent comp, ref EntityTerminatingEvent args)
    {
        CleanupEntity(uid);
    }

    private void CleanupEntity(EntityUid uid)
    {
        var keysToRemove = new List<(EntityUid, string, int)>();

        foreach (var key in _cooldowns.Keys)
        {
            if (key.Item1 == uid)
                keysToRemove.Add(key);
        }

        foreach (var key in keysToRemove)
        {
            _cooldowns.Remove(key);
        }
    }

    public void RebuildCache()
    {
        _triggerCache.Clear();

        foreach (var proto in _prototypes.EnumeratePrototypes<SyndromePrototype>())
        {
            if (proto.Triggers.Count == 0)
                continue;

            var triggers = new List<(int, DamageTrigger)>();

            for (var i = 0; i < proto.Triggers.Count; i++)
            {
                if (proto.Triggers[i] is not DamageTrigger trigger)
                    continue;

                triggers.Add((i, trigger));
            }

            if (triggers.Count > 0)
                _triggerCache[proto.ID] = triggers;
        }
    }

    private void OnBodyDamageChanged(EntityUid uid, BodyPhysiologyComponent comp, DamageChangedEvent args)
    {
        OnDamageChanged(uid, args);
    }

    private void OnOrganDamageChanged(EntityUid uid, OrganPhysiologyComponent comp, DamageChangedEvent args)
    {
        OnDamageChanged(uid, args);
    }

    private void OnDamageChanged(EntityUid uid, DamageChangedEvent args)
    {
        if (args.DamageDelta == null || !args.DamageIncreased)
            return;

        var now = _timing.CurTime;

        foreach (var (protoId, triggers) in _triggerCache)
        {
            foreach (var (index, trigger) in triggers)
            {
                var cooldownKey = (uid, protoId, index);

                if (_cooldowns.TryGetValue(cooldownKey, out var nextAllowed) && now < nextAllowed)
                    continue;
                if (!CheckDamage(args, trigger))
                    continue;
                if (trigger.Chance < 1f && !_random.Prob(trigger.Chance))
                    continue;

                _disease.AddSyndrome(uid, protoId, trigger.Severity);
                _cooldowns[cooldownKey] = now + TimeSpan.FromSeconds(trigger.Cooldown);
            }
        }
    }

    private static bool CheckDamage(DamageChangedEvent args, DamageTrigger trigger)
    {
        return TryGetDamageAmount(args, trigger.DamageGroup, out var damage) && damage >= trigger.DamageThreshold;
    }

    private static bool TryGetDamageAmount(DamageChangedEvent args, string? damageGroup, out float damage)
    {
        damage = 0f;

        if (args.DamageDelta == null)
            return false;

        if (damageGroup != null)
        {
            if (!args.DamageDelta.DamageDict.TryGetValue(damageGroup, out var value))
                return false;

            damage = value.Float();
            return true;
        }

        damage = args.DamageDelta.GetTotal().Float();
        return true;
    }
}
