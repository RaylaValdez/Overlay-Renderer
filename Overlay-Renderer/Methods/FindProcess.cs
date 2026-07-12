using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Windows.Win32.Foundation;

namespace Overlay_Renderer.Methods
{
    public static class FindProcess
    {
        private const int GWL_STYLE = -16;
        private const int GWL_EXSTYLE = -20;
        private const int SW_RESTORE = 9;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const int DWMWA_CLOAKED = 14;

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
            uint bestScore = 0;

            EnumWindows((hWnd, _) =>
            {
                GetWindowThreadProcessId(hWnd, out uint windowPid);
                if (windowPid != pid)
                    return true;

                if (!IsWindowVisible(hWnd))
                    return true;

                uint score = ScoreWindow(hWnd);
                if (score > bestScore)
                {
                    bestScore = score;
                    found = hWnd;
                }
                return true;
            }, IntPtr.Zero);

            return found;
        }

        private static uint ScoreWindow(IntPtr hWnd)
        {
            uint score = 0;

            int exStyle = GetWindowLong(hWnd, GWL_EXSTYLE);
            if ((exStyle & 0x00000080) != 0)
                return 0;

            int style = GetWindowLong(hWnd, GWL_STYLE);

            if ((style & 0x08000000) != 0)
                return 0;

            int cloaked = 0;
            if (DwmGetWindowAttribute(hWnd, DWMWA_CLOAKED, out cloaked, sizeof(int)) == 0 && cloaked != 0)
                return 0;

            if ((style & 0x00C00000) != 0)
                score += 100;
            if ((style & 0x00040000) != 0)
                score += 50;
            if ((style & 0x00080000) != 0)
                score += 30;

            var sb = new StringBuilder(256);
            int len = GetWindowText(hWnd, sb, sb.Capacity);
            if (len > 0)
                score += (uint)Math.Min(len, 100);

            if (GetWindowRect(hWnd, out RECT rect))
            {
                int area = (rect.right - rect.left) * (rect.bottom - rect.top);
                if (area > 0)
                    score += (uint)Math.Min(area / 1000, 200);
            }

            return score;
        }

        public static void ForceForegroundWindow(IntPtr hWnd)
        {
            GetWindowThreadProcessId(hWnd, out uint targetThreadId);
            uint currentThreadId = GetCurrentThreadId();

            if (targetThreadId == currentThreadId)
            {
                SetForegroundWindow(hWnd);
                return;
            }

            AttachThreadInput(currentThreadId, targetThreadId, true);

            try
            {
                ShowWindow(hWnd, SW_RESTORE);
                SetForegroundWindow(hWnd);
                SetWindowPos(hWnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE);
            }
            finally
            {
                AttachThreadInput(currentThreadId, targetThreadId, false);
            }
        }

        delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern void GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("kernel32.dll")]
        static extern uint GetCurrentThreadId();

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("dwmapi.dll")]
        static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);
    }
}
