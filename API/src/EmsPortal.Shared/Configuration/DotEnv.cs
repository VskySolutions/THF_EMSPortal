namespace EmsPortal.Shared.Configuration;

/// <summary>
/// Loads a <c>.env</c> file into the process environment so the standard environment-variable configuration
/// provider picks its lines up: <c>Authentication__Microsoft__ClientId=…</c> sets
/// <c>Authentication:Microsoft:ClientId</c>. A variable that already exists in the environment is left alone,
/// so a real deployment setting is never overridden by a stray file.
/// </summary>
public static class DotEnv
{
    public const string FileName = ".env";

    /// <summary>
    /// Loads the first <c>.env</c> found in <paramref name="directories"/> and returns its path, or null when
    /// there is none.
    /// </summary>
    public static string? Load(params string[] directories)
    {
        foreach (var directory in directories)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                continue;
            }

            var path = Path.Combine(directory, FileName);
            if (!File.Exists(path))
            {
                continue;
            }

            foreach (var line in File.ReadLines(path))
            {
                Apply(line);
            }

            return path;
        }

        return null;
    }

    private static void Apply(string rawLine)
    {
        var line = rawLine.Trim();
        if (line.Length == 0 || line.StartsWith('#'))
        {
            return;
        }

        if (line.StartsWith("export ", StringComparison.Ordinal))
        {
            line = line[7..].TrimStart();
        }

        // Split at the FIRST '=' so a value may itself contain one (client secrets sometimes do).
        var separator = line.IndexOf('=');
        if (separator <= 0)
        {
            return;
        }

        var key = line[..separator].Trim();
        if (key.Length == 0 || Environment.GetEnvironmentVariable(key) is not null)
        {
            return;
        }

        Environment.SetEnvironmentVariable(key, ParseValue(line[(separator + 1)..].Trim()));
    }

    /// <summary>
    /// Quoted values keep everything between the quotes (double quotes also expand <c>\n</c>, which is how a
    /// PEM key fits on one line); bare values stop at a <c> #</c> comment.
    /// </summary>
    private static string ParseValue(string value)
    {
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
        {
            return value[1..^1].Replace("\\n", "\n").Replace("\\\"", "\"");
        }

        if (value.Length >= 2 && value[0] == '\'' && value[^1] == '\'')
        {
            return value[1..^1];
        }

        var comment = value.IndexOf(" #", StringComparison.Ordinal);
        return comment >= 0 ? value[..comment].TrimEnd() : value;
    }
}
