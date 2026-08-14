using Avalonia.Controls;
using ResticBrowser.Models;
using ResticBrowser.Services;

namespace ResticBrowser.Views;

public partial class RestoreWindow
{
    private RemoteRestoreTarget BuildRemoteTarget()
    {
        if (!int.TryParse(RemotePortBox.Text, out var port) || port is < 1 or > 65535)
            throw new ResticException("Bitte einen gültigen SSH-Port angeben.");
        var target = new RemoteRestoreTarget
        {
            Id = (RemoteSessionTargetBox.SelectedItem as RemoteRestoreTarget)?.Id ?? Guid.NewGuid(),
            Name = (RemoteNameBox.Text ?? "").Trim(),
            Host = (RemoteHostBox.Text ?? "").Trim(),
            Port = port,
            User = (RemoteUserBox.Text ?? "").Trim(),
            AuthenticationType = GetRemoteAuthenticationType(),
            PrivateKeyFile = (RemoteKeyBox.Text ?? "").Trim(),
            ResticExecutable = string.IsNullOrWhiteSpace(RemoteResticBox.Text) ? "restic" : RemoteResticBox.Text.Trim(),
            Repository = (RemoteRepositoryBox.Text ?? "").Trim(),
            AllowedRoot = (RemoteAllowedRootBox.Text ?? "").Trim()
        };
        if (string.IsNullOrWhiteSpace(target.Host) || string.IsNullOrWhiteSpace(target.User) ||
            string.IsNullOrWhiteSpace(target.Repository) || string.IsNullOrWhiteSpace(target.AllowedRoot))
            throw new ResticException("Bitte Host, Benutzer, Remote-Repository und erlaubten Basisordner vollständig angeben.");
        if (!target.AllowedRoot.StartsWith('/')) throw new ResticException("Der erlaubte Basisordner muss ein absoluter Linux-Pfad sein.");
        return target;
    }

    private RemoteSshCredentials BuildRemoteCredentials() => new(
        RemotePasswordBox.Text ?? "", RemoteKeyPassphraseBox.Text ?? "");

    private RemoteAuthenticationType GetRemoteAuthenticationType() =>
        Enum.TryParse<RemoteAuthenticationType>((RemoteAuthBox?.SelectedItem as ComboBoxItem)?.Tag?.ToString(), out var value)
            ? value : RemoteAuthenticationType.Agent;

    private void PopulateRemoteTarget(RemoteRestoreTarget target)
    {
        RemoteNameBox.Text = target.Name;
        RemoteHostBox.Text = target.Host;
        RemotePortBox.Text = target.Port.ToString();
        RemoteUserBox.Text = target.User;
        RemoteAuthBox.SelectedIndex = (int)target.AuthenticationType;
        RemoteKeyBox.Text = target.PrivateKeyFile;
        RemoteKeyPassphraseBox.Text = "";
        RemotePasswordBox.Text = "";
        RemoteResticBox.Text = target.ResticExecutable;
        RemoteRepositoryBox.Text = target.Repository;
        RemoteAllowedRootBox.Text = target.AllowedRoot;
        RemoteTargetBox.Text = target.AllowedRoot.TrimEnd('/') + "/Restic-Wiederherstellung";
    }

    private static void CopyRemoteTarget(RemoteRestoreTarget source, RemoteRestoreTarget destination)
    {
        destination.Name = source.Name;
        destination.Host = source.Host;
        destination.Port = source.Port;
        destination.User = source.User;
        destination.AuthenticationType = source.AuthenticationType;
        destination.PrivateKeyFile = source.PrivateKeyFile;
        destination.ResticExecutable = source.ResticExecutable;
        destination.Repository = source.Repository;
        destination.AllowedRoot = source.AllowedRoot;
    }

    private async Task<T> ExecuteWithHostTrustAsync<T>(Func<Task<T>> action)
    {
        try { return await action(); }
        catch (RemoteHostKeyException ex) when (!ex.HostKey.Changed)
        {
            var confirmed = await DialogService.ConfirmAsync(this, "SSH-Hostschlüssel bestätigen",
                $"Server: {ex.HostKey.Host}:{ex.HostKey.Port}\nAlgorithmus: {ex.HostKey.Algorithm}\nFingerprint: {ex.HostKey.Fingerprint}\n\nVergleiche den Fingerprint mit einer vertrauenswürdigen Quelle.", "Vertrauen");
            if (!confirmed) throw new OperationCanceledException();
            await _viewModel.TrustRemoteHostAsync(ex.HostKey);
            return await action();
        }
        catch (RemoteHostKeyException ex)
        {
            var remove = await DialogService.ConfirmAsync(this, "SSH-Hostschlüssel geändert",
                $"Die Verbindung wurde blockiert. Neuer Fingerprint:\n{ex.HostKey.Fingerprint}\n\nNur nach unabhängiger Prüfung darf das bisherige Vertrauen entfernt werden.", "Vertrauen entfernen");
            if (remove) await _viewModel.RemoveRemoteHostTrustAsync(ex.HostKey.Host, ex.HostKey.Port);
            throw new ResticException(remove
                ? "Das bisherige Hostvertrauen wurde entfernt. Prüfe den neuen Fingerprint und starte die Verbindung erneut."
                : ex.Message);
        }
    }
}
