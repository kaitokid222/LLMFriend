using System.Windows;

namespace LLMFriend.Wpf;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        Closed += (_, _) => _viewModel.Dispose();
    }

    private async void Login_Click(object sender, RoutedEventArgs e)
    {
        var password = LoginPassword.Password;
        LoginPassword.Clear();
        await _viewModel.LoginAsync(password);
    }

    private async void ChangePassword_Click(object sender, RoutedEventArgs e)
    {
        var current = CurrentPassword.Password;
        var next = NewPassword.Password;
        CurrentPassword.Clear(); NewPassword.Clear();
        await _viewModel.ChangePasswordAsync(current, next);
    }
}
