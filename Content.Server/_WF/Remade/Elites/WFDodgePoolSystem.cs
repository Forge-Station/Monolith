using System.Linq;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Hitscan.Components;
using Content.Shared.Weapons.Hitscan.Events;
using Content.Shared.Weapons.Hitscan.Systems;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Random;

namespace Content.Server._WF.Remade.Elites;

/// <summary>Lets a twitcher's dodge pool turn bullets and beams aside, and refills it.</summary>
public sealed partial class WFDodgePoolSystem : EntitySystem
{
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private WFEliteSystem _elite = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFDodgePoolComponent, ProjectileReflectAttemptEvent>(OnProjectile);

        // A beam names what it hit before anything takes damage, so the twitcher can be struck from the list first.
        SubscribeLocalEvent<HitscanBasicRaycastComponent, HitscanRaycastFiredEvent>(OnHitscan,
            before: [typeof(HitscanReflectSystem), typeof(HitscanBasicVisualsSystem), typeof(HitscanBasicDamageSystem), typeof(HitscanStunSystem)]);
    }

    /// <summary>A bullet is about to hit. If the twitcher slips it, the bullet flies on through.</summary>
    private void OnProjectile(Entity<WFDodgePoolComponent> ent, ref ProjectileReflectAttemptEvent args)
    {
        if (args.Cancelled || !TryDodge(ent))
            return;

        args.Cancelled = true;
    }

    /// <summary>A beam has found its target. A twitcher that slips it is taken off the list, so the beam does nothing to it.</summary>
    private void OnHitscan(Entity<HitscanBasicRaycastComponent> ent, ref HitscanRaycastFiredEvent args)
    {
        if (args.Canceled || args.HitEntities.Count == 0)
            return;

        foreach (var hit in args.HitEntities.ToArray())
        {
            if (TryComp<WFDodgePoolComponent>(hit, out var pool) && TryDodge((hit, pool)))
                args.HitEntities.Remove(hit);
        }
    }

    /// <summary>Rolls the pool against one hit. A slip costs some of the pool now and then and throws the twitcher aside.</summary>
    private bool TryDodge(Entity<WFDodgePoolComponent> ent)
    {
        var comp = ent.Comp;
        if (_mobState.IsDead(ent) || !_random.Prob(Math.Clamp(comp.Pool / 100f, 0f, 1f)))
            return false;

        comp.Dodged++;
        _audio.PlayPvs(comp.Sound, ent);
        _popup.PopupEntity(Loc.GetString("wf-remade-elite-twitch", ("mob", ent.Owner)), ent);

        if (!_random.Prob(comp.DrainChance))
            return true;

        comp.Pool = Math.Max(0f, comp.Pool - comp.Usage);
        var angle = _random.NextAngle();
        _elite.Blink(ent, angle.ToVec(), comp.Blink);
        return true;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<WFDodgePoolComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.Pool < comp.Max && !_mobState.IsDead(uid))
                comp.Pool = Math.Min(comp.Max, comp.Pool + comp.Regen * frameTime);
        }
    }
}
