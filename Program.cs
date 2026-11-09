using System;
using System.Threading;
using System.Windows.Forms;
using StreloReminder.Forms;

namespace StreloReminder
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            bool createdNew;
            using (Mutex mutex = new Mutex(true, "Global\\StreloReminder_SingleInstance_Mutex", out createdNew))
            {
                if (!createdNew)
                {
                    MessageBox.Show(
                        "Программа «Strelo — Напоминалка» уже запущена.\nПроверьте значок колокольчика в системном трее (возле часов).",
                        "Strelo Напоминалка",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return;
                }

                bool startMinimized = false;
                if (args != null)
                {
                    for (int i = 0; i < args.Length; i++)
                    {
                        if (string.Equals(args[i], "--minimized", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(args[i], "-m", StringComparison.OrdinalIgnoreCase))
                        {
                            startMinimized = true;
                        }
                    }
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm(startMinimized));
            }
        }
    }
}
