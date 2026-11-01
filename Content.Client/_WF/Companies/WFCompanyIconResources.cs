using System.Diagnostics.CodeAnalysis;
using System.IO;
using Robust.Shared.ContentPack;
using Robust.Shared.Utility;

namespace Content.Client._WF.Companies;

/// <summary>A content root holding fetched rank icons as PNGs, so sprite code such as the HUD can draw them by path.</summary>
/// <remarks>Roots can't be removed from a resource manager, so this mounts once per manager and outlives the entity systems.</remarks>
public sealed class WFCompanyIconResources : IContentRoot, IDisposable
{
    /// <summary>Where the root is mounted; nothing ships under it.</summary>
    public static readonly ResPath Prefix = ResPath.Root / "WFCompanyIcons";

    private static readonly List<WeakReference<WFCompanyIconResources>> Mounted = new();

    private readonly WeakReference<IResourceManager> _owner;
    private readonly MemoryContentRoot _root = new();

    private WFCompanyIconResources(IResourceManager resources)
    {
        _owner = new WeakReference<IResourceManager>(resources);
        resources.AddRoot(Prefix, this);
    }

    /// <summary>The root for this resource manager, mounting it on first use.</summary>
    public static WFCompanyIconResources For(IResourceManager resources)
    {
        lock (Mounted)
        {
            for (var i = Mounted.Count - 1; i >= 0; i--)
            {
                if (!Mounted[i].TryGetTarget(out var existing) || !existing._owner.TryGetTarget(out var owner))
                {
                    Mounted.RemoveAt(i);
                    continue;
                }
                if (ReferenceEquals(owner, resources))
                    return existing;
            }
            var created = new WFCompanyIconResources(resources);
            Mounted.Add(new WeakReference<WFCompanyIconResources>(created));
            return created;
        }
    }

    /// <summary>Stores an icon under a name unique to its version and returns its full path.</summary>
    public ResPath Store(string name, byte[] png)
    {
        var relative = new ResPath($"{name}.png");
        _root.AddOrUpdateFile(relative, png);
        return Prefix / relative;
    }

    void IContentRoot.Mount() => _root.Mount();
    bool IContentRoot.TryGetFile(ResPath path, [NotNullWhen(true)] out Stream? stream) => _root.TryGetFile(path, out stream);
    bool IContentRoot.FileExists(ResPath path) => _root.FileExists(path);
    IEnumerable<ResPath> IContentRoot.FindFiles(ResPath path) => _root.FindFiles(path);
    IEnumerable<string> IContentRoot.GetRelativeFilePaths() => _root.GetRelativeFilePaths();
    public void Dispose() => _root.Dispose();
}
