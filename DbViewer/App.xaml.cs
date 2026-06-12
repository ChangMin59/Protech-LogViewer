using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

namespace DbViewer
{
    // WPF 앱 시작/종료 진입점이다.
    // 같은 exe가 두 번 실행되지 않도록 단일 실행 인스턴스를 관리한다.
    public partial class App : Application
    {
        // 사용자 세션 안에서 DbViewer 실행 여부를 구분하는 Mutex 이름이다.
        private const string SingleInstanceMutexName = @"Local\DbViewer_Protech_HistoryViewer";
        private Mutex? _singleInstanceMutex;

        // exe 시작 시 단일 실행 여부를 확인하고 MainWindow를 연다.
        protected override void OnStartup(StartupEventArgs e)
        {
            // createdNew=false면 이미 실행 중인 DbViewer가 있다는 뜻이다.
            _singleInstanceMutex = new Mutex(true, SingleInstanceMutexName, out bool createdNew);

            if (!createdNew)
            {
                // 새 창을 만들지 않고 기존 창을 앞으로 가져온 뒤 종료한다.
                ActivateExistingInstance();
                _singleInstanceMutex.Dispose();
                _singleInstanceMutex = null;
                Shutdown(0);
                return;
            }

            // 메인 창이 닫히면 프로그램 전체를 종료한다.
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            base.OnStartup(e);

            // App.xaml StartupUri를 쓰지 않고 여기서 직접 MainWindow를 생성한다.
            MainWindow mainWindow = new();
            MainWindow = mainWindow;
            mainWindow.Show();
        }

        // 프로그램 종료 시 Mutex를 해제해 다음 실행이 가능하게 한다.
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
                    // 이미 해제된 경우에도 종료 흐름은 계속 진행한다.
                }

                _singleInstanceMutex.Dispose();
                _singleInstanceMutex = null;
            }

            base.OnExit(e);
        }

        // 이미 실행 중인 DbViewer 창을 찾아 앞으로 가져온다.
        private static void ActivateExistingInstance()
        {
            using Process currentProcess = Process.GetCurrentProcess();

            // 같은 exe 이름의 다른 프로세스 중 가장 먼저 실행된 프로세스를 기존 인스턴스로 본다.
            Process? existingProcess = Process.GetProcessesByName(currentProcess.ProcessName)
                .Where(process => process.Id != currentProcess.Id)
                .OrderBy(process => GetProcessStartTime(process))
                .FirstOrDefault();

            if (existingProcess == null)
            {
                // Mutex는 잡혔지만 프로세스를 찾지 못한 예외 상황이다.
                return;
            }

            // WPF 창 핸들이 준비될 때까지 잠깐 기다린다.
            nint windowHandle = WaitForMainWindowHandle(existingProcess);

            if (windowHandle == nint.Zero)
            {
                return;
            }

            if (IsIconic(windowHandle))
            {
                // 기존 창이 최소화되어 있으면 복원한다.
                ShowWindowAsync(windowHandle, ShowRestore);
            }
            else
            {
                // 이미 보이는 창이면 일반 표시 상태로 앞으로 가져온다.
                ShowWindowAsync(windowHandle, ShowNormal);
            }

            SetForegroundWindow(windowHandle);
        }

        // 프로세스 시작 시간을 안전하게 가져온다.
        private static DateTime GetProcessStartTime(Process process)
        {
            try
            {
                return process.StartTime;
            }
            catch
            {
                // 권한 문제 등으로 시작 시간을 못 읽으면 정렬 뒤쪽으로 보낸다.
                return DateTime.MaxValue;
            }
        }

        // 기존 프로세스의 MainWindowHandle이 생길 때까지 짧게 기다린다.
        private static nint WaitForMainWindowHandle(Process process)
        {
            for (int attempt = 0; attempt < 15; attempt++)
            {
                process.Refresh();

                if (process.MainWindowHandle != nint.Zero)
                {
                    // 창 핸들이 준비되면 즉시 반환한다.
                    return process.MainWindowHandle;
                }

                // WPF 창 생성 직후 핸들이 늦게 잡힐 수 있어 100ms씩 대기한다.
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
