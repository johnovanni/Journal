using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Journal.App.ViewModels;

namespace Journal.App.Views;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel vm)
    {
        DataContext = vm;
        FocusSearchCommand = new RelayCommand(() =>
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
        });
        RunNowCommand = new RelayCommand(() => vm.Search.Run());
        InitializeComponent();
        SizeChanged += (_, _) => FitInspirationToWindow();
    }

    /// <summary>Window height below which the verse and quote show five lines instead of seven.</summary>
    public const double ShortWindowHeight = 760;
    public const double ShortInspirationTextHeight = 100;

    /// <summary>Keeps the editor usable on short windows by showing fewer verse/quote lines before scrolling (ISS-003).</summary>
    private void FitInspirationToWindow()
    {
        var cap = ActualHeight < ShortWindowHeight ? ShortInspirationTextHeight : (double)FindResource("InspirationTextMaxHeight");
        VerseScroller.MaxHeight = cap;
        QuoteScroller.MaxHeight = cap;
    }

    public ICommand FocusSearchCommand { get; }
    public ICommand RunNowCommand { get; }

    private MainViewModel Vm => (MainViewModel)DataContext;

    private void ResultsList_MouseUp(object sender, MouseButtonEventArgs e) => OpenSelectedResult();

    private void ResultsList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) OpenSelectedResult();
    }

    private void OpenSelectedResult()
    {
        if (ResultsList.SelectedItem is SearchHitViewModel hit) Vm.Search.OpenResultCommand.Execute(hit);
    }

    /// <summary>
    /// The selectable text boxes inside the verse and quote scrollers would otherwise swallow the mouse wheel,
    /// so the wheel scrolls the capped text area directly.
    /// </summary>
    private void InspirationScroller_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer scroller || scroller.ScrollableHeight <= 0) return;
        scroller.ScrollToVerticalOffset(scroller.VerticalOffset - e.Delta / 3.0);
        e.Handled = true;
    }

    private void TabHeader_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2 && sender is FrameworkElement { DataContext: EntryTabViewModel tab })
        {
            tab.Owner.RenameTabCommand.Execute(tab);
            e.Handled = true;
        }
    }
}
