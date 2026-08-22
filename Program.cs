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
                using var form = new TaskbarOverlayFormV027();
                using var refinement = TaskbarV029Refinement.Attach(form);
                _ = form.Handle;
                return;
            }

            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (_, e) => ReportFatal(e.Exception, showDialog: true);

            // v0.2.10 intentionally uses one shell integration layer only. Previous
            // builds stacked v0.2.8 and v0.2.9 guards, which could fight each other
            // during Show desktop and leave the hover state latched.
            using var mainForm = new TaskbarOverlayFormV027();
            using var integration = TaskbarV029Refinement.Attach(mainForm);
            Application.Run(mainForm);
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
