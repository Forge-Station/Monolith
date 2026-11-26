using Content.Server._WF.Remade.Conversion;
using Robust.Shared.Timing;

namespace Content.Server._WF.Remade.Elites;

/// <summary>Shortens the turning of a corpse when the Infector doing it is an enhanced one.</summary>
public sealed partial class WFReanimateHasteSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<WFRemadeConvertingComponent>();
        while (query.MoveNext(out var uid, out var converting))
        {
            if (HasComp<WFReanimateHastenedComponent>(uid)
                || converting.Reanimator is not { } infector
                || !TryComp<WFReanimateHasteComponent>(infector, out var haste))
            {
                continue;
            }

            EnsureComp<WFReanimateHastenedComponent>(uid);
            var remaining = converting.EndsAt - now;
            if (remaining > TimeSpan.Zero)
                converting.EndsAt = now + remaining * haste.Share;
        }
    }
}
