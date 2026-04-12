using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;

#nullable disable

namespace OLEDSaver
{
    static partial class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            InitializeMonitorSettings();
            LoadSettings();
            SetupControllerActivityTracking();
            if (_desktopIconsHidingEnabled)
            {
                StartDesktopMonitoring();
            }
            MagicTrick();
            StartPriorityEnforcementTimer();
            SetupTrayIcon();
            SetupOverlayWindows();
            StartInactivityTimer();
            StartEdgeTimer();
            UpdateBackgroundTimerProfile();

            Application.Run();
        }

        static void MagicTrick()
        {
            ApplyLowPriorityProfile();

            var currentProcess = Process.GetCurrentProcess();
            foreach (ProcessThread thread in currentProcess.Threads)
            {
                try
                {
                    uint access = THREAD_SET_INFORMATION | THREAD_QUERY_INFORMATION;
                    IntPtr hThread = OpenThread(access, false, (uint)thread.Id);
                    if (hThread != IntPtr.Zero)
                    {
                        SetThreadPriority(hThread, THREAD_PRIORITY_IDLE);
                        SetThreadPriorityBoost(hThread, true);
                        CloseHandle(hThread);
                    }
                }
                catch
                {
                }
            }
        }

        private static void ApplyLowPriorityProfile()
        {
            try
            {
                var currentProcess = Process.GetCurrentProcess();
                currentProcess.PriorityClass = ProcessPriorityClass.Idle;
                SetProcessPriorityBoost(currentProcess.Handle, true);
            }
            catch
            {
            }

            try
            {
                IntPtr currentThread = GetCurrentThread();
                if (currentThread != IntPtr.Zero)
                {
                    SetThreadPriority(currentThread, THREAD_PRIORITY_IDLE);
                    SetThreadPriorityBoost(currentThread, true);
                }
            }
            catch
            {
            }
        }

        private static void StartPriorityEnforcementTimer()
        {
            if (_priorityEnforcementTimer != null)
                return;

            _priorityEnforcementTimer = new Timer { Interval = PriorityEnforcementIntervalMs };
            _priorityEnforcementTimer.Tick += (s, e) => ApplyLowPriorityProfile();
            _priorityEnforcementTimer.Start();
        }

        private static void StartEdgeTimer()
        {
            _edgeTimer = new Timer { Interval = TaskbarStatePollingIntervalMs };
            _edgeTimer.Tick += (s, e) =>
            {
                if (!_taskbarHidingEnabled)
                {
                    SetEdgeTimerInterval(DisabledEdgePollingIntervalMs);
                    return;
                }

                if (IsWindowsKeyPressed())
                {
                    SetEdgeTimerInterval(ActiveEdgePollingIntervalMs);
                    _lastWindowsKeyTime = DateTime.Now;
                    ShowTaskbarAndDesktop();
                    _lastTaskbarInteractionTime = DateTime.Now;
                    return;
                }

                if ((DateTime.Now - _lastWindowsKeyTime) < _winKeyShowDuration)
                {
                    SetEdgeTimerInterval(ActiveEdgePollingIntervalMs);
                    ShowTaskbarAndDesktop();
                    _lastTaskbarInteractionTime = DateTime.Now;
                    return;
                }

                SetEdgeTimerInterval(IsTaskbarEdgeHookActive()
                    ? TaskbarStatePollingIntervalMs
                    : (_taskbarHidden ? IdleEdgePollingIntervalMs : ActiveEdgePollingIntervalMs));

                if (!IsTaskbarEdgeHookActive() && GetCursorPos(out Point cursorPosition))
                {
                    HandleTaskbarEdgePointerActivity(cursorPosition);
                }

                if (!_taskbarHidden)
                {
                    if (_lastTaskbarInteractionTime == DateTime.MinValue)
                    {
                        _lastTaskbarInteractionTime = DateTime.Now;
                    }

                    var elapsed = DateTime.Now - _lastTaskbarInteractionTime;
                    if (elapsed.TotalSeconds >= _taskbarTimeoutSeconds)
                    {
                        HideTaskbarAndDesktop();
                        _lastTaskbarInteractionTime = DateTime.MinValue;
                    }
                }
            };
            _edgeTimer.Start();
        }

        private static bool IsTaskbarEdgeHookActive()
        {
            return _edgeMouseHookHandle != IntPtr.Zero;
        }

        private static bool IsNearTaskbarEdge(Point cursorPosition)
        {
            return cursorPosition.Y >= Screen.PrimaryScreen.Bounds.Bottom - _activityThreshold;
        }

        private static void HandleTaskbarEdgePointerActivity(Point cursorPosition)
        {
            if (!_taskbarHidingEnabled || !IsNearTaskbarEdge(cursorPosition))
            {
                return;
            }

            ShowTaskbarAndDesktop();
            _lastTaskbarInteractionTime = DateTime.Now;
        }

        private static void EnsureTaskbarEdgeHook()
        {
            if (IsTaskbarEdgeHookActive())
            {
                return;
            }

            IntPtr moduleHandle = IntPtr.Zero;

            try
            {
                using var currentProcess = Process.GetCurrentProcess();
                string moduleName = currentProcess.MainModule?.ModuleName;
                if (!string.IsNullOrEmpty(moduleName))
                {
                    moduleHandle = GetModuleHandle(moduleName);
                }
            }
            catch
            {
                moduleHandle = IntPtr.Zero;
            }

            try
            {
                _edgeMouseHookHandle = SetWindowsHookEx(WH_MOUSE_LL, _edgeMouseProc, moduleHandle, 0);
                if (_edgeMouseHookHandle != IntPtr.Zero)
                {
                    _hasLastEdgeHookMousePosition = false;
                }
            }
            catch
            {
                _edgeMouseHookHandle = IntPtr.Zero;
            }
        }

        private static void StopTaskbarEdgeHook()
        {
            if (!IsTaskbarEdgeHookActive())
            {
                return;
            }

            try
            {
                UnhookWindowsHookEx(_edgeMouseHookHandle);
            }
            catch
            {
            }
            finally
            {
                _edgeMouseHookHandle = IntPtr.Zero;
                _hasLastEdgeHookMousePosition = false;
                _lastEdgeHookMousePosition = Point.Empty;
            }
        }

        private static IntPtr EdgeMouseHookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && wParam == (IntPtr)WM_MOUSEMOVE && lParam != IntPtr.Zero)
            {
                MSLLHOOKSTRUCT hookData = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                Point cursorPosition = new Point(hookData.pt.x, hookData.pt.y);

                if (!_hasLastEdgeHookMousePosition || cursorPosition != _lastEdgeHookMousePosition)
                {
                    _lastEdgeHookMousePosition = cursorPosition;
                    _hasLastEdgeHookMousePosition = true;
                    HandleTaskbarEdgePointerActivity(cursorPosition);
                }
            }

            return CallNextHookEx(_edgeMouseHookHandle, nCode, wParam, lParam);
        }

        private static void SetEdgeTimerInterval(int interval)
        {
            if (_edgeTimer != null && _edgeTimer.Interval != interval)
            {
                _edgeTimer.Interval = interval;
            }
        }

        private static void InitializeMonitorSettings()
        {
            foreach (var screen in Screen.AllScreens)
            {
                var displayName = $"Monitor {Array.IndexOf(Screen.AllScreens, screen) + 1}";
                if (screen == Screen.PrimaryScreen)
                    displayName += " (Primary)";

                _monitorSettings[screen.DeviceName] = new MonitorSettings
                {
                    DeviceName = screen.DeviceName,
                    DisplayName = displayName,
                    Enabled = true,
                    TimeoutSeconds = 25
                };
            }
        }
    }
}
