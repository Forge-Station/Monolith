using System.Linq;
using Content.Shared._Forge.Shipyard.Components;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Storage;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Forge.Shipyard;

public sealed class BlackboxDecoderSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<BlackboxDecoderComponent, InteractHandEvent>(OnInteractHand);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var query = EntityQueryEnumerator<BlackboxDecoderComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (!comp.Decoding || _timing.CurTime < comp.DecodeEndTime)
                continue;

            comp.Decoding = false;
            CompleteDecode(uid, comp);
        }
    }

    private void OnInteractHand(EntityUid uid, BlackboxDecoderComponent component, InteractHandEvent args)
    {
        if (args.Handled || component.Decoding)
            return;

        if (!TryComp<StorageComponent>(uid, out var storage) || storage.Container.ContainedEntities.Count == 0)
        {
            _popup.PopupEntity(Loc.GetString("blackbox-decoder-empty"), uid, args.User);
            return;
        }

        component.Decoding = true;
        component.DecodeEndTime = _timing.CurTime + component.DecodeTime;
        args.Handled = true;
        _popup.PopupEntity(Loc.GetString("blackbox-decoder-started"), uid, args.User);
    }

    private void CompleteDecode(EntityUid uid, BlackboxDecoderComponent component)
    {
        if (!TryComp<StorageComponent>(uid, out var storage))
            return;

        var pool = component.T1Outputs;
        foreach (var ent in storage.Container.ContainedEntities.ToList())
        {
            var id = MetaData(ent).EntityPrototype?.ID ?? "";
            if (id.Contains("T22") || id.Contains("T21"))
                pool = component.T2Outputs.Count > 0 ? component.T2Outputs : component.T1Outputs;

            Del(ent);
        }

        if (pool.Count == 0)
            return;

        Spawn(_random.Pick(pool), Transform(uid).Coordinates);
        _popup.PopupEntity(Loc.GetString("blackbox-decoder-complete"), uid);
    }
}
