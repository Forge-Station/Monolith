using Content.Shared.Body.Components;
using Content.Shared.DragDrop;

namespace Content.Shared._WF.Wolfmed.Medical;

/// <summary>
/// The half of the IV drip the client needs: dragging the stand onto a patient. The drag checks run on the client
/// before anything reaches the server, so they have to be answered here.
/// </summary>
public sealed class SharedWolfmedIvDripSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WolfmedIvDripComponent, CanDragEvent>(OnCanDrag);
        SubscribeLocalEvent<WolfmedIvDripComponent, CanDropDraggedEvent>(OnCanDropDragged);
    }

    private void OnCanDrag(Entity<WolfmedIvDripComponent> ent, ref CanDragEvent args)
    {
        args.Handled = true;
    }

    private void OnCanDropDragged(Entity<WolfmedIvDripComponent> ent, ref CanDropDraggedEvent args)
    {
        if (!HasComp<BodyComponent>(args.Target))
            return;

        args.CanDrop = true;
        args.Handled = true;
    }
}
