using System.IO;
using Content.Client.Examine;
using Content.Shared._WF.Headshot;
using Content.Shared.GameTicking;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Graphics;
using Robust.Shared.Timing;

namespace Content.Client._WF.Headshot;

/// <summary>Adds the examined character's headshot to the examine tooltip, downloading each image once.</summary>
public sealed partial class HeadshotSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;

    /// <summary>The server's show event comes just before its examine response, so an older one is stale.</summary>
    private static readonly TimeSpan PendingTimeout = TimeSpan.FromSeconds(5);

    private readonly Dictionary<string, Texture> _textures = new();

    /// <summary>Portraits this system created, filled in when their image arrives and cleared on reset.</summary>
    private readonly List<(string Hash, HeadshotPortrait Rect)> _portraits = new();

    private (EntityUid Target, string Hash, TimeSpan Time)? _pending;

    /// <summary>Raised when the server answers a preview request from the profile editor.</summary>
    public event Action<HeadshotPreviewResultEvent>? PreviewReceived;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<HeadshotShowEvent>(OnShow);
        SubscribeNetworkEvent<HeadshotImageEvent>(OnImage);
        SubscribeNetworkEvent<HeadshotPreviewResultEvent>(ev => PreviewReceived?.Invoke(ev));
        SubscribeNetworkEvent<RoundRestartCleanupEvent>(_ => Reset());
        SubscribeLocalEvent<ExamineServerInfoShownEvent>(OnServerInfoShown);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        Reset();
    }

    public void RequestPreview(string url)
    {
        RaiseNetworkEvent(new HeadshotPreviewRequestEvent(url));
    }

    /// <summary>Decodes PNG bytes from the server, or returns null if they are unreadable.</summary>
    public Texture? LoadTexture(byte[] png)
    {
        try
        {
            // Filtered, so the image scales smoothly between thumbnail and full size.
            var parameters = TextureLoadParameters.Default with { SampleParameters = new TextureSampleParameters { Filter = true } };
            return Texture.LoadFromPNGStream(new MemoryStream(png), "headshot", parameters);
        }
        catch (Exception e)
        {
            Log.Warning($"Unreadable headshot image: {e.Message}");
            return null;
        }
    }

    private void OnShow(HeadshotShowEvent ev)
    {
        _pending = (GetEntity(ev.Target), ev.Hash, _timing.RealTime);
        if (!_textures.ContainsKey(ev.Hash))
            RaiseNetworkEvent(new HeadshotRequestEvent(ev.Hash));
    }

    private void OnImage(HeadshotImageEvent ev)
    {
        if (_textures.ContainsKey(ev.Hash) || LoadTexture(ev.Png) is not { } texture)
            return;

        _textures[ev.Hash] = texture;
        _portraits.RemoveAll(p => p.Rect.Disposed);
        foreach (var (hash, rect) in _portraits)
        {
            if (hash == ev.Hash)
                rect.Texture = texture;
        }
    }

    private void OnServerInfoShown(ExamineServerInfoShownEvent ev)
    {
        if (_pending is not { } pending || pending.Target != ev.Target)
            return;

        _pending = null;
        if (_timing.RealTime - pending.Time > PendingTimeout)
            return;

        var rect = new HeadshotPortrait
        {
            HorizontalAlignment = Control.HAlignment.Center,
            Margin = new Thickness(0, 4),
            Texture = _textures.GetValueOrDefault(pending.Hash),
        };

        _portraits.RemoveAll(p => p.Rect.Disposed);
        _portraits.Add((pending.Hash, rect));

        ev.Box.AddChild(rect);
        rect.SetPositionInParent(1);
    }

    private void Reset()
    {
        foreach (var (_, rect) in _portraits)
        {
            rect.Texture = null;
        }

        foreach (var texture in _textures.Values)
        {
            (texture as IDisposable)?.Dispose();
        }

        _portraits.Clear();
        _textures.Clear();
        _pending = null;
    }
}
