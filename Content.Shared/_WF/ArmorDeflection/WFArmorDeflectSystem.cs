using Content.Shared.Examine;
using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;
using Content.Shared.Popups;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Reflect;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Network;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Random;

namespace Content.Shared._WF.ArmorDeflection;

/// <summary>
/// Ricochets light projectiles off <see cref="WFArmorDeflectComponent"/> armour. The round turns back the way it
/// came within a spread and becomes the wearer's, so it can hit anyone but them.
/// </summary>
public sealed class WFArmorDeflectSystem : EntitySystem
{
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFArmorDeflectComponent, GotEquippedEvent>(OnEquipped);
        SubscribeLocalEvent<WFArmorDeflectComponent, GotUnequippedEvent>(OnUnequipped);
        SubscribeLocalEvent<WFArmorDeflectComponent, ExaminedEvent>(OnExamined);
        // After upstream reflection, so a round a reflective item already turned isn't bounced twice.
        SubscribeLocalEvent<WFArmorDeflectWearerComponent, ProjectileReflectAttemptEvent>(OnReflectAttempt,
            after: [typeof(ReflectSystem)]);
    }

    private void OnEquipped(Entity<WFArmorDeflectComponent> ent, ref GotEquippedEvent args)
    {
        EnsureComp<WFArmorDeflectWearerComponent>(args.Equipee);
    }

    private void OnUnequipped(Entity<WFArmorDeflectComponent> ent, ref GotUnequippedEvent args)
    {
        // Another deflecting piece may still be worn.
        if (!TryGetDeflector(args.Equipee, out _, except: ent.Owner))
            RemComp<WFArmorDeflectWearerComponent>(args.Equipee);
    }

    private void OnExamined(Entity<WFArmorDeflectComponent> ent, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("wf-armor-deflect-examine", ("threshold", ent.Comp.Threshold.Int())));
    }

    /// <summary>The first worn deflecting piece that would bounce this round, if any.</summary>
    private bool TryGetDeflector(EntityUid wearer, out WFArmorDeflectComponent deflector, EntityUid? except = null,
        ProjectileComponent? round = null)
    {
        var slots = _inventory.GetSlotEnumerator(wearer);
        while (slots.NextItem(out var item))
        {
            if (item == except || !TryComp(item, out WFArmorDeflectComponent? found) ||
                round != null && !IsLightRound(found, round))
                continue;

            deflector = found;
            return true;
        }

        deflector = default!;
        return false;
    }

    private void OnReflectAttempt(Entity<WFArmorDeflectWearerComponent> ent, ref ProjectileReflectAttemptEvent args)
    {
        if (args.Cancelled || !TryGetDeflector(ent.Owner, out var armor, round: args.Component) ||
            !TryComp<PhysicsComponent>(args.ProjUid, out var physics))
            return;

        var rotation = _random.NextAngle(-armor.Spread / 2, armor.Spread / 2).Opposite();
        var existing = _physics.GetMapLinearVelocity(args.ProjUid, component: physics);
        var relative = existing - _physics.GetMapLinearVelocity(ent.Owner);
        var bounced = rotation.RotateVec(relative);
        _physics.SetLinearVelocity(args.ProjUid, physics.LinearVelocity + (bounced - existing), body: physics);
        var rot = Transform(args.ProjUid).LocalRotation;
        _transform.SetLocalRotation(args.ProjUid, rotation.RotateVec(rot.ToVec()).ToAngle());

        args.Cancelled = true;

        if (!_net.IsServer)
            return;

        _audio.PlayPvs(armor.Sound, ent.Owner);
        _popup.PopupEntity(Loc.GetString("wf-armor-deflect-popup", ("target", ent.Owner)), ent.Owner);
    }

    /// <summary>
    /// Whether a round is light enough to bounce. Armour-piercing rounds never are; soft rounds (hollow points, RIP)
    /// carry negative armour penetration and count for that much less against the plate.
    /// </summary>
    public static bool IsLightRound(WFArmorDeflectComponent armor, ProjectileComponent projectile)
    {
        if (projectile.IgnoreResistances || projectile.ArmorPenetration >= armor.PenetratingArmorPenetration ||
            !projectile.Damage.DamageDict.TryGetValue(armor.DamageType, out var raw) || raw <= 0)
            return false;

        var effective = raw * (1f + MathF.Min(projectile.ArmorPenetration, 0f));
        return effective <= armor.Threshold;
    }
}
