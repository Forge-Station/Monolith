using Content.Shared._WF.Companies;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Graphics;
using System.IO;
using Robust.Client.ResourceManagement;
using Robust.Shared.ContentPack;
using Robust.Shared.Utility;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Content.Client._WF.Companies;

/// <summary>Receives company snapshots and logos and sends Company window requests.</summary>
public sealed class WFCompanyClientSystem : EntitySystem
{
    [Dependency] private IClyde _clyde = default!;
    [Dependency] private IResourceManager _resources = default!;
    [Dependency] private SpriteSystem _sprites = default!;
    [Dependency] private IResourceCache _cache = default!;

    /// <summary>The latest snapshot for this connection.</summary>
    public WFCompanyState? State { get; private set; }

    public event Action<WFCompanyState>? Received;

    /// <summary>Raised when a logo arrives, so open windows can redraw.</summary>
    public event Action? LogosChanged;

    /// <summary>Raised with a company profile the player asked to see.</summary>
    public event Action<WFCompanyProfileEvent>? ProfileReceived;

    private WFCompanyWindow? _window;
    private readonly Dictionary<int, (int Version, Texture? Texture, byte[]? Pixels)> _logos = new();
    private readonly HashSet<(int, int)> _requested = new();
    private readonly Dictionary<int, (int Version, ResPath? Path, byte[]? Pixels)> _rankIcons = new();
    private readonly HashSet<int> _requestedIcons = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<WFCompanyState>(OnState);
        SubscribeNetworkEvent<WFCompanyLogoEvent>(OnLogo);
        SubscribeNetworkEvent<WFCompanyRankIconEvent>(OnRankIcon);
        SubscribeNetworkEvent<WFCompanyProfileEvent>(args => ProfileReceived?.Invoke(args));
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _window?.Close();
    }

    private void OnState(WFCompanyState state)
    {
        State = state;
        Received?.Invoke(state);
    }

    private void OnLogo(WFCompanyLogoEvent args)
    {
        Texture? texture = null;
        if (args.Logo is { Length: WFCompanyPolicy.LogoBytes } pixels)
        {
            using var image = new Image<Rgba32>(WFCompanyPolicy.LogoSize, WFCompanyPolicy.LogoSize);
            for (var i = 0; i < WFCompanyPolicy.LogoSize * WFCompanyPolicy.LogoSize; i++)
                image[i % WFCompanyPolicy.LogoSize, i / WFCompanyPolicy.LogoSize] = new Rgba32(pixels[i * 4], pixels[i * 4 + 1], pixels[i * 4 + 2], pixels[i * 4 + 3]);
            // Nearest sampling keeps the pixel art crisp when scaled up; the pixels are sRGB like every other sprite.
            texture = _clyde.LoadTextureFromImage(image, $"wf-company-logo-{args.CompanyId}",
                new TextureLoadParameters { SampleParameters = new TextureSampleParameters { Filter = false }, Srgb = true });
        }
        _logos[args.CompanyId] = (args.Version, texture, args.Logo);
        LogosChanged?.Invoke();
    }

    private void OnRankIcon(WFCompanyRankIconEvent args)
    {
        ResPath? path = null;
        if (args.Icon is { Length: WFCompanyPolicy.IconBytes } pixels)
        {
            using var image = new Image<Rgba32>(WFCompanyPolicy.IconSize, WFCompanyPolicy.IconSize);
            for (var i = 0; i < WFCompanyPolicy.IconSize * WFCompanyPolicy.IconSize; i++)
                image[i % WFCompanyPolicy.IconSize, i / WFCompanyPolicy.IconSize] = new Rgba32(pixels[i * 4], pixels[i * 4 + 1], pixels[i * 4 + 2], pixels[i * 4 + 3]);
            using var stream = new MemoryStream();
            image.SaveAsPng(stream);
            path = WFCompanyIconResources.For(_resources).Store($"rank{args.RankId}-{args.Version}", stream.ToArray());
        }
        _rankIcons[args.RankId] = (args.Version, path, args.Icon);
        LogosChanged?.Invoke();
    }

    /// <summary>The path of a rank's job icon, asking the server once if it isn't known; null when it has none.</summary>
    public ResPath? GetRankIconPath(int rankId)
    {
        if (_rankIcons.TryGetValue(rankId, out var known))
            return known.Path;
        if (_requestedIcons.Add(rankId))
            Send(new WFCompanyRequest(WFCompanyAction.RequestRankIcon) { RankId = rankId });
        return null;
    }

    /// <summary>A rank's job icon as a texture for the window, fetched like <see cref="GetRankIconPath"/>.</summary>
    public Texture? GetRankIcon(int rankId, int version)
    {
        if (version == 0)
            return null;
        if (_rankIcons.TryGetValue(rankId, out var known) && known.Version < version)
        {
            _rankIcons.Remove(rankId);
            _requestedIcons.Remove(rankId);
        }
        return GetRankIconPath(rankId) is { } path ? _cache.GetResource<TextureResource>(path).Texture : null;
    }

    /// <summary>A rank's current job icon, fetched once and kept current by change broadcasts; null when it has none.</summary>
    public Texture? GetRankIconLatest(int rankId) =>
        GetRankIconPath(rankId) is { } path ? _cache.GetResource<TextureResource>(path).Texture : null;

    /// <summary>A rank icon's pixels, if they have arrived; for the editor.</summary>
    public byte[]? GetRankIconPixels(int rankId) => _rankIcons.TryGetValue(rankId, out var known) ? known.Pixels : null;

    /// <summary>The company's logo at this version, asking the server for it once if it isn't here yet.</summary>
    public Texture? GetLogo(int companyId, int version)
    {
        if (version == 0)
            return null;
        if (_logos.TryGetValue(companyId, out var known) && known.Version == version)
            return known.Texture;
        if (_requested.Add((companyId, version)))
            Send(new WFCompanyRequest(WFCompanyAction.RequestLogo) { CompanyId = companyId });
        return null;
    }

    /// <summary>The logo's pixels at this version, if they have arrived; for the editor.</summary>
    public byte[]? GetLogoPixels(int companyId, int version) =>
        _logos.TryGetValue(companyId, out var known) && known.Version == version ? known.Pixels : null;

    public void Send(WFCompanyRequest request) => RaiseNetworkEvent(request);

    /// <summary>A job icon's top-left icon-sized corner as raw RGBA, or null if it's too small. Read from the loaded texture:
    /// packaged clients pack RSIs into one file and packaged servers don't have textures at all.</summary>
    public byte[]? ReadIconPreset(SpriteSpecifier icon)
    {
        var texture = _sprites.Frame0(icon);
        var size = WFCompanyPolicy.IconSize;
        if (texture.Width < size || texture.Height < size)
            return null;
        var pixels = new byte[WFCompanyPolicy.IconBytes];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var pixel = texture.GetPixel(x, y);
                var i = (y * size + x) * 4;
                pixels[i] = pixel.RByte;
                pixels[i + 1] = pixel.GByte;
                pixels[i + 2] = pixel.BByte;
                pixels[i + 3] = pixel.AByte;
            }
        }
        return pixels;
    }

    /// <summary>Opens the Company window, or brings it forward.</summary>
    /// <param name="create">Opens on the Create tab if the player's character can use it.</param>
    public void OpenWindow(bool create = false)
    {
        if (_window is { IsOpen: true })
        {
            if (create)
                _window.ShowCreate();
            _window.MoveToFront();
            return;
        }
        var window = new WFCompanyWindow();
        if (create)
            window.ShowCreate();
        _window = window;
        window.OnClose += () =>
        {
            if (_window == window)
                _window = null;
        };
        window.OpenCentered();
    }
}
