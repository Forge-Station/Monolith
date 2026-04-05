using System.Linq;
using Content.Shared._Forge.Barks;
using Content.Client._Forge.Barks;

namespace Content.Client.Lobby.UI; // No, it doesn't need to be changed. It will break the logic.

public sealed partial class HumanoidProfileEditor
{
    private List<BarkPrototype> _barkVoiceList = new();

    private void InitializeBarkVoice()
    {
        _barkVoiceList = _prototypeManager
            .EnumeratePrototypes<BarkPrototype>()
            .Where(o => o.RoundStart)
            // `BarkPrototype.Name` is human-readable and currently isn't backed by Fluent keys.
            // Using Loc.GetString here causes spam warnings for non-en cultures.
            .OrderBy(o => o.Name) // Forge-Change: added Forge-Change prefix to the function name
            .ToList();

        BarkVoiceButton.OnItemSelected += args =>
        {
            BarkVoiceButton.SelectId(args.Id);
            SetBarkVoice(_barkVoiceList[args.Id].ID);
        };

        BarkVoicePlayButton.OnPressed += _ => PlayPreviewBark();
    }

    private void UpdateBarkVoicesControls()
    {
        if (Profile is null)
            return;

        BarkVoiceButton.Clear();

        var firstVoiceChoiceId = 1;
        for (var i = 0; i < _barkVoiceList.Count; i++)
        {
            var voice = _barkVoiceList[i];

            // Avoid Fluent lookup for `voice.Name` for the same reason as above.
            var name = voice.Name; // Forge-Change: added Forge-Change prefix to the function name
            BarkVoiceButton.AddItem(name, i);

            if (firstVoiceChoiceId == 1)
                firstVoiceChoiceId = i;
        }

        var voiceChoiceId = _barkVoiceList.FindIndex(x => x.ID == Profile.BarkVoice);
        if (!BarkVoiceButton.TrySelectId(voiceChoiceId) &&
            BarkVoiceButton.TrySelectId(firstVoiceChoiceId))
        {
            SetBarkVoice(_barkVoiceList[firstVoiceChoiceId].ID);
        }
    }

    private void PlayPreviewBark()
    {
        if (Profile is null)
            return;

        _entManager.System<BarkSystem>().RequestPreviewBark(Profile.BarkVoice!);
    }
}
