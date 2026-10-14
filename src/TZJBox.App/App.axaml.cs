using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using TZJBox.App.Services;
using TZJBox.App.ViewModels;
using TZJBox.App.Views;

namespace TZJBox.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            var viewModel = new MainWindowViewModel(new ExifCapturedTimeReader(), new WindowDialogs(() => window));
            window.DataContext = viewModel;
            desktop.MainWindow = window;

            if (desktop.Args is { Length: > 0 } args && Directory.Exists(args[0]))
            {
                viewModel.FolderPath = args[0];
                _ = viewModel.LoadFolderCommand.ExecuteAsync(null);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
