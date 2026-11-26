using System.Globalization;
using System.IO;
using Content.Shared._NF.Shipyard;
using Content.Shared._NF.Shipyard.Prototypes;
using YamlDotNet.RepresentationModel;

namespace Content.Shared._WF.MappingMode.Export;

/// <summary>
/// The <c>ship.yml</c> a library version keeps beside its vessel file: the mapper's <see cref="WFMappingShipDetails"/>
/// as YAML text, read back with every field sanitized. No file access.
/// </summary>
public static class WFMappingShipDetailsYaml
{
    /// <summary>Reads details; a missing or malformed value keeps its default, as does unreadable text.</summary>
    public static WFMappingShipDetails Read(string text)
    {
        var details = new WFMappingShipDetails();
        YamlMappingNode? root;
        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(text));
            root = stream.Documents.Count > 0 ? stream.Documents[0].RootNode as YamlMappingNode : null;
        }
        catch (Exception)
        {
            return details;
        }

        if (root == null)
            return details;

        details.Name = Scalar(root, "name") ?? string.Empty;
        details.Description = Scalar(root, "description") ?? string.Empty;
        details.Price = Int(root, "price", 0);
        details.Category = Enum.TryParse<VesselSize>(Scalar(root, "category"), out var size) ? size : VesselSize.All;
        details.Group = Enum.TryParse<ShipyardConsoleUiKey>(Scalar(root, "group"), out var group)
            ? group
            : ShipyardConsoleUiKey.Shipyard;
        details.AutoEngines = Scalar(root, "autoEngines") != "false";
        details.Access = Scalar(root, "access") ?? string.Empty;
        details.RequireCrew = Scalar(root, "requireCrew") == "true";

        foreach (var item in Sequence(root, "class"))
        {
            if (Enum.TryParse<VesselClass>(item, out var vesselClass))
                details.Classes.Add(vesselClass);
        }

        foreach (var item in Sequence(root, "engine"))
        {
            if (Enum.TryParse<VesselEngine>(item, out var engine))
                details.Engines.Add(engine);
        }

        return WFMappingExportLimits.Sanitize(details);
    }

    /// <summary>The details as YAML text.</summary>
    public static string Write(WFMappingShipDetails details)
    {
        var root = new YamlMappingNode();
        root.Add("name", details.Name);
        root.Add("description", details.Description);
        root.Add("price", details.Price.ToString(CultureInfo.InvariantCulture));
        root.Add("category", details.Category.ToString());
        root.Add("group", details.Group.ToString());
        root.Add("autoEngines", details.AutoEngines ? "true" : "false");
        root.Add("access", details.Access);
        root.Add("requireCrew", details.RequireCrew ? "true" : "false");

        var classes = new YamlSequenceNode();
        foreach (var vesselClass in details.Classes)
        {
            classes.Add(vesselClass.ToString());
        }

        root.Add("class", classes);

        var engines = new YamlSequenceNode();
        foreach (var engine in details.Engines)
        {
            engines.Add(engine.ToString());
        }

        root.Add("engine", engines);

        var writer = new StringWriter();
        new YamlStream(new YamlDocument(root)).Save(writer, false);
        return writer.ToString();
    }

    private static int Int(YamlMappingNode node, string key, int fallback)
    {
        return Scalar(node, key) is { } text &&
               int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;
    }

    /// <summary>A mapping's scalar by key; enumerated, since the sandbox doesn't allow its dictionary.</summary>
    private static string? Scalar(YamlMappingNode node, string key)
    {
        foreach (var pair in node)
        {
            if (pair.Key is YamlScalarNode { Value: { } name } && name == key)
                return (pair.Value as YamlScalarNode)?.Value;
        }

        return null;
    }

    /// <summary>The scalar items of a mapping's sequence by key.</summary>
    private static List<string> Sequence(YamlMappingNode node, string key)
    {
        var items = new List<string>();
        foreach (var pair in node)
        {
            if (pair.Key is not YamlScalarNode { Value: { } name } || name != key ||
                pair.Value is not YamlSequenceNode sequence)
            {
                continue;
            }

            foreach (var child in sequence)
            {
                if (child is YamlScalarNode { Value: { } value })
                    items.Add(value);
            }
        }

        return items;
    }
}
