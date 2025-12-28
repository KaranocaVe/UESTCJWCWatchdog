using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Watchdog.App.ViewModels;
using Watchdog.App.Views;

namespace Watchdog.App;

public class ViewLocator : IDataTemplate
{
    public Control? Build(object? param)
    {
        return param switch
        {
            null => null,
            MainWindowViewModel => new MainWindow(),
            _ => new TextBlock { Text = "Not Found: " + param.GetType().FullName },
        };
    }

    public bool Match(object? data)
    {
        return data is ViewModelBase;
    }
}
