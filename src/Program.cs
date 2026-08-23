using System.Threading;

namespace SlimMonitorPC;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var selfTest = args.Any(arg => string.Equals(arg, "--self-test", StringComparison.OrdinalIgnoreCase));

        using var mutex = new Mutex(initiallyOwned: true, @"Local\SlimMonitorPC", out var createdNew);
        if (!createdNew)
        {
            if (!selfTest)
            {
                MessageBox.Show(
                    "Slim Monitor PC ya se está ejecutando. Cierra la instancia anterior antes de abrir esta versión.",
                    "Slim Monitor PC",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            return;
        }

        try
        {
            ApplicationConfiguration.Initialize();

            if (selfTest)
            {
                RunSelfTest();
                return;
            }

            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (_, e) => ReportFatal(e.Exception, showDialog: true);

            using var form = new TaskbarWidgetForm();
            using var dock = new TaskbarDockController(form);
            Application.Run(form);
        }
        catch (Exception ex)
        {
            ReportFatal(ex, showDialog: !selfTest);
            if (selfTest)
                Environment.ExitCode = 1;
        }
    }

    private static void RunSelfTest()
    {
        using var form = new TaskbarWidgetForm();
        using var dock = new TaskbarDockController(form);
        _ = form.Handle;

        form.Size = new Size(158, 45);
        using var bitmap = new Bitmap(form.ClientSize.Width, form.ClientSize.Height);
        form.DrawToBitmap(bitmap, form.ClientRectangle);

        if (bitmap.Width <= 0 || bitmap.Height <= 0)
            throw new InvalidOperationException("La superficie del widget no pudo inicializarse.");
    }

    private static void ReportFatal(Exception exception, bool showDialog)
    {
        try
        {
            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "IMC93Labs",
                "SlimMonitorPC");
            Directory.CreateDirectory(folder);
            File.WriteAllText(
                Path.Combine(folder, "startup-error.log"),
                $"{DateTime.Now:O}\r\n{exception}");
        }
        catch
        {
            // Logging must never hide the original startup failure.
        }

        if (!showDialog)
            return;

        MessageBox.Show(
            "Slim Monitor PC no ha podido iniciarse. Se ha guardado un registro en %LOCALAPPDATA%\\IMC93Labs\\SlimMonitorPC\\startup-error.log.\r\n\r\n" + exception.Message,
            "Slim Monitor PC - Error",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }
}
