using System.Windows;
using System.Windows.Interop;
using Journal.App.ViewModels;

namespace Journal.App.Views;

public partial class UnlockWindow : Window
{
    public UnlockWindow(UnlockViewModel vm)
    {
        DataContext = vm;
        InitializeComponent();
        // Prompt for Windows Hello as soon as the window is visible (FR-SEC-08).
        ContentRendered += async (_, _) =>
        {
            await vm.InitializeAsync();
            if (vm.CanUseHello) await vm.TryHelloCommand.ExecuteAsync(null);
            else RecoveryBox.Focus();
        };
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(UnlockViewModel.ShowRecovery) && vm.ShowRecovery) Dispatcher.BeginInvoke(() => RecoveryBox.Focus());
        };
    }

    public nint Handle => new WindowInteropHelper(this).Handle;
}

public partial class SetupWindow : Window
{
    public SetupWindow(SetupViewModel vm)
    {
        DataContext = vm;
        InitializeComponent();
        vm.CopyToClipboard = text => Clipboard.SetText(text);
        Loaded += async (_, _) => await vm.InitializeAsync();
    }

    public nint Handle => new WindowInteropHelper(this).Handle;
}

public partial class SettingsWindow : Window
{
    public SettingsWindow(SettingsViewModel vm)
    {
        DataContext = vm;
        InitializeComponent();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
