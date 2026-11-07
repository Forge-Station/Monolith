using Content.Shared.Body.Systems;
using Content.Shared.Mobs;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._Shitmed.Targeting.Events;
using Content.Shared.Body.Part;
using Robust.Shared.Player;

namespace Content.Server._Shitmed.Targeting;
public sealed partial class TargetingSystem : SharedTargetingSystem
{
    [Dependency] private SharedBodySystem _bodySystem = default!;
    [Dependency] private ISharedPlayerManager _players = default!; // Forge-Change

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<TargetChangeEvent>(OnTargetChange);
        SubscribeLocalEvent<TargetingComponent, MobStateChangedEvent>(OnMobStateChange);
    }

    private void OnTargetChange(TargetChangeEvent message, EntitySessionEventArgs args)
    {
        if (!TryComp<TargetingComponent>(GetEntity(message.Uid), out var target))
            return;

        target.Target = message.BodyPart;
        Dirty(GetEntity(message.Uid), target);
    }

    private void OnMobStateChange(EntityUid uid, TargetingComponent component, MobStateChangedEvent args)
    {
        // Revival is handled by the server, so we're keeping all of this here.
        var changed = false;

        if (args.NewMobState == MobState.Dead)
        {
            foreach (var part in GetValidParts())
            {
                component.BodyStatus[part] = TargetIntegrity.Dead;
                changed = true;
            }
            // I love groin shitcode.
            component.BodyStatus[TargetBodyPart.Groin] = TargetIntegrity.Dead;
        }
        else if (args.OldMobState == MobState.Dead && (args.NewMobState == MobState.Alive || args.NewMobState == MobState.Critical))
        {
            component.BodyStatus = _bodySystem.GetBodyPartStatus(uid);
            changed = true;
        }

        if (changed)
        {
            Dirty(uid, component);
            // Forge-Change: same guard as part damage. A disconnecting body must not be mailed this event.
            if (_players.TryGetSessionByEntity(uid, out var session) && session.Channel is { IsConnected: true })
                RaiseNetworkEvent(new TargetIntegrityChangeEvent(GetNetEntity(uid)), uid);
        }
    }
}
