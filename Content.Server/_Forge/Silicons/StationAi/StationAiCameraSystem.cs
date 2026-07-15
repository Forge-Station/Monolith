using Content.Server.Chat.Systems;
using Content.Server.Construction.Components;
using Content.Shared.Power.EntitySystems;
using Content.Shared.Silicons.StationAi;
using Content.Shared.StationAi;
using Content.Shared.SurveillanceCamera.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Player;

namespace Content.Server._Forge.Silicons.StationAi;

/// <summary>
/// Routes station AI local speech and hearing through operational surveillance cameras.
/// </summary>
public sealed class StationAiCameraSystem : EntitySystem
{
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedPowerReceiverSystem _power = default!;
    [Dependency] private readonly SharedStationAiSystem _stationAi = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    private const float SpeechCameraRange = 5f;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<StationAiHeldComponent, ResolveLocalSpeechSourceEvent>(OnResolveSpeechSource);
        SubscribeLocalEvent<ExpandICChatRecipientsEvent>(OnExpandRecipients);
    }

    private void OnResolveSpeechSource(Entity<StationAiHeldComponent> ent, ref ResolveLocalSpeechSourceEvent args)
    {
        if (!_stationAi.TryGetCore(ent.Owner, out var core) ||
            core.Comp is not { Remote: true, RemoteEntity: { } eye })
        {
            return;
        }

        var coreGrid = Transform(core.Owner).GridUid;
        if (coreGrid == null)
            return;

        var eyeCoordinates = _transform.GetMapCoordinates(eye);
        var eyePosition = eyeCoordinates.Position;
        EntityUid? closest = null;
        var closestDistance = float.MaxValue;

        foreach (var camera in _lookup.GetEntitiesInRange<SurveillanceCameraComponent>(eyeCoordinates, SpeechCameraRange))
        {
            if (Transform(camera).GridUid != coreGrid || !IsOperational(camera))
                continue;

            var distance = (_transform.GetMapCoordinates(camera).Position - eyePosition).LengthSquared();
            if (distance >= closestDistance)
                continue;

            closestDistance = distance;
            closest = camera;
        }

        if (closest != null)
            args.SpeechSource = closest.Value;
    }

    private void OnExpandRecipients(ExpandICChatRecipientsEvent ev)
    {
        var sourceCoordinates = _transform.GetMapCoordinates(ev.Source);
        var sourcePosition = sourceCoordinates.Position;
        var cameraRanges = new Dictionary<EntityUid, float>();

        // Spatial lookup bounds work to cameras which could actually hear this message.
        foreach (var camera in _lookup.GetEntitiesInRange<SurveillanceCameraComponent>(sourceCoordinates, ev.VoiceRange))
        {
            if (!IsOperational(camera) || Transform(camera).GridUid is not { } grid)
                continue;

            var distance = (_transform.GetMapCoordinates(camera).Position - sourcePosition).Length();
            if (!cameraRanges.TryGetValue(grid, out var current) || distance < current)
                cameraRanges[grid] = distance;
        }

        if (cameraRanges.Count == 0)
            return;

        // AI cores are rare; one bounded entity query avoids maintaining another global camera registry.
        var query = EntityQueryEnumerator<StationAiCoreComponent, TransformComponent>();
        while (query.MoveNext(out var coreUid, out var core, out var coreTransform))
        {
            if (coreTransform.GridUid is not { } grid ||
                !cameraRanges.TryGetValue(grid, out var range) ||
                !_stationAi.TryGetHeld((coreUid, core), out var brain) ||
                !TryComp(brain, out ActorComponent? actor))
            {
                continue;
            }

            ev.Recipients.TryAdd(actor.PlayerSession, new ChatSystem.ICChatRecipientData(range, false));
        }
    }

    private bool IsOperational(Entity<SurveillanceCameraComponent> camera)
    {
        if (!camera.Comp.Active ||
            !_power.IsPowered(camera.Owner) ||
            !TryComp(camera.Owner, out StationAiVisionComponent? vision) ||
            !vision.Enabled)
        {
            return false;
        }

        // The completed construction node is the only camera entity capable of new communication.
        return !TryComp(camera.Owner, out ConstructionComponent? construction) || construction.Node == "camera";
    }
}
