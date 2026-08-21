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
            return;

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
