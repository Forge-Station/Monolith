namespace Content.Shared._Forge.ReviveReward;

public sealed class EntityRevivedEvent : EntityEventArgs
{
    public readonly EntityUid Target;
    public readonly EntityUid Reviver;

    public EntityRevivedEvent(EntityUid target, EntityUid reviver)
    {
        Target = target;
        Reviver = reviver;
    }
}
