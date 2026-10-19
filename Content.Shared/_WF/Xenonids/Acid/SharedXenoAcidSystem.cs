using System.Linq;
using Content.Shared._WF.Xenonids.Plasma;
using Content.Shared._WF.Xenonids.Energy;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Coordinates;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.DoAfter;
using Content.Shared.Item;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Physics;
using Robust.Shared.Network;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Configuration;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Physics;
using Content.Shared._WF.Vehicles;
using Content.Shared._WF.Vehicles.Hardpoint;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Audio;

namespace Content.Shared._WF.Xenonids.Acid;

public abstract partial class SharedXenoAcidSystem : EntitySystem
{
    [Dependency] private XenoAcidHoleSystem _acidHole = default!;
    [Dependency] private CollisionWakeSystem _collisionWake = default!;
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedEntityStorageSystem _entityStorage = default!;
    [Dependency] private FixtureSystem _fixtures = default!;
    [Dependency] private HardpointSystem _hardpoints = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] protected IPrototypeManager PrototypeManager = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private XenoEnergySystem _xenoEnergy = default!;
    [Dependency] private XenoPlasmaSystem _xenoPlasma = default!;
    [Dependency] private SharedAudioSystem _audio = default!;

    protected int CorrosiveAcidTickDelaySeconds;
    protected ProtoId<DamageTypePrototype> CorrosiveAcidDamageTypeStr = "Heat";
    protected bool CorrosiveAcidInstant;

    // If a damage tick would happen within this amount of time after expiration, it will still be applied when the acid is removed.
    private const float ExpirationGracePeriodSeconds = 0.5f;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<XenoAcidComponent, XenoCorrosiveAcidEvent>(OnXenoCorrosiveAcid);
        SubscribeLocalEvent<XenoAcidComponent, DoAfterAttemptEvent<XenoCorrosiveAcidDoAfterEvent>>(OnXenoCorrosiveAcidDoAfterAttempt);
        SubscribeLocalEvent<XenoAcidComponent, XenoCorrosiveAcidDoAfterEvent>(OnXenoCorrosiveAcidDoAfter);

        SubscribeLocalEvent<InheritAcidComponent, AmmoShotEvent>(OnAmmoShot);
        // WOLFGATE: grenade acid inheritance and extinguisher vapor removal cut with RMC chemistry and grenades
        SubscribeLocalEvent<TimedCorrodingComponent, GettingPickedUpAttemptEvent>(OnAcidPickupAttempt);
        SubscribeLocalEvent<DamageableCorrodingComponent, GettingPickedUpAttemptEvent>(OnAcidPickupAttempt);

        SubscribeLocalEvent<PickupAttemptEvent>(OnPickupAttempt);

        Subs.CVar(_config,
            WFXenoAcidCVars.RMCCorrosiveAcidTickDelaySeconds,
            obj =>
            {
                CorrosiveAcidTickDelaySeconds = obj;
                OnXenoAcidSystemCVarsUpdated();
            },
            true);
        Subs.CVar(_config,
            WFXenoAcidCVars.RMCCorrosiveAcidDamageType,
            obj =>
            {
                CorrosiveAcidDamageTypeStr = obj;
                OnXenoAcidSystemCVarsUpdated();
            },
            true);
        Subs.CVar(_config,
            WFXenoAcidCVars.RMCCorrosiveAcidInstant,
            obj => CorrosiveAcidInstant = obj,
            true);
    }

    private void OnXenoAcidSystemCVarsUpdated()
    {
        // If any of the relevant vars changed - we need to recalculate and update damage specifiers for all the corroding comps.
        // There is still a bit of a problem here - if AcidTickDelaySeconds changes, it will affect next tick damage-wise immediately while the time of the next tick will not change. It's an edge case though, I'd not expect anybody changing that CVar repeatedly during the round often enough for it to matter. So I'm not going to bother with it.
        var damageableCorrodingQuery = EntityQueryEnumerator<DamageableCorrodingComponent>();
        while (damageableCorrodingQuery.MoveNext(out var uid, out var damageableCorrodingComponent))
        {
            damageableCorrodingComponent.Damage = new(PrototypeManager.Index<DamageTypePrototype>(CorrosiveAcidDamageTypeStr), damageableCorrodingComponent.Dps * CorrosiveAcidTickDelaySeconds);
        }
    }

    private void OnXenoCorrosiveAcid(Entity<XenoAcidComponent> xeno, ref XenoCorrosiveAcidEvent args)
    {
        var target = args.Target;
        // WOLFGATE: dropship attachment points cut

        if (xeno.Owner != args.Performer ||
            !CheckCorrodiblePopupsWithReplacement(xeno, target, args.Strength, out var time, out var _))
        {
            return;
        }

        args.Handled = true;

        var doAfterEvent = new XenoCorrosiveAcidDoAfterEvent(args);
        var delay = time * args.ApplyTimeMultiplier;
        if (CorrosiveAcidInstant)
        {
            delay = TimeSpan.Zero;
        }

        var doAfter = new DoAfterArgs(EntityManager, xeno, delay, doAfterEvent, xeno, target)
        {
            BreakOnMove = true,
            RequireCanInteract = false,
            AttemptFrequency = AttemptFrequency.StartAndEnd
        };

        _doAfter.TryStartDoAfter(doAfter);
    }

    private void OnXenoCorrosiveAcidDoAfterAttempt(Entity<XenoAcidComponent> ent, ref DoAfterAttemptEvent<XenoCorrosiveAcidDoAfterEvent> args)
    {
        if (args.Cancelled)
            return;

        if (_mobState.IsIncapacitated(ent))
            args.Cancel();
    }

    private void OnXenoCorrosiveAcidDoAfter(Entity<XenoAcidComponent> xeno, ref XenoCorrosiveAcidDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled || args.Target is not { } target)
            return;

        if (!TryComp(target, out CorrodibleComponent? corrodible) || !corrodible.IsCorrodible)
            return;

        if (!xeno.Comp.CanMeltStructures && corrodible.Structure)
            return;

        if (HasComp<HardpointSlotsComponent>(target))
        {
            if (args.PlasmaCost != 0 && !_xenoPlasma.TryRemovePlasmaPopup(xeno.Owner, args.PlasmaCost))
                return;

            if (args.EnergyCost != 0 && !_xenoEnergy.TryRemoveEnergyPopup(xeno.Owner, args.EnergyCost))
                return;

            if (_net.IsClient)
                return;

            args.Handled = true;

            var vehicleDamage = args.VehicleDamage ?? GetDefaultVehicleAcidDamage(args.Strength);
            _hardpoints.DamageHardpoint(target, target, vehicleDamage);
            return;
        }

        // Re-check if acid can be replaced at DoAfter end to prevent race conditions
        // (e.g., weak acid downgrading strong acid if both DoAfters were started before any completed)
        if (IsMelted(target) && !CanReplaceAcid(target, args.Strength))
            return;

        var mult = corrodible.MeltTimeMult;

        if (args.PlasmaCost != 0 && !_xenoPlasma.TryRemovePlasmaPopup(xeno.Owner, args.PlasmaCost))
            return;

        if (args.EnergyCost != 0 && !_xenoEnergy.TryRemoveEnergyPopup(xeno.Owner, args.EnergyCost))
            return;

        if (_net.IsClient)
            return;

        args.Handled = true;

        // Remove existing acid if present (we validated we can replace it above)
        if (_net.IsServer && IsMelted(target))
            RemoveAcid(target);

        if (_acidHole.HasActiveHole(target))
            return;

        _acidHole.TryStoreAcidDirection(target, xeno.Owner);

        var acidTime = args.Time * mult;
        if (CorrosiveAcidInstant)
            acidTime = TimeSpan.Zero;

        ApplyAcid(args.AcidId, args.Strength, target, args.Dps, args.ExpendableLightDps, acidTime, args.AcidSound);
    }

    /// <summary>
    ///     Transfer any acid stacks from the cartridge to the shot ammo.
    /// </summary>
    private void OnAmmoShot(Entity<InheritAcidComponent> ent, ref AmmoShotEvent args)
    {
        if (!TryComp(ent, out TimedCorrodingComponent? corroding))
            return;

        foreach (var projectile in args.FiredProjectiles)
        {
            ApplyAcid(corroding.AcidPrototype, corroding.Strength, projectile, corroding.LightDps, corroding.Dps, corroding.CorrodesAt, null, true);
        }
    }


    private void OnAcidPickupAttempt<T>(EntityUid uid, T component, ref GettingPickedUpAttemptEvent args) where T : Component
    {
        if (args.Cancelled)
            return;

        args.Cancel();
        _popup.PopupClient(Loc.GetString("rmc-acid-pickup-blocked", ("target", args.Item)), args.User, args.User, PopupType.SmallCaution);
    }


    private void OnPickupAttempt(PickupAttemptEvent args)
    {
        if (args.Cancelled)
            return;

        if (!HasComp<TimedCorrodingComponent>(args.Item) &&
            !HasComp<DamageableCorrodingComponent>(args.Item))
        {
            return;
        }

        args.Cancel();
        _popup.PopupClient(Loc.GetString("rmc-acid-pickup-blocked", ("target", args.Item)), args.User, args.User, PopupType.SmallCaution);
    }

    private bool CheckCorrodiblePopupsWithReplacement(Entity<XenoAcidComponent> xeno, EntityUid target, XenoAcidStrength newStrength, out TimeSpan time, out float mult)
    {
        time = TimeSpan.Zero;
        mult = 1;
        if (!TryComp(target, out CorrodibleComponent? corrodible) ||
            !corrodible.IsCorrodible)
        {
            _popup.PopupClient(Loc.GetString("cm-xeno-acid-not-corrodible", ("target", target)), xeno, xeno, PopupType.SmallCaution);
            return false;
        }

        if (_acidHole.HasActiveHole(target))
        {
            _popup.PopupClient(Loc.GetString("rmc-acid-hole-already-weakened"), xeno, xeno, PopupType.SmallCaution);
            return false;
        }

        // Check if acid already exists and if new acid can replace it
        if (IsMelted(target))
        {
            if (!CanReplaceAcid(target, newStrength))
            {
                _popup.PopupClient(Loc.GetString("cm-xeno-acid-already-corroding", ("target", target)), xeno, xeno);
                return false;
            }
        }

        if (!xeno.Comp.CanMeltStructures && corrodible.Structure)
        {
            _popup.PopupClient(Loc.GetString("rmc-xeno-acid-structure-unmeltable"), xeno, xeno);
            return false;
        }

        var hasRequiredAcidStrength = newStrength.CompareTo(corrodible.MinimumAcidStrength) >= 0;
        if (!hasRequiredAcidStrength)
        {
            _popup.PopupClient(Loc.GetString("rmc-xeno-acid-too-weak", ("target", target)), xeno, xeno, PopupType.SmallCaution);
            return false;
        }

        time = corrodible.TimeToApply;
        mult = corrodible.MeltTimeMult;

        return true;
    }

    public void ApplyAcid(EntProtoId acidId, XenoAcidStrength strength, EntityUid target, float dps, float lightDps,
        TimeSpan time, SoundSpecifier? acidSound = null, bool inherit = false)
    {
        if (_net.IsClient)
            return;

        EntityUid acid;
        if (HasComp<VisiblyAcidOutsideContainerComponent>(target) &&
            _container.TryGetContainingContainer(target, out var container))
        {
            acid = SpawnAttachedTo(acidId, container.Owner.ToCoordinates());
        }
        else
        {
            acid = SpawnAttachedTo(acidId, target.ToCoordinates());
        }

        if (!inherit)
            time += _timing.CurTime;

        var ev = new CorrodingEvent(acid, dps, lightDps, strength);
        RaiseLocalEvent(target, ref ev);
        if (ev.Cancelled)
            return;

        AddComp(target, new TimedCorrodingComponent
        {
            Acid = acid,
            AcidPrototype = acidId,
            Strength = strength,
            CorrodesAt = time,
            Dps = dps,
            LightDps = lightDps,
            AcidSound = acidSound,
        });

        if (acidSound is { } sound)
            _audio.PlayPvs(sound, target);

        EnsureAcidCollisionWake(target);
    }

    public override void Update(float frameTime)
    {
        if (_net.IsClient)
            return;

        var time = _timing.CurTime;

        var damageableCorrodingQuery = EntityQueryEnumerator<DamageableCorrodingComponent>();
        while (damageableCorrodingQuery.MoveNext(out var uid, out var damageableCorrodingComponent))
        {
            if (time >= damageableCorrodingComponent.NextDamageAt)
            {
                _damageable.TryChangeDamage(uid, damageableCorrodingComponent.Damage);
                damageableCorrodingComponent.NextDamageAt = time.Add(TimeSpan.FromSeconds(CorrosiveAcidTickDelaySeconds));
            }

            if (time >= damageableCorrodingComponent.AcidExpiresAt)
            {
                // Apply damage if the component is removed right before it would've applied a damage tick.
                var timeUntilDamageAfterExpire = damageableCorrodingComponent.NextDamageAt - damageableCorrodingComponent.AcidExpiresAt;
                if (timeUntilDamageAfterExpire >= TimeSpan.Zero && timeUntilDamageAfterExpire < TimeSpan.FromSeconds(ExpirationGracePeriodSeconds))
                    _damageable.TryChangeDamage(uid, damageableCorrodingComponent.Damage);

                var ev = new BeforeMeltedEvent();
                RaiseLocalEvent(uid, ref ev);

                QueueDel(damageableCorrodingComponent.Acid);
                RemCompDeferred<DamageableCorrodingComponent>(uid);
            }
        }

        var timedCorrodingQuery = EntityQueryEnumerator<TimedCorrodingComponent>();
        while (timedCorrodingQuery.MoveNext(out var uid, out var timedCorrodingComponent))
        {
            if (time < timedCorrodingComponent.CorrodesAt)
                continue;

            if (TryConsumeGunSecondWind(uid))
                continue;

            var ev = new BeforeMeltedEvent();
            RaiseLocalEvent(uid, ref ev);

            _audio.PlayPvs(timedCorrodingComponent.AcidSound, Transform(uid).Coordinates);

            if (_acidHole.TryCreateHoleFromMelt(uid))
            {
                QueueDel(timedCorrodingComponent.Acid);
                RemCompDeferred<TimedCorrodingComponent>(uid);
                continue;
            }

            _entityStorage.EmptyContents(uid);

            if (TryComp(uid, out StorageComponent? storage))
            {
                foreach (var contained in storage.Container.ContainedEntities.ToArray())
                {
                    if (!TryComp(contained, out CorrodibleComponent? corrodible) ||
                        !corrodible.IsCorrodible)
                    {
                        _container.Remove(contained, storage.Container);
                    }
                }
            }

            QueueDel(uid);
            QueueDel(timedCorrodingComponent.Acid);
        }
    }

    // WOLFGATE: RMC's gun second wind (an acid-proof first hit on a gun) is cut; callers keep this hook
    public bool TryConsumeGunSecondWind(EntityUid uid)
    {
        return false;
    }

    private static float GetDefaultVehicleAcidDamage(XenoAcidStrength strength)
    {
        return strength switch
        {
            XenoAcidStrength.Weak => 16f,
            XenoAcidStrength.Strong => 100f,
            _ => 40f,
        };
    }

    public bool IsMelted(EntityUid uid)
    {
        return HasComp<TimedCorrodingComponent>(uid) || HasComp<DamageableCorrodingComponent>(uid);
    }

    public bool CanReplaceAcid(EntityUid target, XenoAcidStrength newStrength)
    {
        // Get existing acid strength from the component
        XenoAcidStrength? existingStrength = null;

        if (TryComp<TimedCorrodingComponent>(target, out var timedCorroding))
            existingStrength = timedCorroding.Strength;
        else if (TryComp<DamageableCorrodingComponent>(target, out var damageableCorroding))
            existingStrength = damageableCorroding.Strength;

        if (existingStrength == null)
            return true;

        // Simple comparison - can only replace if new acid is stronger
        return newStrength > existingStrength;
    }

    public void RemoveAcid(EntityUid uid)
    {
        if (TryComp<TimedCorrodingComponent>(uid, out var timed))
        {
            QueueDel(timed.Acid);
            RemComp<TimedCorrodingComponent>(uid);
        }

        if (TryComp<DamageableCorrodingComponent>(uid, out var damageable))
        {
            QueueDel(damageable.Acid);
            RemComp<DamageableCorrodingComponent>(uid);
        }

        RestoreAcidCollisionWake(uid);
    }

    private void EnsureAcidCollisionWake(EntityUid uid)
    {
        if (_net.IsClient)
            return;

        if (!TryComp(uid, out CollisionWakeComponent? wake))
            return;

        if (HasComp<RMCAcidCollisionWakeOverrideComponent>(uid))
            return;

        var overrideComp = AddComp<RMCAcidCollisionWakeOverrideComponent>(uid);
        overrideComp.PreviousEnabled = wake.Enabled;

        _collisionWake.SetEnabled(uid, false, wake);
    }

    private void RestoreAcidCollisionWake(EntityUid uid)
    {
        if (_net.IsClient)
            return;

        if (!TryComp(uid, out RMCAcidCollisionWakeOverrideComponent? overrideComp))
            return;

        if (TryComp(uid, out CollisionWakeComponent? wake))
        {
            _collisionWake.SetEnabled(uid, overrideComp.PreviousEnabled, wake);
        }

        RemComp<RMCAcidCollisionWakeOverrideComponent>(uid);
    }

    /// <summary>
    ///     Set the entity's corrodible status.
    /// </summary>
    /// <param name="entity">The entity whose corrodible state is being changed</param>
    /// <param name="isCorrodible">The new corrodible value</param>
    public void SetCorrodible(EntityUid entity, bool isCorrodible)
    {
        var corrodible = EnsureComp<CorrodibleComponent>(entity);
        corrodible.IsCorrodible = isCorrodible;
        Dirty(entity, corrodible);
    }
}
