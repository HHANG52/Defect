using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using DefectVision.UI.Views;

namespace DefectVision.UI
{
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
                desktop.MainWindow = new MainWindow();
                desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnMainWindowClose;
                desktop.ShutdownRequested += (s, e) =>
                {
                    System.Diagnostics.Process.GetCurrentProcess().Kill();
                };
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}