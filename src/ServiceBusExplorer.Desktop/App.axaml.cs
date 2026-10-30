using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ServiceBusExplorer.Desktop.Services;
using ServiceBusExplorer.Desktop.ViewModels;
using ServiceBusExplorer.Desktop.Views;

namespace ServiceBusExplorer.Desktop;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(target => new ServiceBusNamespaceClient(target))
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
