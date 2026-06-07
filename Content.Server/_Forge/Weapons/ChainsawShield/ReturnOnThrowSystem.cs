using Content.Shared.Actions;
using Content.Shared._Forge.Weapons.ChainsawShield;
using Content.Shared.Hands;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Projectiles;
using Content.Shared.Throwing;
using Content.Server.Projectiles;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.GameStates;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._Forge.Weapons.ChainsawShield;

public sealed partial class ReturnOnThrowSystem : EntitySystem
{
    private const int MaxReturnActions = 12;

    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private ISharedPlayerManager _player = default!;
    [Dependency] private ProjectileSystem _projectiles = default!;
    [Dependency] private SharedPvsOverrideSystem _pvs = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ReturnOnThrowComponent, ThrownEvent>(OnThrown);
        SubscribeLocalEvent<ReturnOnThrowComponent, StopThrowEvent>(OnStopThrow);
        SubscribeLocalEvent<ReturnOnThrowComponent, EmbedEvent>(OnEmbedded);
        SubscribeLocalEvent<ReturnOnThrowComponent, ReturnOnThrowActionEvent>(OnReturnAction);
        SubscribeLocalEvent<ReturnOnThrowComponent, GotEquippedHandEvent>(OnGotEquippedHand);
        SubscribeLocalEvent<ReturnOnThrowComponent, EntGotInsertedIntoContainerMessage>(OnGotInsertedIntoContainer);
        SubscribeLocalEvent<ReturnOnThrowComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnThrown(EntityUid uid, ReturnOnThrowComponent component, ref ThrownEvent args)
    {
        if (args.User == null || !_player.TryGetSessionByEntity(args.User.Value, out _))
        {
            ClearReturn(uid, component);
            return;
        }

        GrantReturnAction(uid, component, args.User.Value, enabled: false);
    }

    private void OnStopThrow(EntityUid uid, ReturnOnThrowComponent component, ref StopThrowEvent args)
    {
        if (component.ReturnOwner == null)
            return;

        SetReturnReady(uid, component);
    }

    private void OnEmbedded(EntityUid uid, ReturnOnThrowComponent component, ref EmbedEvent args)
    {
        if (component.ReturnOwner == null)
            return;

        SetReturnReady(uid, component);
    }

    private void OnGotEquippedHand(EntityUid uid, ReturnOnThrowComponent component, ref GotEquippedHandEvent args)
    {
        ClearReturn(uid, component);
    }

    private void OnGotInsertedIntoContainer(EntityUid uid, ReturnOnThrowComponent component, EntGotInsertedIntoContainerMessage args)
    {
        ClearReturn(uid, component);
    }

    private void OnShutdown(EntityUid uid, ReturnOnThrowComponent component, ref ComponentShutdown args)
    {
        ClearReturn(uid, component);
    }

    private void OnReturnAction(EntityUid uid, ReturnOnThrowComponent component, ref ReturnOnThrowActionEvent args)
    {
        if (args.Handled || component.ReturnOwner != args.Performer || !component.ReturnReady)
            return;

        if (_timing.CurTime < component.NextReturnAttempt)
        {
            args.Handled = true;
            return;
        }

        component.NextReturnAttempt = _timing.CurTime + component.ReturnCooldown;
        args.Handled = true;
        TryReturn(uid, args.Performer, component);
    }

    private void GrantReturnAction(EntityUid uid, ReturnOnThrowComponent component, EntityUid owner, bool enabled)
    {
        if (component.ReturnOwner != null && component.ReturnOwner != owner)
            ClearReturn(uid, component);

        if (!HasReturnActionSlot(component, owner))
        {
            ClearReturn(uid, component);
            return;
        }

        if (!_actions.AddAction(owner, ref component.ReturnActionEntity, out _, component.ReturnAction, uid))
        {
            ClearReturn(uid, component);
            return;
        }

        component.ReturnOwner = owner;
        component.ReturnReady = enabled;
        _actions.SetEnabled(component.ReturnActionEntity, enabled);
        AddPvsOverride(uid, owner);
        Dirty(uid, component);
    }

    private bool HasReturnActionSlot(ReturnOnThrowComponent component, EntityUid owner)
    {
        if (component.ReturnOwner == owner &&
            component.ReturnActionEntity is { } existingAction &&
            !TerminatingOrDeleted(existingAction))
        {
            return true;
        }

        return HasReturnActionCapacity(owner);
    }

    private bool HasReturnActionCapacity(EntityUid owner)
    {
        var count = 0;
        var query = EntityQueryEnumerator<ReturnOnThrowComponent>();
        while (query.MoveNext(out _, out var component))
        {
            if (component.ReturnOwner != owner ||
                component.ReturnActionEntity is not { } action ||
                TerminatingOrDeleted(action))
            {
                continue;
            }

            count++;
            if (count >= MaxReturnActions)
                return false;
        }

        return true;
    }

    private void SetReturnReady(EntityUid uid, ReturnOnThrowComponent component)
    {
        if (component.ReturnActionEntity is not { } action ||
            TerminatingOrDeleted(action))
        {
            ClearReturn(uid, component);
            return;
        }

        component.ReturnReady = true;
        _actions.SetEnabled(action, true);
        Dirty(uid, component);
    }

    private bool TryReturn(EntityUid uid, EntityUid owner, ReturnOnThrowComponent component)
    {
        if (TerminatingOrDeleted(uid) || TerminatingOrDeleted(owner))
            return false;

        if (_containers.TryGetContainingContainer(uid, out _))
        {
            ClearReturn(uid, component);
            return false;
        }

        if (TryComp<EmbeddableProjectileComponent>(uid, out var embeddable) &&
            embeddable.EmbeddedIntoUid != null)
        {
            _projectiles.EmbedDetach(uid, embeddable, owner);
        }

        var pickedUp = component.ForcePickup
            ? _hands.TryForcePickupAnyHand(owner, uid, checkActionBlocker: true)
            : _hands.TryPickupAnyHand(owner, uid, checkActionBlocker: true);

        if (!pickedUp)
            return false;

        if (component.ReturnSound != null)
            _audio.PlayPvs(component.ReturnSound, uid);

        ClearReturn(uid, component);
        return true;
    }

    private void ClearReturn(EntityUid uid, ReturnOnThrowComponent component)
    {
        if (component.ReturnOwner != null)
            RemovePvsOverride(uid, component.ReturnOwner.Value);

        // RemoveAction detaches the contained action entity so it can be reused on the next throw.
        _actions.RemoveAction(component.ReturnActionEntity);
        component.ReturnOwner = null;
        component.ReturnReady = false;
        Dirty(uid, component);
    }

    private void AddPvsOverride(EntityUid uid, EntityUid owner)
    {
        if (_player.TryGetSessionByEntity(owner, out var session))
            _pvs.AddSessionOverride(uid, session);
    }

    private void RemovePvsOverride(EntityUid uid, EntityUid owner)
    {
        if (_player.TryGetSessionByEntity(owner, out var session))
            _pvs.RemoveSessionOverride(uid, session);
    }
}
