using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace Journal.App.Services;

public sealed class InvertBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
}

/// <summary>WPF implementation of <see cref="IDialogService"/>. Small dialogs are built in code.</summary>
public sealed class DialogService : IDialogService
{
    private static Window? Owner => Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                                    ?? Application.Current?.MainWindow;

    public string? OpenFile(string title, string filter)
    {
        var dlg = new OpenFileDialog { Title = title, Filter = filter };
        return dlg.ShowDialog(Owner) == true ? dlg.FileName : null;
    }

    public string[] OpenFiles(string title, string filter)
    {
        var dlg = new OpenFileDialog { Title = title, Filter = filter, Multiselect = true };
        return dlg.ShowDialog(Owner) == true ? dlg.FileNames : [];
    }

    public string? SaveFile(string title, string fileName, string filter)
    {
        var dlg = new SaveFileDialog { Title = title, FileName = fileName, Filter = filter };
        return dlg.ShowDialog(Owner) == true ? dlg.FileName : null;
    }

    public string? PickFolder(string title, string? initial = null)
    {
        var dlg = new OpenFolderDialog { Title = title };
        if (initial is not null && System.IO.Directory.Exists(initial)) dlg.InitialDirectory = initial;
        return dlg.ShowDialog(Owner) == true ? dlg.FolderName : null;
    }

    public bool Confirm(string title, string message, string confirmText = "OK") =>
        ShowMessage(title, message, confirmText, "Cancel", danger: confirmText.StartsWith("Delete", StringComparison.Ordinal) || confirmText == "Remove");

    public void Info(string title, string message) => ShowMessage(title, message, "OK", null);

    public void Error(string title, string message) => ShowMessage(title, message, "OK", null);

    public string? PromptTag(string title, IReadOnlyList<string> suggestions, string? initial = null)
    {
        var combo = new ComboBox
        {
            IsEditable = true,
            ItemsSource = suggestions,
            Text = initial ?? "",
            IsTextSearchEnabled = true,
            MinWidth = 280,
        };
        System.Windows.Automation.AutomationProperties.SetName(combo, "Tag name");
        var error = new TextBlock { Foreground = (Brush)Application.Current.FindResource("Danger"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
        string? result = null;
        var window = BuildWindow(title, out var body, out var buttons);
        body.Children.Add(new TextBlock { Text = "Tag name (for example family, work, prayer)", Margin = new Thickness(0, 0, 0, 8) });
        body.Children.Add(combo);
        body.Children.Add(error);
        var ok = AddButton(buttons, "OK", isDefault: true);
        AddButton(buttons, "Cancel", isCancel: true).Click += (_, _) => window.Close();
        ok.Click += (_, _) =>
        {
            if (!Core.Domain.Tag.TryParse(combo.Text, out _, out var message))
            {
                error.Text = message;
                return;
            }
            result = combo.Text.Trim();
            window.Close();
        };
        window.Loaded += (_, _) => combo.Focus();
        window.ShowDialog();
        return result;
    }

    public void ShowImage(ImageSource image, string caption)
    {
        var window = new Window
        {
            Title = string.IsNullOrWhiteSpace(caption) ? "Image" : caption,
            Owner = Owner,
            Width = Math.Min(1200, SystemParameters.WorkArea.Width * 0.9),
            Height = Math.Min(900, SystemParameters.WorkArea.Height * 0.9),
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brushes.Black,
        };
        var grid = new DockPanel();
        if (!string.IsNullOrWhiteSpace(caption))
        {
            var text = new TextBlock { Text = caption, Foreground = Brushes.White, Margin = new Thickness(12), TextWrapping = TextWrapping.Wrap, FontStyle = FontStyles.Italic };
            DockPanel.SetDock(text, Dock.Bottom);
            grid.Children.Add(text);
        }
        grid.Children.Add(new Image { Source = image, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly });
        window.Content = grid;
        window.KeyDown += (_, e) => { if (e.Key == Key.Escape) window.Close(); };
        window.ShowDialog();
    }

    public void ShowRecoveryCode(string code)
    {
        var window = BuildWindow("Your new recovery code", out var body, out var buttons);
        body.Children.Add(new TextBlock
        {
            Text = "Save this code somewhere safe, away from this PC. It is shown only once. Your old code no longer works.",
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 420,
        });
        body.Children.Add(new TextBox
        {
            Text = code, IsReadOnly = true, FontFamily = new FontFamily("Consolas"), FontSize = 20, Margin = new Thickness(0, 14, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left, BorderThickness = new Thickness(0), Background = Brushes.Transparent,
        });
        AddButton(buttons, "Copy").Click += (_, _) => Clipboard.SetText(code);
        AddButton(buttons, "Save to file…").Click += (_, _) =>
        {
            var path = SaveFile("Save recovery code", "Journal recovery code.txt", "Text file|*.txt");
            if (path is not null) System.IO.File.WriteAllText(path, ViewModels.SetupViewModel.RecoveryCodeFile(code));
        };
        AddButton(buttons, "Done", isDefault: true).Click += (_, _) => window.Close();
        window.ShowDialog();
    }

    private bool ShowMessage(string title, string message, string okText, string? cancelText, bool danger = false)
    {
        bool confirmed = false;
        var window = BuildWindow(title, out var body, out var buttons);
        body.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 440 });
        var ok = AddButton(buttons, okText, isDefault: true);
        if (danger)
        {
            ok.Background = (Brush)Application.Current.FindResource("Danger");
            ok.Foreground = Brushes.White;
            ok.BorderBrush = ok.Background;
        }
        ok.Click += (_, _) => { confirmed = true; window.Close(); };
        if (cancelText is not null) AddButton(buttons, cancelText, isCancel: true).Click += (_, _) => window.Close();
        window.ShowDialog();
        return confirmed;
    }

    private static Window BuildWindow(string title, out StackPanel body, out StackPanel buttons)
    {
        var window = new Window
        {
            Title = title,
            Owner = Owner,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = Owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            Style = (Style)Application.Current.FindResource("AppWindow"),
        };
        body = new StackPanel { Margin = new Thickness(24, 20, 24, 12) };
        buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(24, 8, 24, 20) };
        var root = new StackPanel();
        root.Children.Add(new TextBlock { Text = title, FontSize = 17, FontWeight = FontWeights.SemiBold, Margin = new Thickness(24, 20, 24, 0) });
        root.Children.Add(body);
        root.Children.Add(buttons);
        window.Content = root;
        return window;
    }

    private static Button AddButton(Panel panel, string text, bool isDefault = false, bool isCancel = false)
    {
        var b = new Button { Content = text, IsDefault = isDefault, IsCancel = isCancel, Margin = new Thickness(8, 0, 0, 0), MinWidth = 80 };
        if (isDefault) b.Style = (Style)Application.Current.FindResource("PrimaryButton");
        panel.Children.Add(b);
        return b;
    }
}
