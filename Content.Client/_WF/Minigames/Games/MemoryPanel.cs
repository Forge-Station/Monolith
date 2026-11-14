using System.Numerics;
using Content.Shared._WF.Minigames;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;

namespace Content.Client._WF.Minigames.Games;

/// <summary>Memory: four coloured pads that play the sequence back, then take the player's presses.</summary>
public sealed class MemoryPanel : MinigamePanel
{
    private static readonly Color[] PadColors =
    {
        Color.FromHex("#B83A3A"),
        Color.FromHex("#3A7AB8"),
        Color.FromHex("#3AB85A"),
        Color.FromHex("#C8B23A"),
    };

    private readonly Label _progress = new() { HorizontalAlignment = HAlignment.Center };
    private readonly List<Button> _pads = new();
    private readonly List<int> _sequence = new();

    private int _shownId = -1;
    private float _flash = 0.5f;
    private bool _playing;

    /// <summary>Time into the playback; each pad gets a flash then a short gap.</summary>
    private float _clock;

    public MemoryPanel()
    {
        AddChild(new Label { Text = Loc.GetString("wf-minigame-memory-rules"), HorizontalAlignment = HAlignment.Center });
        AddChild(_progress);

        var grid = new GridContainer { Columns = 2, HorizontalAlignment = HAlignment.Center };
        for (var i = 0; i < MinigameRules.MemoryPads; i++)
        {
            var pad = i;
            var button = MakeButton(string.Empty, () => Move(pad));
            button.MinSize = new Vector2(96, 72);
            button.ModulateSelfOverride = Dim(pad);
            _pads.Add(button);
            grid.AddChild(button);
        }

        AddChild(grid);
    }

    private static Color Dim(int pad) => PadColors[pad].WithAlpha(0.45f);

    public override void UpdateState(MinigameState state)
    {
        if (state.View is not MemoryView view)
            return;

        _flash = view.Flash;
        _progress.Text = Loc.GetString("wf-minigame-memory-progress",
            ("entered", view.Entered), ("length", view.Sequence.Count), ("target", view.TargetLength));

        if (view.ShowId != _shownId)
        {
            _shownId = view.ShowId;
            _sequence.Clear();
            _sequence.AddRange(view.Sequence);
            _playing = true;
            _clock = -0.6f;
        }

        SetPadsEnabled(state.Status == MinigameStatus.Playing && !_playing);
        if (state.Status != MinigameStatus.Playing)
            _playing = false;
    }

    private void SetPadsEnabled(bool enabled)
    {
        foreach (var pad in _pads)
            pad.Disabled = !enabled;
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        if (!_playing)
            return;

        _clock += args.DeltaSeconds;
        var step = _flash * 1.5f;
        var index = _clock < 0 ? -1 : (int) (_clock / step);
        var lit = index >= 0 && index < _sequence.Count && _clock - index * step < _flash ? _sequence[index] : -1;

        for (var i = 0; i < _pads.Count; i++)
            _pads[i].ModulateSelfOverride = i == lit ? PadColors[i] : Dim(i);

        if (index >= _sequence.Count)
        {
            _playing = false;
            SetPadsEnabled(true);
        }
    }
}
