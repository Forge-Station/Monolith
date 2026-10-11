using System.Linq;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using static Content.Client._WF.CombatConsole.WFInstrumentTheme;

namespace Content.Client._WF.ShipPa.UI;

public sealed partial class ShipAlarmPanel
{
    private bool _wfInstrumentsRefitted;

    /// <summary>Rehouses the live PA controls into a compact bank without replacing their handlers.</summary>
    public void WfRefitInstruments()
    {
        if (_wfInstrumentsRefitted)
            return;
        _wfInstrumentsRefitted = true;

        var conditionCaption = ConditionLabel.Parent!.Children.OfType<Label>().Single(label => label != ConditionLabel);
        foreach (var label in new[] { conditionCaption, ConditionLabel, SpeakersCaption, SpeakersLabel })
        {
            label.Margin = new Thickness(0);
            label.MinHeight = 24;
            label.ClipText = true;
        }
        conditionCaption.ClipText = false;
        ConditionLabel.HorizontalExpand = true;
        SpeakersCaption.HorizontalExpand = SpeakersLabel.HorizontalExpand = true;
        SpeakersLabel.Align = Robust.Client.UserInterface.Controls.Label.AlignMode.Left;
        var readouts = Column(Row(conditionCaption, ConditionLabel), Column(SpeakersCaption, SpeakersLabel));
        readouts.SeparationOverride = 2;
        ((BoxContainer) SpeakersLabel.Parent!).SeparationOverride = 0;

        var codes = CodeContainer.Children.OfType<Button>().ToArray();
        foreach (var button in codes)
        {
            Detach(button);
            WfCompactAlarmButton(button);
        }
        CodeContainer.DisposeAllChildren();
        CodeContainer.Margin = new Thickness(0);
        CodeContainer.SeparationOverride = 4;
        for (var i = 0; i < codes.Length; i += 2)
        {
            var row = i + 1 < codes.Length ? Row(codes[i], codes[i + 1]) : Row(codes[i]);
            row.SeparationOverride = 4;
            CodeContainer.AddChild(row);
        }
        WfCompactAlarmButton(GeneralQuartersButton);
        WfCompactAlarmButton(CollisionAlertButton);
        WfCompactAlarmButton(AnnounceButton);
        WfCompactAlarmButton(SoundButton);
        WfCompactAlarmButton(SoundStopButton);
        AnnounceButton.HorizontalExpand = SoundButton.HorizontalExpand = SoundStopButton.HorizontalExpand = false;
        AnnounceButton.SetWidth = 84;
        SoundButton.SetWidth = 56;
        SoundStopButton.SetWidth = 56;
        foreach (var edit in new[] { AnnounceEdit, SoundEdit })
        {
            edit.Margin = new Thickness(0);
            edit.HorizontalExpand = true;
            edit.MinWidth = 100;
            edit.MinHeight = edit.SetHeight = 32;
        }
        var announce = Row(AnnounceEdit, AnnounceButton);
        var sound = Row(SoundEdit, SoundButton, SoundStopButton);
        announce.SeparationOverride = sound.SeparationOverride = 4;
        WfRefitPlaylist();
        var body = Column(readouts, CodeContainer, GeneralQuartersButton, CollisionAlertButton, announce, sound, PlaylistBox);
        body.SeparationOverride = 6;
        DisposeAllChildren();
        Margin = new Thickness(0);
        HorizontalExpand = true;
        VerticalExpand = false;
        AddChild(body);
        ApplyCodeColours();
    }

    /// <summary>Compacts the now-playing line and queue; the section stays hidden until the ship has something to list.</summary>
    private void WfRefitPlaylist()
    {
        foreach (var label in new[] { NowPlayingLabel, QueueLabel })
        {
            label.Margin = new Thickness(0);
            label.MinHeight = 24;
            label.ClipText = true;
            label.VerticalAlignment = VAlignment.Center;
        }
        WfCompactAlarmButton(SkipButton);
        WfCompactAlarmButton(ClearQueueButton);
        SkipButton.HorizontalExpand = ClearQueueButton.HorizontalExpand = false;
        SkipButton.SetWidth = 56;
        ClearQueueButton.SetWidth = 64;
        ((BoxContainer) NowPlayingLabel.Parent!).SeparationOverride = 4;
        QueueHeader.SeparationOverride = 4;
        QueueHeader.Margin = new Thickness(0);
        QueueList.SeparationOverride = 2;
        // Three rows show at once; the rest scroll.
        QueueScroll.MaxHeight = 3 * 32 + 2 * 2;
        PlaylistBox.Margin = new Thickness(0);
        PlaylistBox.SeparationOverride = 4;
    }

    /// <summary>Styles a queue row built after the refit, which the console theme has already passed over.</summary>
    private static void WfStyleQueueRow(BoxContainer row, Button remove)
    {
        row.SeparationOverride = 4;
        remove.AddStyleClass("WfCompact");
        Apply(row);
        remove.Margin = new Thickness(0);
        remove.HorizontalExpand = false;
        remove.MinHeight = remove.SetHeight = 32;
        remove.SetWidth = 32;
    }

    private static void WfCompactAlarmButton(Button button)
    {
        button.AddStyleClass("WfCompact");
        button.Margin = new Thickness(0);
        button.MinWidth = 0;
        button.MinHeight = button.SetHeight = 32;
        button.HorizontalExpand = true;
        Switch(button);
    }
}
