using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ExtentDesktop.Receiver
{
    internal static class Program
    {
        [DllImport("winmm.dll", ExactSpelling = true)]
        private static extern uint timeBeginPeriod(uint uPeriod);

        [DllImport("winmm.dll", ExactSpelling = true)]
        private static extern uint timeEndPeriod(uint uPeriod);

        [STAThread]
        private static void Main()
        {
            timeBeginPeriod(1);
            try
            {
                try
                {
                    System.Diagnostics.Process.GetCurrentProcess().PriorityClass = System.Diagnostics.ProcessPriorityClass.AboveNormal;
                }
                catch
                {
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new ReceiverForm());
            }
            finally
            {
                timeEndPeriod(1);
            }
        }
    }
}
