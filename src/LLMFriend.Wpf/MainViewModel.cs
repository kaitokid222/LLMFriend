using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace LLMFriend.Wpf;

public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly ApiClient _api = new();
    private readonly DialogService _dialogs = new();
    private UserDto? _user;
    private AccountDto? _selectedAccount;
    private UserDto? _selectedUser;
    private UserDto? _selectedShare;
    private string _serverUrl = "http://localhost:5000";
    private string _username = "";
    private string _status = "Bitte API-Adresse und Zugangsdaten eingeben.";
    private bool _isBusy;
    private CancellationTokenSource? _linkCancellation;

    public event PropertyChangedEventHandler? PropertyChanged;
    public ObservableCollection<AccountDto> Accounts { get; } = [];
    public ObservableCollection<GuildDto> Guilds { get; } = [];
    public ObservableCollection<AccountEventDto> Events { get; } = [];
    public ObservableCollection<UserDto> DirectoryUsers { get; } = [];
    public ObservableCollection<UserDto> Shares { get; } = [];

    public string ServerUrl { get => _serverUrl; set => Set(ref _serverUrl, value); }
    public string Username { get => _username; set => Set(ref _username, value); }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public bool IsBusy { get => _isBusy; private set { Set(ref _isBusy, value); NotifyCommands(); } }
    public bool IsLoggedIn => _user is not null;
    public bool NeedsPasswordChange => _user?.MustChangePassword == true;
    public bool CanUseApp => IsLoggedIn && !NeedsPasswordChange;
    public bool IsAdmin => _user?.IsAdmin == true;
    public string SignedInAs => _user is null ? "" : $"Angemeldet als {_user.Username}";
    public bool CanDelete => _selectedAccount is not null && _selectedAccount.OwnerId == _user?.Id;

    public AccountDto? SelectedAccount
    {
        get => _selectedAccount;
        set
        {
            if (Set(ref _selectedAccount, value))
            {
                Changed(nameof(CanDelete));
                NotifyCommands();
                _ = LoadDetailSafeAsync(value?.Id);
            }
        }
    }
    public UserDto? SelectedUser { get => _selectedUser; set { Set(ref _selectedUser, value); NotifyCommands(); } }
    public UserDto? SelectedShare { get => _selectedShare; set { Set(ref _selectedShare, value); NotifyCommands(); } }

    public ICommand ReloadCommand { get; }
    public ICommand LinkCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand DisconnectCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand ShareCommand { get; }
    public ICommand UnshareCommand { get; }
    public ICommand MoreEventsCommand { get; }
    public ICommand CreateUserCommand { get; }
    public ICommand ResetPasswordCommand { get; }
    public ICommand LogoutCommand { get; }

    private readonly List<AsyncCommand> _commands = [];

    public MainViewModel()
    {
        ReloadCommand = Command(ReloadAsync, () => CanUseApp);
        LinkCommand = Command(LinkAsync, () => CanUseApp);
        RefreshCommand = Command(RefreshAsync, () => CanUseApp && SelectedAccount is not null);
        DisconnectCommand = Command(DisconnectAsync, () => CanUseApp && SelectedAccount is not null);
        DeleteCommand = Command(DeleteAsync, () => CanUseApp && CanDelete);
        ShareCommand = Command(ShareAsync, () => CanUseApp && SelectedAccount is not null && SelectedUser is not null);
        UnshareCommand = Command(UnshareAsync, () => CanUseApp && SelectedAccount is not null && SelectedShare is not null);
        MoreEventsCommand = Command(MoreEventsAsync, () => CanUseApp && SelectedAccount is not null && Events.Count > 0);
        CreateUserCommand = Command(CreateUserAsync, () => CanUseApp && IsAdmin);
        ResetPasswordCommand = Command(ResetPasswordAsync, () => CanUseApp && IsAdmin && SelectedUser is not null);
        LogoutCommand = Command(LogoutAsync, () => IsLoggedIn);
    }

    private AsyncCommand Command(Func<Task> action, Func<bool> canExecute)
    {
        var command = new AsyncCommand(() => RunAsync(action), () => !IsBusy && canExecute());
        _commands.Add(command);
        return command;
    }

    private void NotifyCommands() { foreach (var command in _commands) command.NotifyCanExecuteChanged(); }

    public async Task LoginAsync(string password)
    {
        await RunAsync(async () =>
        {
            _api.Configure(ServerUrl);
            var login = await _api.LoginAsync(Username, password);
            _api.SetToken(login.Token);
            _user = login.User;
            NotifyUser();
            if (NeedsPasswordChange) Status = "Bitte das Startpasswort ändern.";
            else await ReloadAsync();
        });
    }

    public async Task ChangePasswordAsync(string current, string next)
    {
        await RunAsync(async () =>
        {
            await _api.ChangePasswordAsync(current, next);
            _api.ClearToken();
            _user = null;
            NotifyUser();
            Status = "Passwort geändert. Bitte neu anmelden.";
        });
    }

    private async Task RunAsync(Func<Task> action)
    {
        if (IsBusy) return;
        IsBusy = true;
        try { await action(); }
        catch (Exception ex) { Status = ex.Message; }
        finally { IsBusy = false; }
    }

    private async Task ReloadAsync()
    {
        var selectedId = SelectedAccount?.Id;
        var accounts = await _api.AccountsAsync();
        Accounts.Clear();
        foreach (var account in accounts) Accounts.Add(account);
        DirectoryUsers.Clear();
        foreach (var user in await _api.DirectoryAsync()) DirectoryUsers.Add(user);
        SelectedAccount = Accounts.FirstOrDefault(a => a.Id == selectedId);
        Status = $"{Accounts.Count} Konto/Konten geladen.";
    }

    private async Task LoadDetailSafeAsync(string? accountId)
    {
        Guilds.Clear(); Events.Clear(); Shares.Clear();
        if (accountId is null) return;
        try
        {
            var detail = await _api.DetailAsync(accountId);
            var events = await _api.EventsAsync(accountId);
            var shares = await _api.SharesAsync(accountId);
            if (SelectedAccount?.Id != accountId) return;
            foreach (var guild in detail.Snapshot?.Guilds ?? []) Guilds.Add(guild);
            foreach (var item in events) Events.Add(item);
            foreach (var user in shares) Shares.Add(user);
            NotifyCommands();
        }
        catch (Exception ex) { Status = ex.Message; }
    }

    private async Task LinkAsync()
    {
        var link = await _api.BeginLinkAsync();
        Process.Start(new ProcessStartInfo(link.AuthorizationUrl) { UseShellExecute = true });
        Status = "Discord-Autorisierung im Browser abschließen …";
        _linkCancellation?.Cancel();
        _linkCancellation = new CancellationTokenSource();
        _ = PollLinkSafeAsync(link.PendingId, _linkCancellation.Token);
    }

    private async Task PollLinkSafeAsync(string pendingId, CancellationToken cancellationToken)
    {
        try
        {
            for (var i = 0; i < 200 && IsLoggedIn; i++)
            {
                await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
                var pending = await _api.LinkStatusAsync(pendingId);
                if (pending.Status == "pending") continue;
                if (pending.Status == "completed") { await ReloadAsync(); Status = "Discord-Konto verbunden."; return; }
                Status = pending.ErrorMessage ?? "Discord-Verknüpfung abgelaufen.";
                return;
            }
            Status = "Discord-Verknüpfung nicht abgeschlossen.";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Status = ex.Message; }
    }

    private async Task RefreshAsync()
    {
        await _api.RefreshAsync(SelectedAccount!.Id);
        Status = "Aktualisierung beim Monitoring-Dienst angefordert.";
    }

    private async Task DisconnectAsync()
    {
        if (!_dialogs.Confirm("Discord-Verbindung trennen?")) return;
        await _api.DisconnectAsync(SelectedAccount!.Id);
        await ReloadAsync();
    }

    private async Task DeleteAsync()
    {
        if (!_dialogs.Confirm("Konto und gesamten Verlauf endgültig löschen?")) return;
        await _api.DeleteAsync(SelectedAccount!.Id);
        await ReloadAsync();
    }

    private async Task ShareAsync()
    {
        await _api.ShareAsync(SelectedAccount!.Id, SelectedUser!.Id);
        await LoadDetailSafeAsync(SelectedAccount.Id);
    }

    private async Task UnshareAsync()
    {
        await _api.UnshareAsync(SelectedAccount!.Id, SelectedShare!.Id);
        await LoadDetailSafeAsync(SelectedAccount.Id);
    }

    private async Task MoreEventsAsync()
    {
        var older = await _api.EventsAsync(SelectedAccount!.Id, Events.Last().Id);
        foreach (var item in older) Events.Add(item);
        Status = older.Count == 0 ? "Keine älteren Ereignisse." : $"{older.Count} ältere Ereignisse geladen.";
    }

    private async Task CreateUserAsync()
    {
        var name = _dialogs.Prompt("Neuen Nutzer anlegen", "Nutzername:");
        if (string.IsNullOrWhiteSpace(name)) return;
        var result = await _api.CreateUserAsync(name);
        _dialogs.ShowSecret($"Startpasswort für {result.User.Username}", result.TemporaryPassword);
        await ReloadAsync();
    }

    private async Task ResetPasswordAsync()
    {
        if (!_dialogs.Confirm($"Passwort für {SelectedUser!.Username} zurücksetzen?")) return;
        var result = await _api.ResetPasswordAsync(SelectedUser.Id);
        _dialogs.ShowSecret($"Neues Startpasswort für {SelectedUser.Username}", result.TemporaryPassword);
    }

    private async Task LogoutAsync()
    {
        _linkCancellation?.Cancel();
        try { await _api.LogoutAsync(); }
        finally
        {
            _api.ClearToken();
            _user = null;
            Accounts.Clear(); Guilds.Clear(); Events.Clear(); Shares.Clear(); DirectoryUsers.Clear();
            SelectedAccount = null;
            NotifyUser();
            Status = "Abgemeldet.";
        }
    }

    private void NotifyUser()
    {
        Changed(nameof(IsLoggedIn)); Changed(nameof(NeedsPasswordChange)); Changed(nameof(CanUseApp));
        Changed(nameof(IsAdmin)); Changed(nameof(SignedInAs)); Changed(nameof(CanDelete)); NotifyCommands();
    }
    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value; Changed(propertyName); return true;
    }
    private void Changed(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    public void Dispose() { _linkCancellation?.Cancel(); _linkCancellation?.Dispose(); _api.Dispose(); }
}

public sealed class AsyncCommand(Func<Task> execute, Func<bool> canExecute) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => canExecute();
    public async void Execute(object? parameter) => await execute();
    public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
