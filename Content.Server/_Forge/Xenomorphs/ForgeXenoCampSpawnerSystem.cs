namespace Content.Server._Forge.Xenomorphs;

public sealed class ForgeXenoCampSpawnerSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ForgeXenoCampSpawnerComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(Entity<ForgeXenoCampSpawnerComponent> ent, ref MapInitEvent args)
    {
        var coords = Transform(ent).Coordinates;
        foreach (var piece in ent.Comp.Pieces)
            Spawn(piece.Proto, coords.Offset(piece.Offset));

        QueueDel(ent);
    }
}
