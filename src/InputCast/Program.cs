namespace InputCast;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        using var mutex = new Mutex(true, @"Local\InputCast-6c1f0e7a-4b0e-4d8e-9a55-1d2f3b7e9c11", out bool firstInstance);
        if (!firstInstance)
        {
            MessageBox.Show("InputCast is already running - look for its icon in the system tray.", "InputCast",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => ReportCrash(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => ReportCrash(e.ExceptionObject as Exception);

        // The tray app is built before the message loop starts; make sure UI marshaling is available.
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());

        bool demo = args.Any(a => a.Equals("--demo", StringComparison.OrdinalIgnoreCase));
        Application.Run(new TrayApp(demo));
    }

    private static void ReportCrash(Exception? ex)
    {
        string text = ex?.ToString() ?? "Unknown error";
        try
        {
            Directory.CreateDirectory(Settings.Directory);
            File.AppendAllText(Path.Combine(Settings.Directory, "error.log"), $"[{DateTime.Now:u}] {text}\n\n");
        }
        catch { }
        MessageBox.Show("InputCast hit an unexpected error:\n\n" + ex?.Message +
                        $"\n\nDetails were written to {Path.Combine(Settings.Directory, "error.log")}",
            "InputCast", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
