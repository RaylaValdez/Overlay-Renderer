using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Overlay_Renderer.Methods
{
    public static class FindProcess
    {
        public static IntPtr TryFindMainWindow(string processName)
        {
            foreach (var process in Process.GetProcessesByName(processName))
            {
                IntPtr hwnd = FindMainWindowForProcess(process.Id);
                if (hwnd != IntPtr.Zero)
                    return hwnd;
            }
            return IntPtr.Zero;
        }

        public static IntPtr WaitForMainWindow(string processName, int retries = 20, int delayMs = 500)
        {
            for (int i = 0; i < retries; i++)
            {
                var hwnd = TryFindMainWindow(processName);
                if (hwnd != IntPtr.Zero)
                    return hwnd;
                Thread.Sleep(delayMs);
            }
            return IntPtr.Zero;
        }

        public static IntPtr FindMainWindowForProcess(int pid)
        {
            IntPtr found = IntPtr.Zero;
            EnumWindows((hWnd, _) =>
            {
                GetWindowThreadProcessId(hWnd, out uint windowPid);
                if (windowPid == pid && IsWindowVisible(hWnd))
                {
                    found = hWnd;
                    return false;
                }
                return true;
            }, IntPtr.Zero);
            return found;
        }

        delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern void GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    }
}
