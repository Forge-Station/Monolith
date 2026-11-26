using Content.Server.Chat.Systems;
using Content.Shared._WF.Xenonids.Neurotoxin;

namespace Content.Server._WF.Xenonids;

public sealed partial class NeurotoxinSystem : SharedNeurotoxinSystem
{
    [Dependency] ChatSystem _chat = default!;
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<NeurotoxinComponent, NeurotoxinEmoteEvent>(OnEmote);
    }

    public void OnEmote(Entity<NeurotoxinComponent> victim, ref NeurotoxinEmoteEvent args)
    {
        _chat.TryEmoteWithChat(victim, args.Emote);
    }
}
