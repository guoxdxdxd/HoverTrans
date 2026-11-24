using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using HoverTrans.ViewModels;

namespace HoverTrans.Views;

/// <summary>The app's content: input, result and the clipboard/file image plumbing.</summary>
public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private async void OnPasteImageClick(object? sender, RoutedEventArgs e) =>
        await PasteImageAsync();

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

    private void OnRemoveImageClick(object? sender, RoutedEventArgs e) => ViewModel?.ClearImage();

    private async void OnCopyClick(object? sender, RoutedEventArgs e)
    {
        var viewModel = ViewModel;
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (viewModel is null || clipboard is null || string.IsNullOrWhiteSpace(viewModel.ResultText))
        {
            return;
        }

        await clipboard.SetTextAsync(viewModel.ResultText);
        viewModel.StatusText = "译文已复制到剪贴板。";
    }

    private async void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.V || !e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            return;
        }

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
        {
            return;
        }

        var bitmap = await clipboard.TryGetBitmapAsync();
        if (bitmap is null)
        {
            return;
        }

        // The clipboard held an image, not text: use it instead of letting the TextBox paste nothing.
        e.Handled = true;
        using (bitmap)
        {
            using var buffer = new MemoryStream();
            bitmap.Save(buffer, PngBitmapEncoderOptions.Default);
            ViewModel?.SetImage(buffer.ToArray());
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

        var bitmap = await clipboard.TryGetBitmapAsync();
        if (bitmap is null)
        {
            viewModel.StatusText = "剪贴板里没有图片，先截图或复制一张图片。";
            return;
        }

        using (bitmap)
        {
            using var buffer = new MemoryStream();
            bitmap.Save(buffer, PngBitmapEncoderOptions.Default);
            viewModel.SetImage(buffer.ToArray());
        }
    }
}