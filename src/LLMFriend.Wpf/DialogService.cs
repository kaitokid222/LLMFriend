using System.Windows;
using System.Windows.Controls;

namespace LLMFriend.Wpf;

public sealed class DialogService
{
    public bool Confirm(string question) => MessageBox.Show(question, "LLMFriend", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public void ShowSecret(string title, string secret) => MessageBox.Show($"{secret}\n\nNur jetzt sichtbar. Bitte sicher weitergeben.", title, MessageBoxButton.OK, MessageBoxImage.Information);

    public string? Prompt(string title, string label)
    {
        var window = new Window { Title = title, Width = 360, Height = 160, WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = Application.Current.MainWindow, ResizeMode = ResizeMode.NoResize };
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = label });
        var input = new TextBox { Margin = new Thickness(0, 8, 0, 12) };
        panel.Children.Add(input);
        var button = new Button { Content = "Anlegen", HorizontalAlignment = HorizontalAlignment.Right, MinWidth = 80, IsDefault = true };
        button.Click += (_, _) => { window.DialogResult = true; window.Close(); };
        panel.Children.Add(button);
        window.Content = panel;
        return window.ShowDialog() == true ? input.Text : null;
    }
}
