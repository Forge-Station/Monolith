using System.IO;
using Content.Client._WF.DoomArcade.Engine;
using Robust.Client.Audio;
using Robust.Shared.Configuration;
using Robust.Shared.ContentPack;
using Robust.Shared.Utility;
using SixLabors.ImageSharp.PixelFormats;

namespace Content.Client._WF.DoomArcade;

/// <summary>One running game: steps the engine at its fixed tic rate and renders frames. Needs no graphics device.</summary>
public sealed class DoomArcadeSession : IDisposable
{
    private static readonly ResPath SavesFolder = new("/wolfgate_games");

    private const float TicLength = 1f / 35f;
    private const int TicsPerSecond = 35;

    // Stops a long client hitch from turning into a burst of game time.
    private const int MaxTicsPerUpdate = 4;

    private readonly GameContent _content;
    private readonly DoomArcadeVideo _video;
    private readonly DoomArcadeInput _input;
    private readonly Doom _doom;
    private readonly DoomArcadeSound? _sound;
    private readonly DoomArcadeMusic? _music;
    private float _accumulator;
    private bool _reportedLevel;

    public int Width => _video.Width;
    public int Height => _video.Height;
    public Rgba32[] Pixels => _video.Pixels;
    public Doom Doom => _doom;
    public DoomArcadeSound? Sound => _sound;
    public DoomArcadeMusic? Music => _music;
    public GameContent Content => _content;

    /// <summary>The player finished a level of a real game, not a title-screen demo.</summary>
    public event Action<DoomLevelResult>? LevelCompleted;

    /// <param name="wadName">The IWAD's file name, which tells the engine which game it is.</param>
    /// <param name="wad">A seekable stream of the IWAD; the session owns it.</param>
    /// <param name="audio">Where to play sound and music; null runs silently.</param>
    /// <param name="saves">Where save games live; null keeps none.</param>
    /// <param name="args">Engine command line, such as <c>-warp 1 1</c>.</param>
    public DoomArcadeSession(string wadName, Stream wad, DoomArcadeAudio? audio, IWritableDirProvider? saves, params string[] args)
    {
        var commandLine = new CommandLineArgs(args);
        var config = new Config { video_highresolution = false };

        _content = new GameContent(commandLine, wadName, wad);
        _video = new DoomArcadeVideo(config, _content);
        _input = new DoomArcadeInput(config);

        if (audio != null)
        {
            _sound = new DoomArcadeSound(config, _content, audio.Manager, audio.Config);
            _music = new DoomArcadeMusic(config, _content, audio.SoundFont, audio.Manager, audio.Config, audio.Sawmill);
        }

        _doom = new Doom(commandLine, config, _content, _video, _sound, _music, _input);
        _doom.Options.Saves = new SaveFiles(saves!, SavesFolder / Engine.Path.GetFileNameWithoutExtension(wadName));
    }

    /// <summary>Advances the game by real time.</summary>
    /// <param name="ticked">True if the game moved, so a new frame is worth rendering.</param>
    /// <returns>True once the player has quit.</returns>
    public bool Update(float deltaSeconds, out bool ticked)
    {
        ticked = false;
        _accumulator = Math.Min(_accumulator + deltaSeconds, MaxTicsPerUpdate * TicLength);
        _music?.Update();

        while (_accumulator >= TicLength)
        {
            _accumulator -= TicLength;
            ticked = true;

            if (_doom.Update() == UpdateResult.Completed)
                return true;

            CheckLevelCompleted();
        }

        return false;
    }

    public void Render() => _video.Render(_doom, Fixed.One);

    public void SetKey(DoomKey key, bool down)
    {
        _input.SetHeld(key, down);
        _doom.PostEvent(new DoomEvent(down ? EventType.KeyDown : EventType.KeyUp, key));
    }

    public void Dispose()
    {
        _music?.Dispose();
        _sound?.Dispose();
        _content.Dispose();
    }

    private void CheckLevelCompleted()
    {
        var finished = _doom.State == DoomState.Game && _doom.Game.State == GameState.Intermission;
        if (!finished)
        {
            _reportedLevel = false;
            return;
        }

        if (_reportedLevel)
            return;

        _reportedLevel = true;

        var options = _doom.Options;
        var info = options.IntermissionInfo;
        var player = info.Players[options.ConsolePlayer];
        LevelCompleted?.Invoke(new DoomLevelResult(
            options.Episode * 10 + options.Map,
            Percent(player.KillCount, info.MaxKillCount),
            Percent(player.ItemCount, info.MaxItemCount),
            Percent(player.SecretCount, info.MaxSecretCount),
            player.Time / TicsPerSecond));
    }

    private static int Percent(int count, int max) => max <= 0 ? 100 : Math.Clamp(count * 100 / max, 0, 100);
}

/// <summary>What a session needs to make noise.</summary>
public sealed record DoomArcadeAudio(IAudioManager Manager, IConfigurationManager Config, byte[] SoundFont, ISawmill Sawmill);

/// <summary>A finished level. <paramref name="Level"/> is episode * 10 + map, so E1M1 is 11.</summary>
public readonly record struct DoomLevelResult(int Level, int KillPercent, int ItemPercent, int SecretPercent, int Seconds);
