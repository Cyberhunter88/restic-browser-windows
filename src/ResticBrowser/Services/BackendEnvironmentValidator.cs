using System.Text.RegularExpressions;
using ResticBrowser.Models;

namespace ResticBrowser.Services;

public static partial class BackendEnvironmentValidator
{
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "RESTIC_PASSWORD", "RESTIC_PASSWORD_FILE", "RESTIC_PASSWORD_COMMAND",
        "RESTIC_REPOSITORY", "RESTIC_REPOSITORY_FILE", "AWS_ACCESS_KEY_ID",
        "AWS_SECRET_ACCESS_KEY", "AWS_SESSION_TOKEN", "AWS_DEFAULT_REGION",
        "RESTIC_REST_USERNAME", "RESTIC_REST_PASSWORD"
    };

    public static Dictionary<string, string> Normalize(IEnumerable<EnvironmentEntry> entries)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            var name = entry.Name?.Trim() ?? "";
            if (name.Length == 0) continue;
            if (!VariableName().IsMatch(name)) throw new ResticException($"Der Variablenname '{name}' ist ungültig.");
            if (Reserved.Contains(name)) throw new ResticException($"Die Variable '{name}' wird von Restic Browser selbst verwaltet.");
            if (!result.TryAdd(name, entry.Value ?? "")) throw new ResticException($"Die Variable '{name}' wurde mehrfach angegeben.");
        }
        return result;
    }

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex VariableName();
}
