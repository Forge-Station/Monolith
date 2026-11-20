using System.Numerics;
using Content.Shared._WF.Strangers;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;

namespace Content.Client._WF.Strangers;

/// <summary>Shows a stranger's hail and his conversation, and sends back what the player says.</summary>
public sealed class WFStrangerClientSystem : EntitySystem
{
    private readonly Dictionary<NetEntity, DefaultWindow> _hails = new();
    private readonly Dictionary<NetEntity, WFStrangerDialogueWindow> _talks = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<WFStrangerHailEvent>(OnHail);
        SubscribeNetworkEvent<WFStrangerDialogueEvent>(OnDialogue);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        foreach (var window in _hails.Values)
            window.Close();
        foreach (var window in _talks.Values)
            window.Close();
        _hails.Clear();
        _talks.Clear();
    }

    private void OnHail(WFStrangerHailEvent ev)
    {
        if (_hails.Remove(ev.Stranger, out var old))
            old.Close();

        var window = new DefaultWindow
        {
            Title = Loc.GetString("wf-stranger-hail-title", ("name", ev.Name)),
            MinSize = new Vector2(360, 120),
        };
        var body = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 8 };
        body.AddChild(new RichTextLabel { Text = ev.Line });
        var buttons = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal, SeparationOverride = 8 };
        var talk = new Button { Text = Loc.GetString("wf-stranger-hail-talk"), HorizontalExpand = true };
        var ignore = new Button { Text = Loc.GetString("wf-stranger-hail-ignore"), HorizontalExpand = true };
        var answered = false;
        talk.OnPressed += _ =>
        {
            answered = true;
            RaiseNetworkEvent(new WFStrangerHailReplyEvent { Stranger = ev.Stranger, Accept = true });
            window.Close();
        };
        ignore.OnPressed += _ =>
        {
            answered = true;
            RaiseNetworkEvent(new WFStrangerHailReplyEvent { Stranger = ev.Stranger, Accept = false });
            window.Close();
        };
        buttons.AddChild(talk);
        buttons.AddChild(ignore);
        body.AddChild(buttons);
        window.Contents.AddChild(body);
        window.OnClose += () =>
        {
            _hails.Remove(ev.Stranger);
            // Closing the hail without an answer waves him off.
            if (!answered)
                RaiseNetworkEvent(new WFStrangerHailReplyEvent { Stranger = ev.Stranger, Accept = false });
        };
        _hails[ev.Stranger] = window;
        window.OpenCenteredAt(new Vector2(0.5f, 0.3f));
    }

    private void OnDialogue(WFStrangerDialogueEvent ev)
    {
        if (_hails.Remove(ev.Stranger, out var hail))
            hail.Close();

        if (!_talks.TryGetValue(ev.Stranger, out var window))
        {
            if (ev.Ended && ev.Options.Count == 0 && string.IsNullOrEmpty(ev.Line))
                return;
            window = new WFStrangerDialogueWindow();
            window.Chosen += index => RaiseNetworkEvent(new WFStrangerChooseEvent { Stranger = ev.Stranger, Option = index });
            window.OnClose += () => _talks.Remove(ev.Stranger);
            _talks[ev.Stranger] = window;
            window.OpenCenteredAt(new Vector2(0.5f, 0.35f));
        }

        window.Show(ev.Name, ev.Line, ev.Options, ev.Ended);
    }
}

/// <summary>A stranger's conversation: his last line, and the answers the player can give.</summary>
public sealed class WFStrangerDialogueWindow : DefaultWindow
{
    public event Action<int>? Chosen;

    private readonly RichTextLabel _line = new() { VerticalExpand = true };
    private readonly BoxContainer _options = new() { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 4 };

    public WFStrangerDialogueWindow()
    {
        MinSize = new Vector2(420, 220);
        var body = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 10 };
        body.AddChild(_line);
        body.AddChild(_options);
        Contents.AddChild(body);
    }

    public void Show(string name, string line, List<string> options, bool ended)
    {
        if (!string.IsNullOrEmpty(name))
            Title = name;
        if (!string.IsNullOrEmpty(line))
            _line.Text = line;

        _options.RemoveAllChildren();
        for (var i = 0; i < options.Count; i++)
        {
            var index = i;
            var button = new Button { Text = $"{i + 1}. {options[i]}", HorizontalExpand = true };
            button.Label.ClipText = false;
            button.OnPressed += _ => Chosen?.Invoke(index);
            _options.AddChild(button);
        }

        if (ended)
        {
            var leave = new Button { Text = Loc.GetString("wf-stranger-dialogue-close"), HorizontalExpand = true };
            leave.OnPressed += _ => Close();
            _options.AddChild(leave);
        }
    }
}
