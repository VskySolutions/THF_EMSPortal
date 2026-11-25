namespace EmsPortal.Api.Rems;

/// <summary>The country picked for a phone number, as it is stored: ISO-2, upper case.</summary>
public static class PhoneCountry
{
    /// <summary>"ca" → "CA"; anything that is not two letters (a dial code, a blank) → null.</summary>
    public static string? Clean(string? value)
    {
        var code = value?.Trim();
        return code is { Length: 2 } && char.IsAsciiLetter(code[0]) && char.IsAsciiLetter(code[1])
            ? code.ToUpperInvariant()
            : null;
    }
}
