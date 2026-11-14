using Content.Shared.Shuttles.Components;

namespace Content.Server._WF.ShipAccess;

/// <summary>
/// A ship that breaks apart keeps its helm lock on every piece. Without it a piece's consoles fall back to their own
/// lock, locked by default, and a crashed ship's owner is shut out of their own helm.
/// </summary>
public sealed class WFShipGridLockSplitSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ShipGridLockComponent, GridSplitEvent>(OnSplit);
    }

    private void OnSplit(Entity<ShipGridLockComponent> ent, ref GridSplitEvent args)
    {
        foreach (var piece in args.NewGrids)
        {
            RemComp<ShipGridLockComponent>(piece);
            ShipGridLockComponent? source = ent.Comp;
            TryCopyComponent(ent, piece, ref source, out _);
        }
    }
}
