using System.Windows;
using EnvyConverter.Ui;

namespace EnvyConverter;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        ThemeLoader.Apply(Resources);
        base.OnStartup(e);
    }
}
