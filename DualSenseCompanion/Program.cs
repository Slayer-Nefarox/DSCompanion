
class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        try
        {
            CrashReporter.Initialize();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"The crash report folder could not be created:\n{ex.Message}",
                "DualSense Companion",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            CrashReporter.Report(
                "Unhandled application-domain exception",
                e.ExceptionObject as Exception);
        Application.ThreadException += (_, e) =>
            CrashReporter.Report("Unhandled Windows Forms thread exception", e.Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            CrashReporter.Report("Unobserved task exception", e.Exception);
            e.SetObserved();
        };

        DependencyManager.CheckAndInstallDependencies();
        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.Run(new MainForm());
    }
}
