using System.IO;
using Content.Client._WF.DoomArcade.Engine;
using Content.Client._WF.GameHub;
using Content.Shared._WF.GameHub;
using Robust.Client.Audio;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.UserInterface;
using Robust.Shared.Configuration;
using Robust.Shared.ContentPack;
using Robust.Shared.Input;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using SixLabors.ImageSharp.PixelFormats;

namespace Content.Client._WF.DoomArcade;

/// <summary>Runs a Doom session while in the UI tree, draws it and takes the keyboard while focused.</summary>
public sealed partial class DoomArcadeScreen : Control
{
    private static readonly ResPath SoundFontPath = new("/Textures/_WF/DoomArcade/TimGM6mb.sf2");

    // Doom's 320x200 was shown on a 4:3 screen, with tall pixels.
    private const float DisplayAspect = 4f / 3f;

    [Dependency] private IClyde _clyde = default!;
    [Dependency] private IInputManager _input = default!;
    [Dependency] private IResourceManager _resources = default!;
    [Dependency] private ILogManager _log = default!;
    [Dependency] private IAudioManager _audio = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IEntitySystemManager _systems = default!;
    [Dependency] private IPrototypeManager _protos = default!;
    [Dependency] private WFGameDataManager _data = default!;

    private readonly Dictionary<Keyboard.Key, DoomKey> _held = new();
    private ISawmill _sawmill = default!;
    private DoomArcadeSession? _session;
    private OwnedTexture? _texture;
    private EntityUid _source;
    private string _game = WFGameHubSystem.DoomGame;

    /// <summary>The player quit from the game's menu.</summary>
    public event Action? Quit;

    /// <summary>The game could not load or crashed.</summary>
    public event Action? Failed;

    public bool Running => _session != null;

    public DoomArcadeScreen()
    {
        IoCManager.InjectDependencies(this);
        _sawmill = _log.GetSawmill("doom.arcade");

        CanKeyboardFocus = true;
        KeyboardFocusOnClick = true;
        MouseFilter = MouseFilterMode.Stop;
        HorizontalExpand = true;
        VerticalExpand = true;
    }

    /// <summary>Starts a game from downloaded data, with its sound heard around <paramref name="source"/>.</summary>
    public void Start(EntityUid source, string game)
    {
        if (_session != null)
            return;

        _source = source;
        _game = game;

        try
        {
            var soundFont = ReadResource(SoundFontPath).ToArray();
            var audio = new DoomArcadeAudio(_audio, _cfg, soundFont, _sawmill);

            _session = new DoomArcadeSession(_protos.Index<WFGamePrototype>(game).DataFile, _data.OpenData(game), audio, _resources.UserData);
            _texture = _clyde.CreateBlankTexture<Rgba32>((_session.Width, _session.Height), "doom-arcade");

            var hub = _systems.GetEntitySystem<WFGameHubSystem>();
            _session.Sound!.NoiseMade += (sound, volume) => hub.SendNoise(_source, _game, sound, volume);
            _session.Music!.TrackChanged += (track, loop) => hub.SendMusic(_source, _game, track, loop);
            _session.LevelCompleted += result => hub.SendLevelComplete(_source, _game, result);

            GrabKeyboardFocus();
        }
        catch (Exception e)
        {
            Fail(e);
        }
    }

    protected override void EnteredTree()
    {
        base.EnteredTree();

        _input.FirstChanceOnKeyEvent += OnKeyEvent;
    }

    protected override void ExitedTree()
    {
        base.ExitedTree();

        _input.FirstChanceOnKeyEvent -= OnKeyEvent;
        Stop();
    }

    protected override void KeyboardFocusExited()
    {
        base.KeyboardFocusExited();

        foreach (var key in _held.Values)
        {
            _session?.SetKey(key, false);
        }

        _held.Clear();
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        if (_session == null || _texture == null)
            return;

        try
        {
            if (_session.Update(args.DeltaSeconds, out var ticked))
            {
                Stop();
                Quit?.Invoke();
                return;
            }

            if (!ticked)
                return;

            _session.Render();
            _texture.SetSubImage<Rgba32>(Vector2i.Zero, (_session.Width, _session.Height), _session.Pixels);
        }
        catch (Exception e)
        {
            Fail(e);
        }
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        handle.DrawRect(PixelSizeBox, Color.Black);

        if (_texture == null)
            return;

        var width = Math.Min(PixelWidth, PixelHeight * DisplayAspect);
        var height = width / DisplayAspect;
        var left = (PixelWidth - width) / 2f;
        var top = (PixelHeight - height) / 2f;
        handle.DrawTextureRect(_texture, new UIBox2(left, top, left + width, top + height));
    }

    private MemoryStream ReadResource(ResPath path)
    {
        var memory = new MemoryStream();
        using (var file = _resources.ContentFileRead(path))
        {
            file.CopyTo(memory);
        }

        memory.Position = 0;
        return memory;
    }

    private void Stop()
    {
        if (_session != null)
            _systems.GetEntitySystem<WFGameHubSystem>().SendStopped(_source, _game);

        _held.Clear();
        _session?.Dispose();
        _session = null;
        _texture?.Dispose();
        _texture = null;
    }

    private void Fail(Exception e)
    {
        _sawmill.Error($"Doom stopped: {e}");
        Stop();
        Failed?.Invoke();
    }

    private void OnKeyEvent(KeyEventArgs args, KeyEventType type)
    {
        if (_session == null)
            return;

        if (type == KeyEventType.Up)
        {
            if (!_held.Remove(args.Key, out var released))
                return;

            _session.SetKey(released, false);
            args.Handle();
            return;
        }

        if (!HasKeyboardFocus())
            return;

        var key = DoomArcadeKeys.ToDoom(args.Key);
        if (key == DoomKey.Unknown)
            return;

        args.Handle();

        if (type == KeyEventType.Repeat || !_held.TryAdd(args.Key, key))
            return;

        _session.SetKey(key, true);
    }
}
