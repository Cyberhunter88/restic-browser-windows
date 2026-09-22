using System.Runtime.InteropServices;
using System.Text;

namespace ResticBrowser.Services;

public sealed record SessionDiagnosticSnapshot(
    DateTimeOffset StartedAt,
    IReadOnlyList<ResticCommandMetric> Metrics);

public sealed class SessionDiagnosticCollector : IResticCommandObserver
{
    public const int MaximumMetricCount = 200;

    private readonly object _sync = new();
    private readonly Queue<ResticCommandMetric> _metrics = new();
    private bool _isEnabled;
    private DateTimeOffset? _startedAt;

    public bool IsEnabled
    {
        get { lock (_sync) return _isEnabled; }
    }

    public bool HasSession
    {
        get { lock (_sync) return _startedAt is not null; }
    }

    public void Start(DateTimeOffset? startedAt = null)
    {
        lock (_sync)
        {
            _metrics.Clear();
            _startedAt = startedAt ?? DateTimeOffset.Now;
            _isEnabled = true;
        }
    }

    public void Stop()
    {
        lock (_sync) _isEnabled = false;
    }

    public void Completed(ResticCommandMetric metric)
    {
        lock (_sync)
        {
            if (!_isEnabled) return;
            while (_metrics.Count >= MaximumMetricCount) _metrics.Dequeue();
            _metrics.Enqueue(metric);
        }
    }

    public SessionDiagnosticSnapshot? CreateSnapshot()
    {
        lock (_sync)
            return _startedAt is null ? null : new SessionDiagnosticSnapshot(_startedAt.Value, _metrics.ToArray());
    }
}

public sealed record DiagnosticReportContext(
    string ApplicationVersion,
    string OperatingSystem,
    string ProcessArchitecture,
    string RuntimeVersion,
    string? ResticVersion,
    string? ResticSource)
{
    public static DiagnosticReportContext Create(string applicationVersion, string? resticVersion, string? resticSource) =>
        new(applicationVersion, RuntimeInformation.OSDescription, RuntimeInformation.ProcessArchitecture.ToString(),
            RuntimeInformation.FrameworkDescription, resticVersion, resticSource);
}

public static class SessionDiagnosticReportFormatter
{
    public static string Format(SessionDiagnosticSnapshot session, DiagnosticReportContext context, DateTimeOffset createdAt)
    {
        var report = new StringBuilder();
        report.AppendLine("Restic Browser – Sitzungsdiagnose");
        report.AppendLine("Berichtsformat: 1");
        report.AppendLine($"Erstellt: {createdAt:O}");
        report.AppendLine($"Sitzungsbeginn: {session.StartedAt:O}");
        report.AppendLine($"App-Version: {context.ApplicationVersion}");
        report.AppendLine($"Betriebssystem: {context.OperatingSystem}");
        report.AppendLine($"Prozessarchitektur: {context.ProcessArchitecture}");
        report.AppendLine($".NET-Runtime: {context.RuntimeVersion}");
        if (!string.IsNullOrWhiteSpace(context.ResticVersion)) report.AppendLine($"Restic-Version: {context.ResticVersion}");
        if (!string.IsNullOrWhiteSpace(context.ResticSource)) report.AppendLine($"Restic-Herkunft: {context.ResticSource}");
        report.AppendLine($"Erfasste Vorgänge: {session.Metrics.Count}");
        report.AppendLine();
        report.AppendLine("Datenschutz: Dieser Bericht enthält keine Repository- oder Zielpfade, Argumente, ausführbaren Dateien, Umgebungsvariablen, Zugangsdaten, Hostnamen, Benutzerkennungen, Standardausgaben oder Rohfehlermeldungen.");
        report.AppendLine();
        report.AppendLine("Vorgang;Backend;Zeit bis erste Ausgabe (ms);Laufzeit (ms);Ausgabe (Bytes);Ausgabe (Zeilen);Exit-Code;Früh beendet");
        foreach (var metric in session.Metrics)
        {
            report.Append(metric.Operation).Append(';')
                .Append(metric.BackendType).Append(';')
                .Append(metric.TimeToFirstOutput?.TotalMilliseconds.ToString("F0", System.Globalization.CultureInfo.InvariantCulture) ?? "-").Append(';')
                .Append(metric.Duration.TotalMilliseconds.ToString("F0", System.Globalization.CultureInfo.InvariantCulture)).Append(';')
                .Append(metric.OutputBytes).Append(';')
                .Append(metric.OutputLines).Append(';')
                .Append(metric.ExitCode).Append(';')
                .Append(metric.StoppedEarly ? "Ja" : "Nein").AppendLine();
        }
        return report.ToString();
    }
}
