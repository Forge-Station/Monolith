using Robust.Shared.Serialization;


namespace Content.Shared._Forge.Trade;


[Serializable, NetSerializable,]
public enum StoreMode
{
    Buy,
    Sell,
    Barter
}
