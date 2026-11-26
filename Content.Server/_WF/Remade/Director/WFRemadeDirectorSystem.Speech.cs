using Content.Server._WF.Remade.Conversion;
using Content.Server.Chat.Systems;
using Content.Shared.Radio.Components;

namespace Content.Server._WF.Remade.Director;

/// <summary>
/// Voices. Talking, talking into a radio and a radio speaker playing a message are all noises the Remade come to. A plain whisper
/// and an emote are the only ways to talk they can't hear.
/// </summary>
public sealed partial class WFRemadeDirectorSystem
{
    private const float SpeechRange = 12f;
    private const float ShoutRange = 18f;
    private const float RadioRange = 10f;

    private void OnSpoke(EntitySpokeEvent args)
    {
        if (HasComp<WFRemadeComponent>(args.Source))
            return;

        float range;
        if (HasComp<RadioSpeakerComponent>(args.Source))
            range = RadioRange;
        else if (args.Channel != null)
            range = RadioRange;
        else if (args.IsWhisper)
            return;
        else
            range = args.Message.TrimEnd().EndsWith('!') ? ShoutRange : SpeechRange;

        GiveAway(args.Source);
        Noise(_transform.GetMapCoordinates(args.Source), range);
    }
}
