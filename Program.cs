using System.Threading;
using System.Windows.Forms;

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
                    "Slim Monitor PC ya se está ejecutando. Si no lo ves, comprueba la instancia anterior en el Administrador de tareas y ciérrala antes de abrir esta versión.",
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
                using var form = new TaskbarEmbeddedForm();
                _ = form.Handle;
                return;
            }

            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (_, e) => ReportFatal(e.Exception, showDialog: true);
            Application.Run(new TaskbarEmbeddedForm());
        }
        catch (Exception ex)
        {
            ReportFatal(ex, showDialog: !selfTest);
            if (selfTest)
                Environment.ExitCode = 1;
        }
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
            // Logging must never hide the original startup problem.
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
