using System;
using System.Windows.Forms;

namespace ExtentDesktop.Host
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new HostForm());
        }
    }
}
