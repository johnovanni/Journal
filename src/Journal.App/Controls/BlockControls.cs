using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Journal.Core.Documents;

namespace Journal.App.Controls;

public enum AttachmentAction { Open, SaveAs, EditDescription, Remove }

public enum ImageAction { View, Small, Medium, Full, Remove }

/// <summary>
/// Raised by embedded block controls when their content changes. Editors check whether the sender is in
/// their own document. Static because WPF re-creates embedded controls from XAML on undo and paste.
/// </summary>
public static class BlockEvents
{
    public static event EventHandler? ContentEdited;
    public static event EventHandler<AttachmentAction>? AttachmentActionRequested;
    public static event EventHandler<ImageAction>? ImageActionRequested;

    internal static void RaiseEdited(object sender) => ContentEdited?.Invoke(sender, EventArgs.Empty);
    internal static void RaiseAttachment(object sender, AttachmentAction a) => AttachmentActionRequested?.Invoke(sender, a);
    internal static void RaiseImage(object sender, ImageAction a) => ImageActionRequested?.Invoke(sender, a);
}

/// <summary>Loads image bitmaps for <see cref="ImageBlockView"/> (set by the app after unlock, cleared on lock).</summary>
public delegate Task<ImageSource?> ImageLoader(BlobRef blob, int decodePixelWidth);

/// <summary>
/// An image with its caption, hosted in a BlockUIContainer between paragraphs. All state is in plain
/// dependency properties so WPF can re-create it from XAML (undo, copy/paste).
/// </summary>
[TemplatePart(Name = "PART_Image", Type = typeof(Image))]
public class ImageBlockView : Control
{
    public static ImageLoader? Loader { get; set; }

    public const double SmallWidth = 240, MediumWidth = 480;
    public const int MaxDecodeWidth = 1600;

    static ImageBlockView()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(ImageBlockView), new FrameworkPropertyMetadata(typeof(ImageBlockView)));
    }

    public static readonly DependencyProperty BlockIdProperty = Register(nameof(BlockId), "");
    public static readonly DependencyProperty BlobIdProperty = Register(nameof(BlobId), "");
    public static readonly DependencyProperty BlobShaProperty = Register(nameof(BlobSha), "");
    public static readonly DependencyProperty CaptionProperty = DependencyProperty.Register(nameof(Caption), typeof(string), typeof(ImageBlockView),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => BlockEvents.RaiseEdited(d)));
    public static readonly DependencyProperty DisplayWidthProperty = DependencyProperty.Register(nameof(DisplayWidth), typeof(double), typeof(ImageBlockView),
        new PropertyMetadata(double.NaN, (d, _) => ((ImageBlockView)d).UpdateImageWidth()));
    public static readonly DependencyProperty PixelWidthProperty = DependencyProperty.Register(nameof(PixelWidth), typeof(int), typeof(ImageBlockView), new PropertyMetadata(0));
    public static readonly DependencyProperty PixelHeightProperty = DependencyProperty.Register(nameof(PixelHeight), typeof(int), typeof(ImageBlockView), new PropertyMetadata(0));

    private static DependencyProperty Register(string name, string def) =>
        DependencyProperty.Register(name, typeof(string), typeof(ImageBlockView), new PropertyMetadata(def));

    public string BlockId { get => (string)GetValue(BlockIdProperty); set => SetValue(BlockIdProperty, value); }
    public string BlobId { get => (string)GetValue(BlobIdProperty); set => SetValue(BlobIdProperty, value); }
    public string BlobSha { get => (string)GetValue(BlobShaProperty); set => SetValue(BlobShaProperty, value); }
    public string Caption { get => (string)GetValue(CaptionProperty); set => SetValue(CaptionProperty, value); }
    public double DisplayWidth { get => (double)GetValue(DisplayWidthProperty); set => SetValue(DisplayWidthProperty, value); }
    public int PixelWidth { get => (int)GetValue(PixelWidthProperty); set => SetValue(PixelWidthProperty, value); }
    public int PixelHeight { get => (int)GetValue(PixelHeightProperty); set => SetValue(PixelHeightProperty, value); }

    private Image? _image;

    public ImageBlockView()
    {
        Loaded += async (_, _) => await LoadImageAsync();
        MouseDoubleClick += (_, e) =>
        {
            if (e.OriginalSource is not TextBox) BlockEvents.RaiseImage(this, ImageAction.View);
        };
        // The menu is opened manually rather than stored in ContextMenu, because WPF serializes local
        // property values to XAML when the editor copies or undoes embedded controls.
        MouseRightButtonUp += (_, e) =>
        {
            if (e.OriginalSource is TextBox) return;
            var menu = BuildMenu();
            menu.PlacementTarget = this;
            menu.IsOpen = true;
            e.Handled = true;
        };
    }

    public static ImageBlockView From(ImageBlock block) => new()
    {
        BlockId = block.Id.ToString(),
        BlobId = block.Image.Id.ToString(),
        BlobSha = block.Image.Sha256,
        Caption = block.Caption,
        DisplayWidth = block.DisplayWidth ?? double.NaN,
        PixelWidth = block.PixelWidth,
        PixelHeight = block.PixelHeight,
    };

    public ImageBlock ToBlock() => new()
    {
        Id = Guid.TryParse(BlockId, out var id) ? id : Guid.NewGuid(),
        Image = new BlobRef(Guid.TryParse(BlobId, out var b) ? b : Guid.Empty, BlobSha),
        Caption = Caption ?? "",
        DisplayWidth = double.IsNaN(DisplayWidth) ? null : DisplayWidth,
        PixelWidth = PixelWidth,
        PixelHeight = PixelHeight,
    };

    /// <summary>The image currently shown (null until loaded).</summary>
    public ImageSource? LoadedSource => _image?.Source;

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _image = GetTemplateChild("PART_Image") as Image;
        UpdateImageWidth();
    }

    private void UpdateImageWidth()
    {
        if (_image is null) return;
        _image.MaxWidth = double.IsNaN(DisplayWidth) ? double.PositiveInfinity : DisplayWidth;
        if (PixelWidth > 0) _image.MaxWidth = Math.Min(_image.MaxWidth, PixelWidth);
    }

    private async Task LoadImageAsync()
    {
        if (_image is null || _image.Source is not null || Loader is null) return;
        if (!Guid.TryParse(BlobId, out var blobId)) return;
        try
        {
            int decode = PixelWidth > 0 ? Math.Min(PixelWidth, MaxDecodeWidth) : MaxDecodeWidth;
            _image.Source = await Loader(new BlobRef(blobId, BlobSha), decode);
        }
        catch (Exception)
        {
            _image.Source = null; // the template shows "Image unavailable"
        }
    }

    private ContextMenu BuildMenu()
    {
        var menu = new ContextMenu();
        void Add(string header, ImageAction action) =>
            menu.Items.Add(new MenuItem { Header = header, Command = new SimpleCommand(() => Apply(action)) });
        Add("View full size", ImageAction.View);
        menu.Items.Add(new Separator());
        Add("Small", ImageAction.Small);
        Add("Medium", ImageAction.Medium);
        Add("Full width", ImageAction.Full);
        menu.Items.Add(new Separator());
        Add("Remove image", ImageAction.Remove);
        return menu;
    }

    internal void Apply(ImageAction action)
    {
        switch (action)
        {
            case ImageAction.Small: DisplayWidth = SmallWidth; BlockEvents.RaiseEdited(this); break;
            case ImageAction.Medium: DisplayWidth = MediumWidth; BlockEvents.RaiseEdited(this); break;
            case ImageAction.Full: DisplayWidth = double.NaN; BlockEvents.RaiseEdited(this); break;
            default: BlockEvents.RaiseImage(this, action); break;
        }
    }
}

/// <summary>An attached data file shown as a chip with name, size and an editable description.</summary>
public class AttachmentChip : Control
{
    static AttachmentChip()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(AttachmentChip), new FrameworkPropertyMetadata(typeof(AttachmentChip)));
    }

    public static readonly DependencyProperty BlockIdProperty = Register(nameof(BlockId));
    public static readonly DependencyProperty BlobIdProperty = Register(nameof(BlobId));
    public static readonly DependencyProperty BlobShaProperty = Register(nameof(BlobSha));
    public static readonly DependencyProperty FileNameProperty = Register(nameof(FileName));
    public static readonly DependencyProperty ContentTypeProperty = Register(nameof(ContentType));
    public static readonly DependencyProperty SizeBytesProperty = DependencyProperty.Register(nameof(SizeBytes), typeof(long), typeof(AttachmentChip),
        new PropertyMetadata(0L, (d, _) => ((AttachmentChip)d).SizeText = AttachmentBlock.FormatSize(((AttachmentChip)d).SizeBytes)));
    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(nameof(Description), typeof(string), typeof(AttachmentChip),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => BlockEvents.RaiseEdited(d)));
    private static readonly DependencyPropertyKey SizeTextKey = DependencyProperty.RegisterReadOnly(nameof(SizeText), typeof(string), typeof(AttachmentChip), new PropertyMetadata("0 B"));
    public static readonly DependencyProperty SizeTextProperty = SizeTextKey.DependencyProperty;

    private static DependencyProperty Register(string name) =>
        DependencyProperty.Register(name, typeof(string), typeof(AttachmentChip), new PropertyMetadata(""));

    public string BlockId { get => (string)GetValue(BlockIdProperty); set => SetValue(BlockIdProperty, value); }
    public string BlobId { get => (string)GetValue(BlobIdProperty); set => SetValue(BlobIdProperty, value); }
    public string BlobSha { get => (string)GetValue(BlobShaProperty); set => SetValue(BlobShaProperty, value); }
    public string FileName { get => (string)GetValue(FileNameProperty); set => SetValue(FileNameProperty, value); }
    public string ContentType { get => (string)GetValue(ContentTypeProperty); set => SetValue(ContentTypeProperty, value); }
    public long SizeBytes { get => (long)GetValue(SizeBytesProperty); set => SetValue(SizeBytesProperty, value); }
    public string Description { get => (string)GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }
    public string SizeText { get => (string)GetValue(SizeTextProperty); private set => SetValue(SizeTextKey, value); }

    public AttachmentChip()
    {
        MouseDoubleClick += (_, e) =>
        {
            if (e.OriginalSource is not TextBox) BlockEvents.RaiseAttachment(this, AttachmentAction.Open);
        };
        MouseRightButtonUp += (_, e) =>
        {
            if (e.OriginalSource is TextBox) return;
            var menu = new ContextMenu { PlacementTarget = this };
            void Add(string header, AttachmentAction a) =>
                menu.Items.Add(new MenuItem { Header = header, Command = new SimpleCommand(() => Request(a)) });
            Add("Open", AttachmentAction.Open);
            Add("Save As…", AttachmentAction.SaveAs);
            Add("Edit description", AttachmentAction.EditDescription);
            menu.Items.Add(new Separator());
            Add("Remove", AttachmentAction.Remove);
            menu.IsOpen = true;
            e.Handled = true;
        };
    }

    internal void Request(AttachmentAction action)
    {
        if (action == AttachmentAction.EditDescription && GetTemplateChild("PART_Description") is TextBox box)
        {
            box.Focus();
            box.SelectAll();
            return;
        }
        BlockEvents.RaiseAttachment(this, action);
    }

    public static AttachmentChip From(AttachmentBlock block) => new()
    {
        BlockId = block.Id.ToString(),
        BlobId = block.File.Id.ToString(),
        BlobSha = block.File.Sha256,
        FileName = block.FileName,
        ContentType = block.ContentType,
        SizeBytes = block.SizeBytes,
        Description = block.Description,
    };

    public AttachmentBlock ToBlock() => new()
    {
        Id = Guid.TryParse(BlockId, out var id) ? id : Guid.NewGuid(),
        File = new BlobRef(Guid.TryParse(BlobId, out var b) ? b : Guid.Empty, BlobSha),
        FileName = FileName,
        ContentType = string.IsNullOrEmpty(ContentType) ? "application/octet-stream" : ContentType,
        SizeBytes = SizeBytes,
        Description = Description ?? "",
    };
}

/// <summary>Minimal ICommand for code-built menus.</summary>
public sealed class SimpleCommand(Action execute) : ICommand
{
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => execute();
}
