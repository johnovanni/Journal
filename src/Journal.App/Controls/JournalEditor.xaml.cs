using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Journal.App.Editor;
using Journal.App.Services;
using Journal.App.ViewModels;
using Journal.Core.Documents;

namespace Journal.App.Controls;

/// <summary>
/// Rich-text editor for one tab: formatting toolbar, images and files between paragraphs, checklists.
/// Bound to an <see cref="EntryTabViewModel"/> through DataContext.
/// </summary>
public partial class JournalEditor : UserControl, IDocumentEditorHost
{
    private static readonly FlowDocumentMapper Mapper = new();
    private static readonly double[] Sizes = [8, 9, 10, 11, 12, 14, 16, 18, 20, 24, 28, 32, 36, 48, 60, 72];
    private static readonly Color[] Palette =
    [
        Color.FromRgb(0x1E, 0x24, 0x30), Color.FromRgb(0x64, 0x6C, 0x7A), Color.FromRgb(0xB4, 0x23, 0x18), Color.FromRgb(0xC2, 0x41, 0x0C),
        Color.FromRgb(0xB7, 0x79, 0x1F), Color.FromRgb(0x4D, 0x7C, 0x0F), Color.FromRgb(0x1F, 0x6F, 0x78), Color.FromRgb(0x1D, 0x4E, 0xD8),
        Color.FromRgb(0x6D, 0x28, 0xD9), Color.FromRgb(0xBE, 0x18, 0x5D), Color.FromRgb(0x78, 0x35, 0x0F), Color.FromRgb(0x0F, 0x76, 0x6E),
        Color.FromRgb(0x37, 0x41, 0x51), Color.FromRgb(0x9C, 0xA3, 0xAF), Color.FromRgb(0x03, 0x69, 0xA1), Color.FromRgb(0x00, 0x00, 0x00),
    ];
    private static readonly Color[] HighlightPalette =
    [
        Color.FromRgb(0xFF, 0xF1, 0xB8), Color.FromRgb(0xFD, 0xE6, 0x8A), Color.FromRgb(0xD9, 0xF9, 0x9D), Color.FromRgb(0xBB, 0xF7, 0xD0),
        Color.FromRgb(0xA5, 0xF3, 0xFC), Color.FromRgb(0xBF, 0xDB, 0xFE), Color.FromRgb(0xDD, 0xD6, 0xFE), Color.FromRgb(0xFB, 0xCF, 0xE8),
        Color.FromRgb(0xFE, 0xCA, 0xCA), Color.FromRgb(0xFE, 0xD7, 0xAA), Color.FromRgb(0xE5, 0xE7, 0xEB), Color.FromRgb(0xD6, 0xEC, 0xEE),
        Color.FromRgb(0xF5, 0xF5, 0xF4), Color.FromRgb(0xFE, 0xF0, 0x8A), Color.FromRgb(0xC7, 0xD2, 0xFE), Color.FromRgb(0xF0, 0xAB, 0xFC),
    ];

    private EntryTabViewModel? _tab;
    private bool _loading;
    private bool _syncing;

    static JournalEditor()
    {
        // Checkbox clicks inside any editor document tick or untick a checklist item.
        EventManager.RegisterClassHandler(typeof(CheckBox), ButtonBase.ClickEvent, new RoutedEventHandler(OnAnyCheckBoxClick));
    }

    public JournalEditor()
    {
        InitializeComponent();
        FontBox.ItemsSource = Fonts.SystemFontFamilies.Select(f => f.Source).OrderBy(n => n).ToList();
        SizeBox.ItemsSource = Sizes;
        BuildSwatches(TextSwatches, Palette, c => ApplyColor(TextElement.ForegroundProperty, c));
        BuildSwatches(HighlightSwatches, HighlightPalette, c => ApplyColor(TextElement.BackgroundProperty, c));

        Box.TextChanged += (_, _) => { if (!_loading) _tab?.MarkDirty(); };
        Box.SelectionChanged += (_, _) => SyncToolbar();
        Box.PreviewKeyDown += Box_PreviewKeyDown;
        Box.PreviewDragOver += Box_PreviewDragOver;
        Box.PreviewDrop += Box_PreviewDrop;
        // Copy, cut, paste and drag keep checklists, images, files and headings (ISS-010).
        JournalClipboard.Attach(Box, () => _tab is not null && !Box.IsReadOnly, () => _tab?.MarkDirty());
        DataObject.AddPastingHandler(Box, Box_Pasting);
        DataContextChanged += (_, e) => Bind(e.OldValue as EntryTabViewModel, e.NewValue as EntryTabViewModel);
        Loaded += (_, _) => Subscribe();
        Unloaded += (_, _) => Unsubscribe();
    }

    /// <summary>The RichTextBox (exposed for tests).</summary>
    public RichTextBox TextBox => Box;

    // ------------------------------------------------------------------ binding to the tab view model

    private void Bind(EntryTabViewModel? oldTab, EntryTabViewModel? newTab)
    {
        if (oldTab is not null && ReferenceEquals(oldTab.Host, this))
        {
            oldTab.Capture();
            oldTab.Host = null;
        }
        _tab = newTab;
        _loading = true;
        Box.IsUndoEnabled = false;
        Box.Document = newTab is null ? FlowDocumentMapper.CreateEmptyDocument() : Mapper.ToFlowDocument(newTab.Document);
        Box.IsUndoEnabled = true;
        Box.IsReadOnly = newTab?.IsReadOnly ?? true;
        Toolbar.IsEnabled = !Box.IsReadOnly;
        if (newTab is not null) newTab.Host = this;
        // Show the font and size at the caret straight away, not only after the selection changes (FR-EDT-10).
        SyncToolbar();
        // TextChanged for the new document can arrive after this method returns.
        Dispatcher.BeginInvoke(() => _loading = false, DispatcherPriority.ContextIdle);
    }

    public JournalDocument Capture() => Mapper.FromFlowDocument(Box.Document);

    public void InsertImage(ImageBlock block) =>
        EditorOperations.InsertBlockAtCaret(Box, FlowDocumentMapper.ImageContainer(block));

    public void InsertAttachment(AttachmentBlock block) =>
        EditorOperations.InsertBlockAtCaret(Box, FlowDocumentMapper.AttachmentContainer(block));

    private void Subscribe()
    {
        BlockEvents.ContentEdited += OnBlockEdited;
        BlockEvents.AttachmentActionRequested += OnAttachmentAction;
        BlockEvents.ImageActionRequested += OnImageAction;
    }

    private void Unsubscribe()
    {
        BlockEvents.ContentEdited -= OnBlockEdited;
        BlockEvents.AttachmentActionRequested -= OnAttachmentAction;
        BlockEvents.ImageActionRequested -= OnImageAction;
    }

    private bool Owns(object? sender) => sender is DependencyObject d && EditorOperations.IsInDocument(d, Box.Document);

    private void OnBlockEdited(object? sender, EventArgs e)
    {
        if (!_loading && Owns(sender)) _tab?.MarkDirty();
    }

    private async void OnAttachmentAction(object? sender, AttachmentAction action)
    {
        if (_tab is null || sender is not AttachmentChip chip || !Owns(chip)) return;
        switch (action)
        {
            case AttachmentAction.Open: await _tab.OpenAttachmentAsync(chip.ToBlock()); break;
            case AttachmentAction.SaveAs: await _tab.SaveAttachmentAsAsync(chip.ToBlock()); break;
            case AttachmentAction.Remove when !Box.IsReadOnly:
                if (EditorOperations.RemoveHostedBlock(chip)) _tab.MarkDirty();
                break;
        }
    }

    private async void OnImageAction(object? sender, ImageAction action)
    {
        if (_tab is null || sender is not ImageBlockView view || !Owns(view)) return;
        switch (action)
        {
            case ImageAction.View: await _tab.ViewImageAsync(view.ToBlock()); break;
            case ImageAction.Remove when !Box.IsReadOnly:
                if (EditorOperations.RemoveHostedBlock(view)) _tab.MarkDirty();
                break;
        }
    }

    private static void OnAnyCheckBoxClick(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox cb || cb.Parent is not InlineUIContainer { Parent: Paragraph p }) return;
        EditorOperations.ApplyCheckedDecoration(p, cb.IsChecked == true);
        BlockEvents.RaiseEdited(cb);
    }

    // ------------------------------------------------------------------ keyboard

    private void Box_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Box.IsReadOnly) return;
        var mods = Keyboard.Modifiers;
        switch (e.Key)
        {
            case Key.X when mods == (ModifierKeys.Control | ModifierKeys.Shift):
                Strike_Click(this, e);
                e.Handled = true;
                break;
            case Key.L when mods == (ModifierKeys.Control | ModifierKeys.Shift):
                Checklist_Click(this, e);
                e.Handled = true;
                break;
            case Key.Enter when mods == ModifierKeys.Control:
                if (EditorOperations.ToggleChecked(Box))
                {
                    _tab?.MarkDirty();
                    e.Handled = true;
                }
                break;
            case Key.Tab when mods is ModifierKeys.None or ModifierKeys.Shift:
                if (EditorOperations.Indent(Box, mods == ModifierKeys.Shift ? -1 : 1))
                {
                    _tab?.MarkDirty();
                    e.Handled = true;
                }
                break;
            case Key.Enter when mods == ModifierKeys.None:
                if (EditorOperations.IsInEmptyChecklistItem(Box))
                {
                    EditorOperations.RemoveCheckbox(Box.CaretPosition.Paragraph!);
                    _tab?.MarkDirty();
                    e.Handled = true;
                    break;
                }
                var previous = Box.CaretPosition.Paragraph;
                Dispatcher.BeginInvoke(() => EditorOperations.AfterEnter(Box, previous), DispatcherPriority.Input);
                break;
        }
    }

    // ------------------------------------------------------------------ paste and drop

    private void Box_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        if (_tab is null || Box.IsReadOnly) return;
        var data = e.DataObject;
        if (data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] files)
        {
            e.CancelCommand();
            _ = _tab.AddDroppedFilesAsync(files);
            return;
        }
        bool hasText = data.GetDataPresent(DataFormats.Rtf) || data.GetDataPresent(DataFormats.Xaml) ||
                       data.GetDataPresent(DataFormats.UnicodeText) || data.GetDataPresent(DataFormats.Text);
        if (!hasText && data.GetDataPresent(DataFormats.Bitmap) && Clipboard.GetImage() is { } bmp)
        {
            e.CancelCommand();
            _ = _tab.AddPastedImageAsync(ImageProbe.EncodePng(bmp));
        }
    }

    private void Box_PreviewDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop) && !Box.IsReadOnly)
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    private async void Box_PreviewDrop(object sender, DragEventArgs e)
    {
        if (_tab is null || Box.IsReadOnly || e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;
        e.Handled = true;
        if (Box.GetPositionFromPoint(e.GetPosition(Box), snapToText: true) is { } pos) Box.CaretPosition = pos;
        await _tab.AddDroppedFilesAsync(files);
    }

    // ------------------------------------------------------------------ toolbar

    private void SyncToolbar()
    {
        _syncing = true;
        try
        {
            var f = EditorOperations.ReadFormatting(Box);
            BoldButton.IsChecked = f.IsBold;
            ItalicButton.IsChecked = f.IsItalic;
            UnderlineButton.IsChecked = f.IsUnderline;
            StrikeButton.IsChecked = f.IsStrikethrough;
            BulletButton.IsChecked = f.List == ListKind.Bulleted;
            NumberButton.IsChecked = f.List == ListKind.Numbered;
            ChecklistButton.IsChecked = f.List == ListKind.Checklist;
            FontBox.SelectedItem = f.FontFamily;
            SizeBox.Text = f.FontSizePt?.ToString(CultureInfo.CurrentCulture) ?? "";
            if (f.Foreground is { } c) ColorSwatch.Background = new SolidColorBrush(c);
        }
        finally { _syncing = false; }
    }

    private void AfterFormat()
    {
        _tab?.MarkDirty();
        SyncToolbar();
        Box.Focus();
    }

    private void Bold_Click(object sender, RoutedEventArgs e) { EditingCommands.ToggleBold.Execute(null, Box); AfterFormat(); }
    private void Italic_Click(object sender, RoutedEventArgs e) { EditingCommands.ToggleItalic.Execute(null, Box); AfterFormat(); }
    private void Underline_Click(object sender, RoutedEventArgs e) { EditorOperations.ToggleTextDecoration(Box, TextDecorationLocation.Underline); AfterFormat(); }
    private void Strike_Click(object sender, RoutedEventArgs e) { EditorOperations.ToggleTextDecoration(Box, TextDecorationLocation.Strikethrough); AfterFormat(); }
    private void H1_Click(object sender, RoutedEventArgs e) { EditorOperations.SetParagraphStyle(Box, ParagraphStyle.Heading1); AfterFormat(); }
    private void H2_Click(object sender, RoutedEventArgs e) { EditorOperations.SetParagraphStyle(Box, ParagraphStyle.Heading2); AfterFormat(); }
    private void Normal_Click(object sender, RoutedEventArgs e) { EditorOperations.SetParagraphStyle(Box, ParagraphStyle.Normal); AfterFormat(); }
    private void Bullets_Click(object sender, RoutedEventArgs e) { EditingCommands.ToggleBullets.Execute(null, Box); AfterFormat(); }
    private void Numbers_Click(object sender, RoutedEventArgs e) { EditingCommands.ToggleNumbering.Execute(null, Box); AfterFormat(); }
    private void Checklist_Click(object sender, RoutedEventArgs e) { EditorOperations.ToggleChecklist(Box); AfterFormat(); }
    private void InsertImage_Click(object sender, RoutedEventArgs e) => _tab?.InsertImageCommand.Execute(null);
    private void AttachFile_Click(object sender, RoutedEventArgs e) => _tab?.AttachFileCommand.Execute(null);

    private void FontBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || FontBox.SelectedItem is not string name) return;
        Box.Selection.ApplyPropertyValue(TextElement.FontFamilyProperty, new FontFamily(name));
        AfterFormat();
    }

    private void SizeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || SizeBox.SelectedItem is not double size) return;
        ApplySize(size);
    }

    private void SizeBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) SizeBox_Commit(sender, e);
    }

    private void SizeBox_Commit(object sender, RoutedEventArgs e)
    {
        if (_syncing || !double.TryParse(SizeBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var size)) return;
        ApplySize(size);
    }

    private void ApplySize(double points)
    {
        points = ClampFontSize(points);
        Box.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, FlowDocumentMapper.PtToDip(points));
        AfterFormat();
    }

    /// <summary>Font sizes are limited to 8–72 points (FR-EDT-04).</summary>
    public static double ClampFontSize(double points) => Math.Clamp(points, 8, 72);

    private void ApplyColor(DependencyProperty property, Color? color)
    {
        EditorOperations.ApplyColor(Box, property, color);
        if (property == TextElement.ForegroundProperty)
        {
            ColorButton.IsChecked = false;
            if (color is { } c) ColorSwatch.Background = new SolidColorBrush(c);
        }
        else
        {
            HighlightButton.IsChecked = false;
            if (color is { } c) HighlightSwatch.Background = new SolidColorBrush(c);
        }
        AfterFormat();
    }

    private static void BuildSwatches(Panel panel, IEnumerable<Color> colors, Action<Color> apply)
    {
        foreach (var c in colors)
        {
            var b = new Button
            {
                Width = 20, Height = 20, Margin = new Thickness(0, 0, 4, 4), Padding = new Thickness(0),
                Background = new SolidColorBrush(c), BorderBrush = Brushes.Transparent,
                ToolTip = $"#{c.R:X2}{c.G:X2}{c.B:X2}",
            };
            AutomationPropertiesHelper.SetName(b, $"Color #{c.R:X2}{c.G:X2}{c.B:X2}");
            b.Click += (_, _) => apply(c);
            panel.Children.Add(b);
        }
    }

    private void CustomTextColor_Click(object sender, RoutedEventArgs e)
    {
        if (Core.Documents.ArgbColor.TryParse(TextHex.Text, out var c)) ApplyColor(TextElement.ForegroundProperty, Color.FromArgb(c.A, c.R, c.G, c.B));
    }

    private void CustomHighlight_Click(object sender, RoutedEventArgs e)
    {
        if (Core.Documents.ArgbColor.TryParse(HighlightHex.Text, out var c)) ApplyColor(TextElement.BackgroundProperty, Color.FromArgb(c.A, c.R, c.G, c.B));
    }

    private void DefaultTextColor_Click(object sender, RoutedEventArgs e) => ApplyColor(TextElement.ForegroundProperty, null);
    private void NoHighlight_Click(object sender, RoutedEventArgs e) => ApplyColor(TextElement.BackgroundProperty, null);

    private void Popup_Closed(object? sender, EventArgs e)
    {
        if (sender is Popup { Tag: ToggleButton t }) t.IsChecked = false;
    }
}

internal static class AutomationPropertiesHelper
{
    public static void SetName(DependencyObject d, string name) =>
        System.Windows.Automation.AutomationProperties.SetName(d, name);
}
