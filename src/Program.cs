using System;
using System.Threading;
using System.Windows.Forms;

namespace FastSkip
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            bool first;
            using (var mutex = new Mutex(true, "Local\\FastSkip.Desktop.v1", out first))
            {
                if (!first) { MessageBox.Show("Fast Skip 已在运行，请从系统托盘打开设置。", "Fast Skip"); return; }
                Native.SetProcessDPIAware();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm(Settings.DefaultPath));
            }
        }
    }
}
