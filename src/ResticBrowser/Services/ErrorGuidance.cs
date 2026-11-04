namespace ResticBrowser.Services;

/// <summary>Ergänzt bekannte Restic-Fehler um einen konkreten Handlungshinweis.</summary>
public static class ErrorGuidance
{
    public static string Describe(ResticException exception)
    {
        var hint = exception.ExitCode switch
        {
            10 => "Prüfe den Repository-Pfad und ob das Laufwerk oder die Netzfreigabe erreichbar ist.",
            11 => "Warte, bis der andere Vorgang beendet ist, und versuche es erneut.",
            12 => "Prüfe das Passwort auf Tippfehler und die Umschalt- oder Feststelltaste.",
            _ => null
        };
        if (hint is null && exception.Message.Contains("Restic", StringComparison.OrdinalIgnoreCase)
            && exception.Message.Contains("nicht gefunden", StringComparison.OrdinalIgnoreCase))
            hint = "Wähle im Verbindungsdialog unter „Erweitert“ eine Restic-Datei aus.";
        return hint is null ? exception.Message : $"{exception.Message}{Environment.NewLine}{Environment.NewLine}{hint}";
    }
}
