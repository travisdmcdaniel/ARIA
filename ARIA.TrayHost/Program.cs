namespace ARIA.TrayHost;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        using var trayApp = new TrayApplication();
        Application.Run(trayApp);
    }
}
