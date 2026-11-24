using Avalonia.Controls;
using HoverTrans.Services;
using HoverTrans.ViewModels;

namespace HoverTrans.Views;

/// <summary>Topmost, non-activating translation popup shown next to the cursor.</summary>
public partial class HoverWindow : Window
{
    public HoverWindow()
    {
        InitializeComponent();
        DataContext = new HoverViewModel();
    }

    public void Apply(HoverSnapshot snapshot)
    {
        if (DataContext is HoverViewModel viewModel)
        {
            viewModel.Apply(snapshot);
        }
    }
}