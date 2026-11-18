namespace Content.Shared._Forge.Company;

/// <summary>
/// Groups digits by three so 2500000 reads as 2 500 000.
/// Uses only APIs the content sandbox allows.
/// </summary>
public static class CompanyNumbers
{
    public static string Format(long value)
    {
        if (value < 0)
            return "-" + FormatDigits(-value);

        return FormatDigits(value);
    }

    private static string FormatDigits(long value)
    {
        var digits = value.ToString();
        if (digits.Length <= 3)
            return digits;

        var parts = new string[(digits.Length + 2) / 3];
        var index = parts.Length;
        var end = digits.Length;
        while (end > 0)
        {
            var start = end - 3;
            if (start < 0)
                start = 0;

            index--;
            parts[index] = digits.Substring(start, end - start);
            end = start;
        }

        return string.Join(" ", parts);
    }
}
