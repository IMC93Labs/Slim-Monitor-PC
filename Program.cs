using System.Threading;
using System.Windows.Forms;

namespace SlimMonitorPC;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(initiallyOwned: true, @"Local\SlimMonitorPC", out var createdNew);
        if (!createdNew)
        {
            MessageBox.Show(
                "Slim Monitor PC ya se está ejecutando. Si no lo ves, comprueba la instancia anterior en el Administrador de tareas y ciérrala antes de abrir esta versión.",
                "Slim Monitor PC",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        try
        {
            ApplicationConfiguration.Initialize();
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (_, e) => ReportFatal(e.Exception);
            Application.Run(new TaskbarMonitorForm());
        }
        catch (Exception ex)
        {
            ReportFatal(ex);
        }
    }

    private static void ReportFatal(Exception exception)
    {
        try
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IMC93Labs", "SlimMonitorPC");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "startup-error.log"), $"{DateTime.Now:O}\r\n{exception}");
        }
        catch
        {
            // Logging must never hide the original startup problem.
        }

        MessageBox.Show(
            "Slim Monitor PC no ha podido iniciarse. Se ha guardado un registro en %LOCALAPPDATA%\\IMC93Labs\\SlimMonitorPC\\startup-error.log.\r\n\r\n" + exception.Message,
            "Slim Monitor PC - Error",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }
}
