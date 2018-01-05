using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace iTunesGoogleHome
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Mutex mutex = new Mutex(false, "iTunes Google Home Single Instance Mutex");
            if (!mutex.WaitOne(0, false))
            {
                mutex.Close();
                mutex = null;
                MessageBox.Show("iTunes Google Home is already running!");
            }
            else
            {
                Application.Run(new MainForm());
                mutex.ReleaseMutex();
            }
        }
    }
}
