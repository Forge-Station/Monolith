using System.Linq;
using System.Text;
using Content.Shared._NF.Shipyard.Prototypes;

namespace Content.Shared._WF.MappingMode.Export;

/// <summary>
/// The pure naming and pricing rules of an exported ship, shared so the wizard previews what the server generates:
/// the vessel id and file name from a name, the size from the tile count and the price from the appraisal.
/// </summary>
public static class WFMappingShipNaming
{
    public const string IdPrefix = "WF";

    /// <summary>The resource folder the grid file goes in, as <c>shuttlePath</c> names it.</summary>
    public const string ShuttleFolder = "/SharedMaps/_WF/Community";

    /// <summary>Tile counts below which a ship is Micro, Small and Medium; anything bigger is Large.</summary>
    public const int MicroTiles = 50;
    public const int SmallTiles = 200;
    public const int MediumTiles = 500;

    /// <summary>The appraisal is raised by this before rounding, so the price stays over a sale of the ship.</summary>
    public const double PriceMarkup = 1.25;

    public const int MinPrice = 1000;

    /// <summary><c>WF</c> and the name's words in PascalCase: "my cool-ship 2" gives <c>WFMyCoolShip2</c>.</summary>
    public static string VesselId(string name)
    {
        var builder = new StringBuilder(IdPrefix);
        foreach (var word in Words(name))
        {
            builder.Append(char.ToUpperInvariant(word[0]));
            builder.Append(word, 1, word.Length - 1);
        }

        return builder.Length > IdPrefix.Length ? builder.ToString() : IdPrefix + "Ship";
    }

    /// <summary>The grid and prototype file name, without <c>.yml</c>: the name's words in lower case joined by '_'.</summary>
    public static string FileName(string name)
    {
        var file = string.Join('_', Words(name).Select(word => word.ToLowerInvariant()));
        return file.Length > 0 ? file : "ship";
    }

    /// <summary>The shuttle path the prototype points at.</summary>
    public static string ShuttlePath(string name)
    {
        return $"{ShuttleFolder}/{FileName(name)}.yml";
    }

    /// <summary>The letter and digit runs of a name.</summary>
    private static List<string> Words(string name)
    {
        var words = new List<string>();
        var builder = new StringBuilder();
        foreach (var c in name)
        {
            if (c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                builder.Append(c);
                continue;
            }

            if (builder.Length > 0)
                words.Add(builder.ToString());

            builder.Clear();
        }

        if (builder.Length > 0)
            words.Add(builder.ToString());

        return words;
    }

    public static VesselSize SizeFor(int tiles)
    {
        return tiles switch
        {
            < MicroTiles => VesselSize.Micro,
            < SmallTiles => VesselSize.Small,
            < MediumTiles => VesselSize.Medium,
            _ => VesselSize.Large,
        };
    }

    /// <summary>
    /// The appraisal raised by <see cref="PriceMarkup"/> and rounded up: to 500 below 20,000, 1,000 below 100,000 and
    /// 5,000 above; never under <see cref="MinPrice"/>.
    /// </summary>
    public static int PriceFor(double appraisal)
    {
        var raised = double.IsFinite(appraisal) && appraisal > 0 ? appraisal * PriceMarkup : 0;
        var step = raised switch
        {
            < 20_000 => 500,
            < 100_000 => 1_000,
            _ => 5_000,
        };

        var price = Math.Ceiling(raised / step) * step;
        return (int) Math.Clamp(price, MinPrice, int.MaxValue / 2);
    }

    /// <summary>The lowest price the shipyard test accepts for an appraisal: the appraisal itself, rounded up.</summary>
    public static int PriceFloor(double appraisal)
    {
        return double.IsFinite(appraisal) && appraisal > 0 ? (int) Math.Ceiling(appraisal) : 0;
    }
}
