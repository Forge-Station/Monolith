using Content.Shared.Emoting;
using Content.Shared.Hands;
using Content.Shared.Interaction.Events;
using Content.Shared.Item;
using Content.Shared.Pointing;
using Content.Shared.Speech;
using Content.Shared.Throwing;

namespace Content.Shared._WF.MappingMode;

/// <summary>
/// Keeps mapping observers out of the world: no interaction, pickup, speech, emotes, throwing, attacks or pointing.
/// </summary>
public sealed class WFMappingObserverSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WFMappingObserverComponent, InteractionAttemptEvent>(OnInteractionAttempt);
        SubscribeLocalEvent<WFMappingObserverComponent, UseAttemptEvent>(OnAttempt);
        SubscribeLocalEvent<WFMappingObserverComponent, PickupAttemptEvent>(OnAttempt);
        SubscribeLocalEvent<WFMappingObserverComponent, DropAttemptEvent>(OnAttempt);
        SubscribeLocalEvent<WFMappingObserverComponent, EmoteAttemptEvent>(OnAttempt);
        SubscribeLocalEvent<WFMappingObserverComponent, SpeakAttemptEvent>(OnAttempt);
        SubscribeLocalEvent<WFMappingObserverComponent, ThrowAttemptEvent>(OnAttempt);
        SubscribeLocalEvent<WFMappingObserverComponent, AttackAttemptEvent>(OnAttempt);
        SubscribeLocalEvent<WFMappingObserverComponent, PointAttemptEvent>(OnAttempt);
    }

    private void OnInteractionAttempt(Entity<WFMappingObserverComponent> ent, ref InteractionAttemptEvent args)
    {
        args.Cancelled = true;
    }

    private void OnAttempt(EntityUid uid, WFMappingObserverComponent component, CancellableEntityEventArgs args)
    {
        args.Cancel();
    }
}
