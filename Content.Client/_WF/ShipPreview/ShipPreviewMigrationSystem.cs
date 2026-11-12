using System.IO;
using System.Linq;
using Robust.Shared.ContentPack;
using Robust.Shared.Map.Events;
using Robust.Shared.Serialization.Markdown;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Serialization.Markdown.Value;
using Robust.Shared.Utility;

namespace Content.Client._WF.ShipPreview;

/// <summary>
/// Applies the server's prototype migrations when loading client-side ship previews.
/// </summary>
public sealed partial class ShipPreviewMigrationSystem : EntitySystem
{
    [Dependency] private IResourceManager _resources = default!;

    // Keep in sync with Content.Server.Maps.MapMigrationSystem.
    private static readonly string[] MigrationFiles =
    {
        "/migration.yml",
        "/nf_migration.yml",
        "/mono_migration.yml",
        "/forge_migration.yml",
    };

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<BeforeEntityReadEvent>(OnBeforeRead);
    }

    private void OnBeforeRead(BeforeEntityReadEvent ev)
    {
        foreach (var file in MigrationFiles)
        {
            if (!_resources.TryContentFileRead(new ResPath(file), out var stream))
                continue;

            using var reader = new StreamReader(stream, EncodingHelpers.UTF8);
            if (DataNodeParser.ParseYamlStream(reader).FirstOrDefault()?.Root is not MappingDataNode mapping)
                continue;

            foreach (var (key, value) in mapping)
            {
                if (value is not ValueDataNode node)
                    continue;

                if (string.IsNullOrWhiteSpace(node.Value) || node.Value == "null")
                    ev.DeletedPrototypes.Add(key);
                else
                    ev.RenamedPrototypes.Add(key, node.Value);
            }
        }
    }
}
