using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

namespace DbViewer
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private const string SingleInstanceMutexName = @"Local\DbViewer_Protech_HistoryViewer";
        private Mutex? _singleInstanceMutex;

        protected override void OnStartup(StartupEventArgs e)
        {
            _singleInstanceMutex = new Mutex(true, SingleInstanceMutexName, out bool createdNew);

            if (!createdNew)
            {
                ActivateExistingInstance();
                _singleInstanceMutex.Dispose();
                _singleInstanceMutex = null;
                Shutdown(0);
                return;
            }

            ShutdownMode = ShutdownMode.OnMainWindowClose;
            base.OnStartup(e);

            MainWindow mainWindow = new();
            MainWindow = mainWindow;
            mainWindow.Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (_singleInstanceMutex != null)
            {
                try
                {
                    _singleInstanceMutex.ReleaseMutex();
                }
                catch (ApplicationException)
                {
                }

                _singleInstanceMutex.Dispose();
                _singleInstanceMutex = null;
            }

            base.OnExit(e);
        }

        private static void ActivateExistingInstance()
        {
            using Process currentProcess = Process.GetCurrentProcess();

            Process? existingProcess = Process.GetProcessesByName(currentProcess.ProcessName)
                .Where(process => process.Id != currentProcess.Id)
                .OrderBy(process => GetProcessStartTime(process))
                .FirstOrDefault();

            if (existingProcess == null)
            {
                return;
            }

            nint windowHandle = WaitForMainWindowHandle(existingProcess);

            if (windowHandle == nint.Zero)
            {
                return;
            }

            if (IsIconic(windowHandle))
            {
                ShowWindowAsync(windowHandle, ShowRestore);
            }
            else
            {
                ShowWindowAsync(windowHandle, ShowNormal);
            }

            SetForegroundWindow(windowHandle);
        }

        private static DateTime GetProcessStartTime(Process process)
        {
            try
            {
                return process.StartTime;
            }
            catch
            {
                return DateTime.MaxValue;
            }
        }

        private static nint WaitForMainWindowHandle(Process process)
        {
            for (int attempt = 0; attempt < 15; attempt++)
            {
                process.Refresh();

                if (process.MainWindowHandle != nint.Zero)
                {
                    return process.MainWindowHandle;
                }

                Thread.Sleep(100);
            }

            return nint.Zero;
        }

        private const int ShowNormal = 1;
        private const int ShowRestore = 9;

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(nint hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindowAsync(nint hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(nint hWnd);
    }
}
