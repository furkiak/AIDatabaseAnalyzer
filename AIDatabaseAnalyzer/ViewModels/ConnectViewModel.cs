using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AIDatabaseAnalyzer.Models;
using AIDatabaseAnalyzer.Services.Database;
using AIDatabaseAnalyzer.Services.Infrastructure;

namespace AIDatabaseAnalyzer.ViewModels;

/// <summary>Step 1: choose the engine, enter credentials (or pick a saved connection) and connect.</summary>
public partial class ConnectViewModel : ObservableObject
{
    private readonly WorkspaceSession _session;
    private readonly IDbSchemaService _db;
    private readonly IConnectionProfileStore _profiles;
    private readonly IDialogService _dialogs;
    private readonly INoticeService _notices;
    private CancellationTokenSource? _connectCts;

    public ConnectViewModel(
        WorkspaceSession session,
        IDbSchemaService db,
        IConnectionProfileStore profiles,
        IDialogService dialogs,
        INoticeService notices)
    {
        _session = session;
        _db = db;
        _profiles = profiles;
        _dialogs = dialogs;
        _notices = notices;

        foreach (var profile in profiles.Load().OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase))
            Profiles.Add(profile);

        RestoreLastUsed();
        UpdateEngineHints();
    }

    public IReadOnlyList<string> Engines { get; } = new[] { "SQL Server", "PostgreSQL", "MySQL / MariaDB" };

    public ObservableCollection<SavedConnection> Profiles { get; } = new();

    [NotifyPropertyChangedFor(nameof(ShowIntegratedSecurity))]
    [NotifyPropertyChangedFor(nameof(ShowCredentials))]
    [ObservableProperty] private int selectedEngineIndex;

    [ObservableProperty] private string server = "";
    [ObservableProperty] private string port = "";
    [ObservableProperty] private string username = "";
    [ObservableProperty] private string password = "";

    [NotifyPropertyChangedFor(nameof(ShowCredentials))]
    [ObservableProperty] private bool useIntegratedSecurity;

    [ObservableProperty] private bool trustServerCertificate = true;
    [ObservableProperty] private string profileName = "";
    [ObservableProperty] private bool rememberPassword = true;

    [ObservableProperty] private SavedConnection? selectedProfile;

    [ObservableProperty] private string serverPlaceholder = "localhost";
    [ObservableProperty] private string portPlaceholder = "1433";
    [ObservableProperty] private string userPlaceholder = "sa";

    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    [ObservableProperty] private bool isBusy;

    [NotifyPropertyChangedFor(nameof(HasError))]
    [ObservableProperty] private string? errorMessage;

    public bool IsNotBusy => !IsBusy;
    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);
    public bool HasProfiles => Profiles.Count > 0;

    /// <summary>Windows authentication only exists for SQL Server on Windows.</summary>
    public bool ShowIntegratedSecurity => SelectedEngineIndex == 0 && OperatingSystem.IsWindows();

    public bool ShowCredentials => !(ShowIntegratedSecurity && UseIntegratedSecurity);

    partial void OnSelectedEngineIndexChanged(int value)
    {
        if (value != 0) UseIntegratedSecurity = false;
        UpdateEngineHints();
    }

    partial void OnSelectedProfileChanged(SavedConnection? value)
    {
        if (value is null) return;
        _ = LoadProfileAsync(value);
    }

    private void UpdateEngineHints()
    {
        var engine = (DbEngine)Math.Clamp(SelectedEngineIndex, 0, 2);
        ServerPlaceholder = engine == DbEngine.SqlServer ? @"localhost\SQLEXPRESS" : "localhost";
        PortPlaceholder = engine.DefaultPort().ToString();
        UserPlaceholder = engine switch { DbEngine.SqlServer => "sa", DbEngine.PostgreSql => "postgres", _ => "root" };
    }

    private async Task LoadProfileAsync(SavedConnection profile)
    {
        SelectedEngineIndex = Math.Clamp(profile.Engine, 0, 2);
        Server = profile.Server;
        Port = profile.Port?.ToString() ?? "";
        Username = profile.Username;
        UseIntegratedSecurity = profile.UseIntegratedSecurity && ShowIntegratedSecurity;
        TrustServerCertificate = profile.TrustServerCertificate;
        ProfileName = profile.Name;
        Password = profile.HasSavedPassword ? await _profiles.GetPasswordAsync(profile.Id) ?? "" : "";
        ErrorMessage = null;
    }

    // ------------------------------------------------------------------ connect

    [RelayCommand(CanExecute = nameof(IsNotBusy))]
    private async Task ConnectAsync()
    {
        ErrorMessage = null;

        if (string.IsNullOrWhiteSpace(Server))
        {
            ErrorMessage = "Enter the server address first.";
            return;
        }

        int? portNumber = null;
        if (!string.IsNullOrWhiteSpace(Port))
        {
            if (!int.TryParse(Port.Trim(), out int p) || p is < 1 or > 65535)
            {
                ErrorMessage = "The port must be a number between 1 and 65535.";
                return;
            }
            portNumber = p;
        }

        var settings = new ConnectionSettings
        {
            Engine = (DbEngine)Math.Clamp(SelectedEngineIndex, 0, 2),
            Server = Server.Trim(),
            Port = portNumber,
            Username = Username.Trim(),
            Password = Password,
            UseIntegratedSecurity = ShowIntegratedSecurity && UseIntegratedSecurity,
            TrustServerCertificate = TrustServerCertificate
        };

        _connectCts?.Cancel();
        var cts = _connectCts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        IsBusy = true;
        try
        {
            var databases = await _db.GetDatabasesAsync(settings, cts.Token);
            if (databases.Count == 0)
            {
                ErrorMessage = "Connected, but no accessible databases were found for this account.";
                return;
            }

            SaveLastUsed(settings);
            _session.SetConnection(settings, databases);
            _notices.Show($"Connected to {settings.Server} — {databases.Count} databases found.", NoticeKind.Success);
            _session.TryNavigate(AppSection.Scope);
        }
        catch (OperationCanceledException)
        {
            ErrorMessage = "The connection attempt timed out.";
        }
        catch (Exception ex)
        {
            ErrorMessage = Describe(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>First line of the provider's message, without stack noise.</summary>
    private static string Describe(Exception ex)
    {
        string message = ex.GetBaseException().Message.Trim();
        int newline = message.IndexOfAny(new[] { '\r', '\n' });
        if (newline > 0) message = message[..newline];
        return message.Length > 300 ? message[..300] + "…" : message;
    }

    // ------------------------------------------------------------------ profiles

    [RelayCommand]
    private async Task SaveProfileAsync()
    {
        if (string.IsNullOrWhiteSpace(Server))
        {
            ErrorMessage = "Enter a server address before saving the connection.";
            return;
        }

        string name = string.IsNullOrWhiteSpace(ProfileName) ? Server.Trim() : ProfileName.Trim();
        var existing = Profiles.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

        int? portNumber = int.TryParse(Port.Trim(), out int parsed) ? parsed : null;
        var profile = existing ?? new SavedConnection();
        profile.Name = name;
        profile.Engine = Math.Clamp(SelectedEngineIndex, 0, 2);
        profile.Server = Server.Trim();
        profile.Port = portNumber;
        profile.Username = Username.Trim();
        profile.UseIntegratedSecurity = ShowIntegratedSecurity && UseIntegratedSecurity;
        profile.TrustServerCertificate = TrustServerCertificate;

        await _profiles.SaveAsync(profile, RememberPassword ? Password : null);

        if (existing is not null) Profiles.Remove(existing);
        Profiles.Add(profile);
        SortProfiles();
        OnPropertyChanged(nameof(HasProfiles));

        _notices.Show(RememberPassword && !string.IsNullOrEmpty(Password)
            ? $"Saved “{name}” (password stored securely)."
            : $"Saved “{name}”.", NoticeKind.Success);
    }

    [RelayCommand]
    private async Task DeleteProfileAsync()
    {
        var profile = SelectedProfile;
        if (profile is null) return;

        if (!await _dialogs.ConfirmAsync("Delete connection", $"Remove “{profile.Name}” and its saved password?", "Delete")) return;

        await _profiles.DeleteAsync(profile.Id);
        SelectedProfile = null;
        Profiles.Remove(profile);
        OnPropertyChanged(nameof(HasProfiles));
    }

    private void SortProfiles()
    {
        var sorted = Profiles.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        for (int i = 0; i < sorted.Count; i++)
        {
            int current = Profiles.IndexOf(sorted[i]);
            if (current != i) Profiles.Move(current, i);
        }
    }

    // ------------------------------------------------------------------ last used (no secrets)

    private void SaveLastUsed(ConnectionSettings s)
    {
        Preferences.Default.Set("last.engine", (int)s.Engine);
        Preferences.Default.Set("last.server", s.Server);
        Preferences.Default.Set("last.port", s.Port?.ToString() ?? "");
        Preferences.Default.Set("last.user", s.Username);
        Preferences.Default.Set("last.integrated", s.UseIntegratedSecurity);
        Preferences.Default.Set("last.trust", s.TrustServerCertificate);
    }

    private void RestoreLastUsed()
    {
        SelectedEngineIndex = Math.Clamp(Preferences.Default.Get("last.engine", 0), 0, 2);
        Server = Preferences.Default.Get("last.server", "");
        Port = Preferences.Default.Get("last.port", "");
        Username = Preferences.Default.Get("last.user", "");
        TrustServerCertificate = Preferences.Default.Get("last.trust", true);
        UseIntegratedSecurity = Preferences.Default.Get("last.integrated", false) && ShowIntegratedSecurity;
    }
}
