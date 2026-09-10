using System.Windows;

namespace TaskBoard.Windows;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var smokeIndex = Array.IndexOf(e.Args, "--smoke-output");
        var smokeOutput = smokeIndex >= 0 && smokeIndex + 1 < e.Args.Length ? e.Args[smokeIndex + 1] : null;
        var window = new MainWindow(smokeOutput, e.Args.Contains("--smoke-online"));
        MainWindow = window;
        window.Show();
    }
}
