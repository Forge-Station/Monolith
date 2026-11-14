namespace Content.Shared.Materials.OreSilo;

public abstract partial class SharedOreSiloSystem
{
    /// <summary>
    /// Whether a powered silo can reach a client on its own grid within the given range.
    /// </summary>
    public bool CanTransmit(Entity<TransformComponent?> silo, EntityUid client, float range)
    {
        if (!Resolve(silo, ref silo.Comp))
            return false;

        if (!_powerReceiver.IsPowered(silo.Owner))
            return false;

        if (_transform.GetGrid(client) != _transform.GetGrid(silo.Owner))
            return false;

        return _transform.InRange((silo.Owner, silo.Comp), client, range);
    }
}
