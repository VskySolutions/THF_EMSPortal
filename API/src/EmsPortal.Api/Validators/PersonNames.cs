using System.Globalization;
using FluentValidation;

namespace EmsPortal.Api.Validators;

/// <summary>What a person's name may be, in one place.</summary>
public static class PersonNames
{
    /// <summary>Mirrors the nvarchar(100) name columns, and the browser's NAME_MAX_LENGTH.</summary>
    public const int MaxLength = 100;

    /// <summary>
    /// What is wrong with a name, as a sentence to show against the field — or <c>null</c> when
    /// there is nothing wrong.
    /// </summary>
    public static string? Issue(string? value, string label = "This name")
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (!string.Equals(value, trimmed, StringComparison.Ordinal))
        {
            return $"{label} cannot start or end with a space.";
        }
        if (trimmed.Length > MaxLength)
        {
            return $"{label} is at most {MaxLength} characters.";
        }
        if (trimmed.Any(char.IsDigit))
        {
            return $"{label} cannot contain numbers.";
        }
        if (trimmed.Contains("  ", StringComparison.Ordinal))
        {
            return $"{label} cannot contain two spaces in a row.";
        }
        if (!IsNameStart(trimmed[0]))
        {
            return $"{label} must start with a letter.";
        }
        return trimmed.All(IsNameChar)
            ? null
            : $"{label} can only contain letters, spaces, hyphens, apostrophes and periods.";
    }

    /// <summary>True when a name is usable as it stands (an empty one included — see <see cref="Issue"/>).</summary>
    public static bool IsValid(string? value) => Issue(value) is null;

    // A letter from any script, plus the combining marks that go with one (an accent written as its own code
    // point). char.IsLetter covers the scripts; the mark categories cover decomposed forms.
    private static bool IsNameStart(char c) => char.IsLetter(c) || IsMark(c);

    private static bool IsNameChar(char c) =>
        IsNameStart(c) || c is ' ' or '-' or '\'' or '’' or '.';

    private static bool IsMark(char c) => CharUnicodeInfo.GetUnicodeCategory(c) is
        UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark;
}

/// <summary>Wires <see cref="PersonNames"/> into a FluentValidation chain.</summary>
public static class PersonNameValidatorExtensions
{
    /// <summary>The property must read as a person's name.</summary>
    public static IRuleBuilderOptions<T, string?> MustBeAPersonName<T>(
        this IRuleBuilder<T, string?> rule, string label)
        => rule
            .Must(value => PersonNames.IsValid(value))
            // The message is re-derived from the value so the caller is told WHICH rule they broke — a
            // digit, a doubled space, a symbol — rather than being read the whole rule back at them.
            .WithMessage((_, value) => PersonNames.Issue(value, label)
                ?? $"{label} can only contain letters, spaces, hyphens, apostrophes and periods.");
}
