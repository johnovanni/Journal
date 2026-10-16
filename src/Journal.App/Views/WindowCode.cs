using System.Windows;
using System.Windows.Interop;
using Journal.App.Services;
using Journal.App.ViewModels;

namespace Journal.App.Views;

public partial class UnlockWindow : Window
{
    public UnlockWindow(UnlockViewModel vm)
    {
        DataContext = vm;
        InitializeComponent();
        // Windows Hello is asked only when the user clicks the button (ISS-008); it is the default button,
        // so Enter works too. Closing the window cancels a request that is still waiting (ISS-007).
        ContentRendered += async (_, _) =>
        {
            await vm.InitializeAsync();
            if (!vm.CanUseHello) RecoveryBox.Focus();
        };
        Closed += (_, _) => vm.CancelPendingHello();
        // Keep the "opens on your main display" hint in step with the display this window is on (ISS-009).
        SourceInitialized += (_, _) => vm.IsOnPrimaryDisplay = DisplayInfo.IsOnPrimaryDisplay(Handle);
        LocationChanged += (_, _) => vm.IsOnPrimaryDisplay = DisplayInfo.IsOnPrimaryDisplay(Handle);
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
        Closed += (_, _) => vm.CancelPendingHello();
        SourceInitialized += (_, _) => vm.IsOnPrimaryDisplay = DisplayInfo.IsOnPrimaryDisplay(Handle);
        LocationChanged += (_, _) => vm.IsOnPrimaryDisplay = DisplayInfo.IsOnPrimaryDisplay(Handle);
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
