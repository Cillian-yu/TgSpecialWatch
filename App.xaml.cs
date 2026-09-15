using System.Windows;
using TgSpecialWatch.Services;

namespace TgSpecialWatch;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        AppServices.Instance.Initialize();
    }
}
