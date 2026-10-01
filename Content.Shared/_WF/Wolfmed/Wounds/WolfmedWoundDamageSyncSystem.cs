using System.Linq;
using Content.Shared._Onyx.Wounds;
using Content.Shared._WF.Wolfmed.Compat;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Wolfmed.Wounds;

/// <summary>
/// Brings a part's damage back down to what its wounds still account for.
/// </summary>
/// <remarks>
/// Damage creates wounds, but a wound does not own the damage it was made from: the part's own
/// <see cref="DamageableComponent"/> carries it and the body totals every part. Surgery closes wounds
/// directly (the tend step, embedded removal) without going through the damage path, so a patient the pod
/// had put back together walked out with brute nothing could find - no wounds, no part the analyzer called
/// hurt, and a body total only a brute pack could clear. This lowers each wound-backed damage type to the
/// damage its remaining wounds represent, and never raises one, so damage nothing models is left alone.
/// </remarks>
public sealed class WolfmedWoundDamageSyncSystem : EntitySystem
{
    [Dependency] private INetManager _net = default!;
    [Dependency] private IPrototypeManager _protos = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private WolfmedDamageableSystem _damage = default!;
    [Dependency] private WoundSystem _wounds = default!;

    /// <summary>Every damage type any wound prototype is made from, rebuilt when prototypes reload.</summary>
    private HashSet<ProtoId<DamageTypePrototype>>? _woundBacked;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (args.WasModified<WoundPrototype>())
            _woundBacked = null;
    }

    /// <summary>Syncs every part of a body. What the pod does when it is finished with a patient.</summary>
    public void SyncBody(EntityUid body)
    {
        if (!_net.IsServer || !HasComp<WoundHostComponent>(body))
            return;

        foreach (var (part, _) in _body.GetBodyChildren(body))
            SyncPart(part);
    }

    /// <summary>Syncs one part. Safe to call after anything that closed or removed a wound.</summary>
    public void SyncPart(EntityUid part)
    {
        if (!_net.IsServer ||
            !TryComp(part, out WoundableComponent? woundable) ||
            !TryComp(part, out DamageableComponent? damageable))
            return;

        var accounted = new Dictionary<ProtoId<DamageTypePrototype>, FixedPoint2>();
        foreach (var wound in _wounds.GetWounds((part, woundable)))
        {
            if (!_protos.TryIndex(wound.Comp.Prototype, out var prototype))
                continue;

            foreach (var (type, settings) in prototype.DamageTypes)
            {
                if (settings.SeverityMultiplier <= 0f)
                    continue;

                accounted[type] = accounted.GetValueOrDefault(type) +
                                  wound.Comp.Severity / FixedPoint2.New(settings.SeverityMultiplier);
            }
        }

        var backed = WoundBacked();
        var target = damageable.Damage.Clone();
        var changed = false;
        foreach (var (type, amount) in damageable.Damage.DamageDict)
        {
            if (amount <= FixedPoint2.Zero || !backed.Contains(type))
                continue;

            var left = accounted.GetValueOrDefault(type);
            if (amount <= left)
                continue;

            target.DamageDict[type] = left;
            changed = true;
        }

        if (changed)
            _damage.SetDamage((part, damageable), target);
    }

    private HashSet<ProtoId<DamageTypePrototype>> WoundBacked()
    {
        if (_woundBacked != null)
            return _woundBacked;

        _woundBacked = _protos.EnumeratePrototypes<WoundPrototype>()
            .SelectMany(prototype => prototype.DamageTypes.Keys)
            .ToHashSet();

        return _woundBacked;
    }
}
