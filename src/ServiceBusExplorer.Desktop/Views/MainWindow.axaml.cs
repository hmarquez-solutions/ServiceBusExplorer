using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using ServiceBusExplorer.Desktop.ViewModels;

namespace ServiceBusExplorer.Desktop.Views;

public partial class MainWindow : Window
{
    MainWindowViewModel? viewModel;

    public MainWindow()
    {
        InitializeComponent();
    }

    // The view model only says which theme it wants; applying it is a view concern.
    protected override void OnDataContextChanged(EventArgs e)
    {
        if (viewModel is not null)
        {
            viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        viewModel = DataContext as MainWindowViewModel;
        if (viewModel is not null)
        {
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
            ApplyTheme(viewModel.IsDarkTheme);
        }

        base.OnDataContextChanged(e);
    }

    void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.IsDarkTheme) && viewModel is not null)
        {
            ApplyTheme(viewModel.IsDarkTheme);
        }
    }

    static void ApplyTheme(bool dark)
    {
        if (Application.Current is { } application)
        {
            application.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        }
    }
}
