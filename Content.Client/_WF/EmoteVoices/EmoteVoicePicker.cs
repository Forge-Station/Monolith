using Content.Shared._WF.EmoteVoices;
using Content.Shared.Chat.Prototypes;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;

namespace Content.Client._WF.EmoteVoices;

/// <summary>Creator card with a dropdown and a play button for the message sound, the scream and the laugh.</summary>
public sealed partial class EmoteVoicePicker : PanelContainer
{
    [Dependency] private IPrototypeManager _proto = default!;

    /// <summary>A voice was picked for an emote; null is the species default.</summary>
    public event Action<ProtoId<EmotePrototype>, ProtoId<EmoteVoicePrototype>?>? OnVoiceSelected;

    /// <summary>The play button was pressed for an emote.</summary>
    public event Action<ProtoId<EmotePrototype>>? OnPlayPressed;

    /// <summary>A message sound was picked; null is the species default.</summary>
    public event Action<ProtoId<MessageSoundPrototype>?>? OnMessageSoundSelected;

    /// <summary>The message sound's play button was pressed.</summary>
    public event Action? OnMessageSoundPlayPressed;

    private readonly Dictionary<ProtoId<EmotePrototype>, (OptionButton Button, List<ProtoId<EmoteVoicePrototype>?> Voices)> _rows = new();

    private OptionButton _messageButton = default!;
    private readonly List<ProtoId<MessageSoundPrototype>?> _messageSounds = new() { null };

    public EmoteVoicePicker()
    {
        IoCManager.InjectDependencies(this);
        StyleClasses.Add("CreatorCard");
        HorizontalExpand = true;

        var box = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 6,
        };
        AddChild(box);

        box.AddChild(new Label
        {
            Text = Loc.GetString("emote-voice-heading"),
            StyleClasses = { "CreatorHeading" },
        });

        AddMessageRow(box);
        AddRow(box, EmoteVoiceRules.Scream, "emote-voice-scream");
        AddRow(box, EmoteVoiceRules.Laugh, "emote-voice-laugh");
    }

    private void AddMessageRow(BoxContainer box)
    {
        var row = NewRow("message-sound-label");

        // Item ids index into _messageSounds; id 0 is the species default.
        _messageButton = new OptionButton { MinWidth = 220 };
        _messageButton.AddItem(Loc.GetString("emote-voice-species-default"), 0);
        foreach (var sound in MessageSoundRules.All(_proto))
        {
            _messageButton.AddItem(Loc.GetString(sound.Name), _messageSounds.Count);
            _messageSounds.Add(sound.ID);
        }

        _messageButton.OnItemSelected += args =>
        {
            _messageButton.SelectId(args.Id);
            OnMessageSoundSelected?.Invoke(_messageSounds[args.Id]);
        };
        row.AddChild(_messageButton);
        row.AddChild(NewPlayButton(() => OnMessageSoundPlayPressed?.Invoke()));
        box.AddChild(row);
    }

    private static BoxContainer NewRow(LocId label)
    {
        var row = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            SeparationOverride = 6,
        };

        row.AddChild(new Label
        {
            Text = Loc.GetString(label),
            VAlign = Label.VAlignMode.Center,
            StyleClasses = { "CreatorFieldLabel" },
        });
        row.AddChild(new Control { HorizontalExpand = true });
        return row;
    }

    private static Button NewPlayButton(Action onPressed)
    {
        var play = new Button
        {
            Text = Loc.GetString("emote-voice-play"),
            ToolTip = Loc.GetString("emote-voice-play-tooltip"),
        };
        play.OnPressed += _ => onPressed();
        return play;
    }

    private void AddRow(BoxContainer box, ProtoId<EmotePrototype> emote, LocId label)
    {
        var row = NewRow(label);

        // Item ids index into voices; id 0 is the species default.
        var voices = new List<ProtoId<EmoteVoicePrototype>?> { null };
        var button = new OptionButton { MinWidth = 220 };
        button.AddItem(Loc.GetString("emote-voice-species-default"), 0);
        foreach (var voice in EmoteVoiceRules.VoicesFor(emote, _proto))
        {
            button.AddItem(Loc.GetString(voice.Name), voices.Count);
            voices.Add(voice.ID);
        }

        button.OnItemSelected += args =>
        {
            button.SelectId(args.Id);
            OnVoiceSelected?.Invoke(emote, voices[args.Id]);
        };
        row.AddChild(button);
        row.AddChild(NewPlayButton(() => OnPlayPressed?.Invoke(emote)));

        box.AddChild(row);
        _rows[emote] = (button, voices);
    }

    /// <summary>Selects a profile's voice for an emote; null or an unknown voice shows the species default.</summary>
    public void SetVoice(ProtoId<EmotePrototype> emote, ProtoId<EmoteVoicePrototype>? voice)
    {
        if (!_rows.TryGetValue(emote, out var row))
            return;

        row.Button.SelectId(Math.Max(0, row.Voices.IndexOf(voice)));
    }

    /// <summary>Selects a profile's message sound; null or an unknown one shows the species default.</summary>
    public void SetMessageSound(ProtoId<MessageSoundPrototype>? sound)
    {
        _messageButton.SelectId(Math.Max(0, _messageSounds.IndexOf(sound)));
    }
}
