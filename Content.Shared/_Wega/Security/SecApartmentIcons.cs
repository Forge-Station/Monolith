// Forge-Change
namespace Content.Shared.SecApartment;

public static class SecApartmentIcons
{
    public static string GetPrototypeId(SquadIconNum icon, string prefix = "SecuritySquadIcon")
    {
        var suffix = icon switch
        {
            SquadIconNum.Alpha => "Alpha",
            SquadIconNum.Beta => "Beta",
            SquadIconNum.Gamma => "Gamma",
            SquadIconNum.Delta => "Delta",
            SquadIconNum.Epsilon => "Epsilon",
            SquadIconNum.Zeta => "Zeta",
            SquadIconNum.Heta => "Heta",
            SquadIconNum.Theta => "Theta",
            SquadIconNum.Iota => "Iota",
            SquadIconNum.Kappa => "Kappa",
            SquadIconNum.Lambda => "Lambda",
            SquadIconNum.Mu => "Mu",
            SquadIconNum.Nu => "Nu",
            SquadIconNum.Xi => "Xi",
            SquadIconNum.Omicron => "Omicron",
            SquadIconNum.Pi => "Pi",
            SquadIconNum.Ro => "Ro",
            SquadIconNum.Sigma => "Sigma",
            SquadIconNum.Tau => "Tau",
            SquadIconNum.Upsilon => "Upsilon",
            SquadIconNum.Fi => "Fi",
            SquadIconNum.Hi => "Hi",
            SquadIconNum.Psi => "Psi",
            SquadIconNum.Omega => "Omega",
            _ => "Alpha"
        };

        return $"{prefix}{suffix}";
    }
}
