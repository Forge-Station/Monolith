using Robust.Shared.Serialization;

namespace Content.Shared._WF.Headshot;

/// <summary>Server to examiner: the examined entity's headshot can be shown. Sent just before the examine response.</summary>
[Serializable, NetSerializable]
public sealed class HeadshotShowEvent(NetEntity target, string hash) : EntityEventArgs
{
    public readonly NetEntity Target = target;
    public readonly string Hash = hash;
}

/// <summary>Client to server: send the image for a hash the server has shown this client.</summary>
[Serializable, NetSerializable]
public sealed class HeadshotRequestEvent(string hash) : EntityEventArgs
{
    public readonly string Hash = hash;
}

/// <summary>Server to client: a headshot as PNG bytes.</summary>
[Serializable, NetSerializable]
public sealed class HeadshotImageEvent(string hash, byte[] png) : EntityEventArgs
{
    public readonly string Hash = hash;
    public readonly byte[] Png = png;
}

/// <summary>Client to server, from the profile editor: fetch this URL and send back how it will look.</summary>
[Serializable, NetSerializable]
public sealed class HeadshotPreviewRequestEvent(string url) : EntityEventArgs
{
    public readonly string Url = url;
}

/// <summary>Server to client: the preview image, or a loc id saying why there is none.</summary>
[Serializable, NetSerializable]
public sealed class HeadshotPreviewResultEvent(string url, byte[]? png, string? error) : EntityEventArgs
{
    public readonly string Url = url;
    public readonly byte[]? Png = png;
    public readonly string? Error = error;
}
