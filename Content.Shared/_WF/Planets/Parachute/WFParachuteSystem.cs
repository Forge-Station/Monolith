using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._CE.ZLevels.Core.EntitySystems;
using Content.Shared._CE.ZLevels.Damage;
using Content.Shared.DoAfter;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Network;

namespace Content.Shared._WF.Planets.Parachute;

/// <summary>
/// Parachutes that open on a fall and hold it under the hurting speed; shared so the wearer's client predicts the fall.
/// </summary>
public sealed partial class WFParachuteSystem : EntitySystem
{
    [Dependency] private INetManager _net = default!;
    [Dependency] private CESharedZLevelsSystem _zLevels = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedHandsSystem _hands = default!;

    /// <summary>Height over the ground under which a canopy that has stopped sinking counts as landed.</summary>
    private const float LandedHeight = 0.05f;

    private readonly List<EntityUid> _landed = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WFParachuteComponent, AfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<WFParachuteComponent, UseInHandEvent>(OnUseInHand);
        SubscribeLocalEvent<WFParachuteComponent, WFParachuteAttachDoAfterEvent>(OnAttachDoAfter);
        SubscribeLocalEvent<WFParachutedComponent, CEZFallingDamageCalculateEvent>(OnFallDamage);
        SubscribeLocalEvent<WFParachutedComponent, CEZLevelFallMapEvent>(OnFallMap);

        // Ahead of the pat-and-hug popups, which would otherwise answer an empty-handed click on a person first.
        SubscribeLocalEvent<WFParachutedComponent, InteractHandEvent>(OnInteractHand, before: new[] { typeof(InteractionPopupSystem) });
        SubscribeLocalEvent<WFParachutedComponent, GetVerbsEvent<AlternativeVerb>>(OnGetVerbs);
    }

    private void OnAfterInteract(Entity<WFParachuteComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } target)
            return;

        args.Handled = TryStartAttach(ent, args.User, target);
    }

    private void OnUseInHand(Entity<WFParachuteComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = TryStartAttach(ent, args.User, args.User);
    }

    private bool TryStartAttach(Entity<WFParachuteComponent> ent, EntityUid user, EntityUid target)
    {
        // Anything the z-levels can drop can wear one; a wall or a floor tile cannot fall in the first place.
        if (!TryComp<CEZPhysicsComponent>(target, out var zPhysics) || !zPhysics.Fallable || Transform(target).Anchored)
        {
            _popup.PopupClient(Loc.GetString("wf-parachute-cannot-attach"), target, user);
            return false;
        }

        if (HasComp<WFParachutedComponent>(target))
        {
            _popup.PopupClient(Loc.GetString("wf-parachute-already"), target, user);
            return false;
        }

        return _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, user, ent.Comp.AttachTime,
            new WFParachuteAttachDoAfterEvent(), ent, target: target, used: ent)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
        });
    }

    private void OnAttachDoAfter(Entity<WFParachuteComponent> ent, ref WFParachuteAttachDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || args.Target is not { } target || HasComp<WFParachutedComponent>(target))
            return;

        args.Handled = true;

        EnsureComp<WFParachutedComponent>(target);
        _audio.PlayPredicted(ent.Comp.AttachSound, target, args.User);
        _popup.PopupClient(Loc.GetString(target == args.User ? "wf-parachute-attached-self" : "wf-parachute-attached",
            ("target", target)), target, args.User);

        PredictedQueueDel(ent.Owner);
    }

    /// <summary>An empty-handed click on the wearer takes the pack off again.</summary>
    private void OnInteractHand(Entity<WFParachutedComponent> ent, ref InteractHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = TryRemove(ent, args.User);
    }

    private void OnGetVerbs(Entity<WFParachutedComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanInteract || !args.CanAccess || ent.Comp.Deployed)
            return;

        var user = args.User;

        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("wf-parachute-remove-verb"),
            Act = () => TryRemove(ent, user),
        });
    }

    /// <summary>Unstraps a packed parachute into the hands of whoever took it off. An open canopy stays on until touchdown.</summary>
    public bool TryRemove(Entity<WFParachutedComponent> ent, EntityUid user)
    {
        if (ent.Comp.Deployed)
        {
            _popup.PopupClient(Loc.GetString("wf-parachute-remove-deployed"), ent, user);
            return false;
        }

        _popup.PopupClient(Loc.GetString(ent.Owner == user ? "wf-parachute-removed-self" : "wf-parachute-removed",
            ("target", ent.Owner)), ent, user);

        // The pack is the server's to make; the client sees the component go with the next state.
        if (_net.IsClient)
            return true;

        var pack = Spawn(ent.Comp.Pack, Transform(ent).Coordinates);
        _hands.PickupOrDrop(user, pack);
        RemComp<WFParachutedComponent>(ent);
        return true;
    }

    /// <summary>A held fall never reaches the hurting speed, but anything that still lands hard under canopy is spared.</summary>
    private void OnFallDamage(Entity<WFParachutedComponent> ent, ref CEZFallingDamageCalculateEvent args)
    {
        if (args.Fallen != ent.Owner || !ent.Comp.Deployed)
            return;

        args.DamageMultiplier = 0f;
        args.StunMultiplier = 0f;
    }

    /// <summary>The canopy opens on falling through a level, not on speed, so stepping off a ledge does not open it.</summary>
    private void OnFallMap(Entity<WFParachutedComponent> ent, ref CEZLevelFallMapEvent args)
    {
        if (ent.Comp.Deployed)
            return;

        ent.Comp.Deployed = true;
        Dirty(ent);

        if (_net.IsServer)
            _audio.PlayPvs(ent.Comp.DeploySound, ent);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _landed.Clear();

        var query = EntityQueryEnumerator<WFParachutedComponent, CEZPhysicsComponent>();

        while (query.MoveNext(out var uid, out var chute, out var zPhysics))
        {
            if (!chute.Deployed)
                continue;

            if (zPhysics.Velocity < -chute.FallSpeed)
                _zLevels.SetZVelocity((uid, zPhysics), -chute.FallSpeed);

            if (zPhysics.Velocity >= 0f && zPhysics.LocalPosition - zPhysics.CachedGroundHeight <= LandedHeight)
                _landed.Add(uid);
        }

        // The pack is the server's to hand back; the client only stops predicting the canopy.
        foreach (var uid in _landed)
        {
            if (!TryComp<WFParachutedComponent>(uid, out var chute))
                continue;

            if (_net.IsServer)
            {
                Spawn(chute.Pack, Transform(uid).Coordinates);
                RemComp<WFParachutedComponent>(uid);
            }
            else
            {
                chute.Deployed = false;
            }
        }
    }
}
