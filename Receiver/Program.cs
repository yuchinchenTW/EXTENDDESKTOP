using System;
using System.Runtime.InteropServices;
using System.Runtime;
using System.Windows.Forms;

namespace ExtentDesktop.Receiver
{
    internal static class Program
    {
        [DllImport("winmm.dll", ExactSpelling = true)]
        private static extern uint timeBeginPeriod(uint uPeriod);

        [DllImport("winmm.dll", ExactSpelling = true)]
        private static extern uint timeEndPeriod(uint uPeriod);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetProcessWorkingSetSize(IntPtr hProcess, IntPtr minSize, IntPtr maxSize);

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

                try
                {
                    GCSettings.LatencyMode = GCLatencyMode.LowLatency;
                }
                catch
                {
                }

                try
                {
                    var p = System.Diagnostics.Process.GetCurrentProcess();
                    SetProcessWorkingSetSize(p.Handle, new IntPtr(80L * 1024 * 1024), new IntPtr(300L * 1024 * 1024));
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
