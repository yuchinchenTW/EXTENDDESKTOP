using System;
using System.Windows.Forms;

namespace ExtentDesktop.Receiver
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new ReceiverForm());
        }
    }
}
