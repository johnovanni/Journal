using System.Windows;
using System.Windows.Controls;
using Journal.App.ViewModels;

namespace Journal.App.Views;

/// <summary>Every tracked value's graph (FR-TRK-09). It belongs to the main window, so it closes when the journal locks.</summary>
public partial class GraphsWindow : Window
{
    public GraphsWindow(GraphsViewModel vm)
    {
        DataContext = vm;
        InitializeComponent();
        Closed += (_, _) => vm.Detach();
        Loaded += (_, _) => ScrollToFocus();
    }

    public GraphsViewModel Vm => (GraphsViewModel)DataContext;

    /// <summary>Shows the measure that was clicked on the main screen.</summary>
    public void ScrollToFocus()
    {
        if (Vm.Focus is not { } focus) return;
        UpdateLayout();
        var index = Vm.Graphs.ToList().FindIndex(g => g.Measure.Key == focus.Key);
        if (index >= 0 && GraphList.ItemContainerGenerator.ContainerFromIndex(index) is FrameworkElement item)
            item.BringIntoView(new Rect(0, 0, item.ActualWidth, item.ActualHeight));
    }

    private void Range_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { DataContext: RangeOption option } radio) radio.IsChecked = option == Vm.Range;
    }

    private void Range_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { DataContext: RangeOption option }) Vm.Range = option;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
