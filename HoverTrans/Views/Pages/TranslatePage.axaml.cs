using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Transformation;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using HoverTrans.ViewModels;

namespace HoverTrans.Views.Pages;

/// <summary>
/// The translate page's window plumbing: the clipboard and file pickers, dropping an image on the composer, and the
/// two shortcuts that make the page feel like a tool (Ctrl+Enter translates, Esc cancels).
/// </summary>
public partial class TranslatePage : UserControl
{
    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif"];
    private double _swapAngle;

    public TranslatePage()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AddHandler(DragDrop.DropEvent, OnDrop);

        // The swap button turns half a circle each press: the languages changed places.
        SwapIcon.RenderTransform = TransformOperations.Parse("rotate(0deg)");
        SwapIcon.Transitions = new Transitions
        {
            new TransformOperationsTransition
            {
                Property = Visual.RenderTransformProperty,
                Duration = TimeSpan.FromMilliseconds(260),
                Easing = new CubicEaseOut()
            }
        };

        // The field takes the height the card leaves for it, and keeps it as the window resizes. Arc's text field
        // sizes its inner scroll area from its own height (MaxLines/FrameHeight); leaving that to the layout clips the
        // text instead of scrolling it, and a fixed MaxLines leaves dead space under the field.
        Loaded += (_, _) => FitComposer();
        ComposerHost.SizeChanged += (_, _) => FitComposer();
        Composer.TextChanged += (_, _) => FitComposer();
    }

    /// <summary>Gives the composer exactly the room the card has for it, so long text scrolls and no gap is left.</summary>
    private void FitComposer()
    {
        var height = ComposerHost.Bounds.Height;
        if (height <= 0)
        {
            // First frame: the host is not measured yet, so try again once the layout pass has run.
            Avalonia.Threading.Dispatcher.UIThread.Post(FitComposer, Avalonia.Threading.DispatcherPriority.Loaded);
            return;
        }

        // The frame is the field itself: a hair less than the host keeps a rounding pixel from pushing the action row.
        // NaN means "not sized yet" (the default), and NaN compares false, so it has to be checked explicitly.
        var frame = Math.Max(Composer.MinHeight, Math.Floor(height) - 1);
        if (double.IsNaN(Composer.FrameHeight) || Math.Abs(Composer.FrameHeight - frame) > 0.5)
        {
            Composer.FrameHeight = frame;
        }
    }

    private TranslatePageViewModel? ViewModel => DataContext as TranslatePageViewModel;

    private void OnSwapClick(object? sender, RoutedEventArgs e)
    {
        _swapAngle += 180;
        SwapIcon.RenderTransform = TransformOperations.Parse($"rotate({_swapAngle}deg)");
    }

    private async void OnPasteImageClick(object? sender, RoutedEventArgs e) => await PasteImageAsync();

    private async void OnPickImageClick(object? sender, RoutedEventArgs e)
    {
        var viewModel = ViewModel;
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (viewModel is null || storage is null)
        {
            return;
        }

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择要翻译的图片",
            AllowMultiple = false,
            FileTypeFilter = [FilePickerFileTypes.ImageAll]
        });

        if (files.Count == 0)
        {
            return;
        }

        try
        {
            await using var source = await files[0].OpenReadAsync();
            using var buffer = new MemoryStream();
            await source.CopyToAsync(buffer);
            viewModel.SetImage(buffer.ToArray(), files[0].Name);
        }
        catch (Exception ex)
        {
            viewModel.StatusText = "读取图片失败：" + ex.Message;
        }
    }

    private async Task PasteImageAsync()
    {
        var viewModel = ViewModel;
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (viewModel is null || clipboard is null)
        {
            return;
        }

        if (!await TrySetImageFromClipboardAsync(viewModel, clipboard))
        {
            viewModel.StatusText = "剪贴板里没有图片，先截图或复制一张图片。";
        }
    }

    /// <summary>
    /// Puts the clipboard's picture (a screenshot) or image file (a picture copied in Explorer) into the page.
    /// Returns false when the clipboard holds neither, so text still pastes as text.
    /// </summary>
    private static async Task<bool> TrySetImageFromClipboardAsync(TranslatePageViewModel viewModel, IClipboard clipboard)
    {
        var bitmap = await clipboard.TryGetBitmapAsync();
        if (bitmap is not null)
        {
            using (bitmap)
            {
                using var buffer = new MemoryStream();
                bitmap.Save(buffer, PngBitmapEncoderOptions.Default);
                viewModel.SetImage(buffer.ToArray());
            }

            return true;
        }

        var files = await clipboard.TryGetFilesAsync();
        var image = files?.FirstOrDefault(file => IsImagePath(file.Name) || IsImagePath(file.TryGetLocalPath()));
        if (image?.TryGetLocalPath() is { } path)
        {
            await viewModel.SetImageFileAsync(path);
            return true;
        }

        return false;
    }

    private async void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            if (ViewModel?.TranslateCommand.CanExecute(null) == true)
            {
                ViewModel.TranslateCommand.Execute(null);
                e.Handled = true;
            }

            return;
        }

        if (e.Key == Key.Escape && ViewModel is { IsBusy: true } busy)
        {
            busy.CancelCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.V && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            var viewModel = ViewModel;
            if (clipboard is null || viewModel is null)
            {
                return;
            }

            // Text on the clipboard is left to the text box; only a picture or an image file is taken over.
            if (!await TrySetImageFromClipboardAsync(viewModel, clipboard))
            {
                return;
            }

            e.Handled = true;
        }
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (!HasImageFile(e))
        {
            return;
        }

        e.DragEffects = DragDropEffects.Copy;
        e.Handled = true;
        DropHint.IsVisible = true;
    }

    private void OnDragLeave(object? sender, DragEventArgs e) => DropHint.IsVisible = false;

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        DropHint.IsVisible = false;
        var files = e.DataTransfer.TryGetFiles();
        var image = files?.FirstOrDefault(file => IsImagePath(file.Name) || IsImagePath(file.TryGetLocalPath()));
        if (image?.TryGetLocalPath() is not { } path || ViewModel is not { } viewModel)
        {
            return;
        }

        e.Handled = true;
        await viewModel.SetImageFileAsync(path);
    }

    private static bool HasImageFile(DragEventArgs e) =>
        e.DataTransfer.Contains(DataFormat.File)
        && e.DataTransfer.TryGetFiles()?.Any(file => IsImagePath(file.Name) || IsImagePath(file.TryGetLocalPath())) == true;

    private static bool IsImagePath(string? path) =>
        !string.IsNullOrWhiteSpace(path)
        && ImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
}
