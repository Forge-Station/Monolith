using Content.Server.Chat.Systems;
using Content.Server._WF.RMCCore.Emote;
using Content.Shared._WF.RMCCore.OnCollide;
using Content.Shared._WF.Xenonids;
using Content.Shared.Chat.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.RMCCore.OnCollide;

public sealed partial class OnCollideSystem : SharedOnCollideSystem
{
    [Dependency] private RMCEmoteSystem _rmcEmotes = default!;

    protected override void DoEmote(Entity<DamageOnCollideComponent> ent, EntityUid other)
    {
        if (ent.Comp.Emote is not { } emote)
            return;

        if (HasComp<XenoComponent>(other) && ent.Comp.XenoEmote != null)
        {
            emote = ent.Comp.XenoEmote.Value;
        }
        else if (HasComp<XenoComponent>(other))
            return;

        _rmcEmotes.TryEmoteWithChat(other, emote);
    }
}
