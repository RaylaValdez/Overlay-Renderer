using Microsoft.Web.WebView2.Core;
using Overlay_Renderer.Helpers;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using Windows.Win32.Foundation;

namespace Overlay_Renderer.Methods
{
    internal sealed class WebViewSurface : IDisposable
    {
        private readonly HWND _parentHwnd;

        private CoreWebView2Environment? _env;
        private CoreWebView2Controller? _controller;
        private CoreWebView2? _core;

        private bool _initialized;
        private bool _initFailed;
        private string? _lastUrl;
        private bool _active;

        private bool _cursorHookInstalled;
        private const int CursorHookRetryMs = 1000;
        private long _lastCursorHookAttemptTicks;

        private Rectangle _lastBounds;
        private bool _hasLastBounds;

        public bool IsInitialized => _initialized && !_initFailed;

        private IntPtr _webViewHwnd = IntPtr.Zero;
        private IntPtr _webViewOldWndProc = IntPtr.Zero;
        private WndProc? _webViewHookProc;

        private const int GWLP_WNDPROC = -4;
        private const uint WM_SETCURSOR = 0x0020;

        private readonly object _cursorHookLock = new();

        private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        private delegate bool EnumChildProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool EnumChildWindows(IntPtr hWndParent, EnumChildProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
        private static extern IntPtr GetWindowLong32(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
        private static extern IntPtr SetWindowLong32(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", SetLastError = false)]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = false)]
        private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);


        private static IntPtr GetWindowLongPtrSafe(IntPtr hWnd, int nIndex)
        {
            if (IntPtr.Size == 8)
                return GetWindowLongPtr64(hWnd, nIndex);
            else
                return GetWindowLong32(hWnd, nIndex);
        }

        private static IntPtr SetWindowLongPtrSafe(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
        {
            if (IntPtr.Size == 8)
                return SetWindowLongPtr64(hWnd, nIndex, dwNewLong);
            else
                return SetWindowLong32(hWnd, nIndex, dwNewLong);
        }

        public WebViewSurface(HWND parentHwnd)
        {
            _parentHwnd = parentHwnd;
        }

        public async Task InitializeAsync()
        {
            if (_initialized || _initFailed)
                return;

            try
            {
                string userData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Starboard", "WebView2");

                Directory.CreateDirectory(userData);

                _env = await CoreWebView2Environment.CreateAsync(
                            browserExecutableFolder: null,
                            userDataFolder: userData,
                            options: null);
                _controller = await _env.CreateCoreWebView2ControllerAsync(_parentHwnd);
                _core = _controller.CoreWebView2;

                try
                {
                    var script = WebBrowserManager.GetGlobalDocumentScript();
                    if (!string.IsNullOrEmpty(script) && _core != null)
                    {
                        _ = _core.AddScriptToExecuteOnDocumentCreatedAsync(script);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn($"[WebViewSurface] Failed to inject global document script: {ex.Message}");
                }

                _controller.Bounds = new Rectangle(0, 0, 1, 1);
                _controller.IsVisible = _active;

                _ = HookChildForCursorAsync();

                _initialized = true;
                _initFailed = false;
            }
            catch (Exception ex)
            {
                Logger.Warn($"[WebViewSurface] init failed: {ex}");
                _initFailed = true;
            }
        }

        public void SetActive(bool active)
        {
            _active = active;

            if (_controller == null)
                return;

            try
            {
                _controller.IsVisible = active;

                if (_core != null)
                {
                    _core.IsMuted = !active;

                    string policy = active ? "advance" : "pause";
                    _core.CallDevToolsProtocolMethodAsync(
                        "Emulation.setVirtualTimePolicy",
                        $"{{\"policy\":\"{policy}\"}}");

                    if (!active)
                    {
                        const string pauseMediaJs = @"
                            try {
                                const media = document.querySelectorAll('video,audio');
                                media.forEach(function(el) {
                                    try {
                                        if (el.pause) el.pause();
                                    } catch (_) {}
                                    try {
                                        el.muted = true;
                                    } catch (_) {}
                                });
                            } catch (_) { }
                            ";
                        _core.ExecuteScriptAsync(pauseMediaJs);
                    }
                    else
                    {
                        const string unmuteMediaJs = @"
                            try {
                                const media = document.querySelectorAll('video,audio');
                                media.forEach(function(el) {
                                    try { el.muted = false; } catch (_) {}
                                });
                            } catch (_) { }
                            ";
                        _core.ExecuteScriptAsync(unmuteMediaJs);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"[WebViewSurface] SetActive failed: {ex.Message}");
            }
        }


        public void NavigateIfNeeded(string url)
        {
            if (!_initialized || _core == null) return;
            if (string.IsNullOrWhiteSpace(url)) return;

            if (string.Equals(url, _lastUrl, StringComparison.OrdinalIgnoreCase))
                return;

            _lastUrl = url;
            try
            {
                _core.Navigate(url);
            }
            catch (Exception ex)
            {
                Logger.Warn($"[WebViewSurface] navigate failed: {ex.Message}");
            }
        }

        public bool CanGoBack
        {
            get
            {
                try
                {
                    return _core != null && _core.CanGoBack;
                }
                catch
                {
                    return false;
                }
            }
        }

        public void GoBack()
        {
            try
            {
                if (_core != null && _core.CanGoBack)
                {
                    _core.GoBack();
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"[WebViewSurface] GoBack failed: {ex.Message}");
            }
        }

        public void UpdateBounds(Vector2 imageMinClient, Vector2 drawSize)
        {
            if (!_initialized || _controller == null)
                return;

            int w = Math.Max(1, (int)MathF.Round(drawSize.X));
            int h = Math.Max(1, (int)MathF.Round(drawSize.Y));
            int x = (int)MathF.Round(imageMinClient.X);
            int y = (int)MathF.Round(imageMinClient.Y);

            var rect = new Rectangle(x, y, w, h);

            try
            {
                _controller.Bounds = rect;
                _controller.IsVisible = _active;

                if (!_hasLastBounds || !_lastBounds.Equals(rect))
                {
                    _hasLastBounds = true;
                    _lastBounds = rect;
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"[WebViewSurface] UpdateBounds failed: {ex.Message}");
            }

            EnsureCursorHook();
        }

        private async Task HookChildForCursorAsync()
        {
            try
            {
                await Task.Delay(500).ConfigureAwait(false);

                IntPtr parent = _parentHwnd;
                if (parent == IntPtr.Zero)
                {
                    Logger.Warn("[WebViewSurface] HookChildForCursorAsync: parent HWND is 0.");
                    return;
                }

                IntPtr topLevel = IntPtr.Zero;
                var sb = new StringBuilder(256);
                int loggedCount = 0;

                EnumChildProc findTop = (hwnd, lParam) =>
                {
                    sb.Clear();
                    int len = GetClassName(hwnd, sb, sb.Capacity);
                    if (len <= 0)
                        return true;

                    string cls = sb.ToString();

                    if (loggedCount < 10)
                    {
                        loggedCount++;
                    }

                    if (cls.Contains("WebView", StringComparison.OrdinalIgnoreCase) ||
                        cls.Contains("Chrome_WidgetHost", StringComparison.OrdinalIgnoreCase) ||
                        cls.Contains("Chrome_RenderWidgetHostHWND", StringComparison.OrdinalIgnoreCase) ||
                        cls.Contains("Chrome_WidgetWin", StringComparison.OrdinalIgnoreCase))
                    {
                        topLevel = hwnd;
                        return false;
                    }

                    return true;
                };

                EnumChildWindows(parent, findTop, IntPtr.Zero);

                if (topLevel == IntPtr.Zero)
                {
                    Logger.Warn("[WebViewSurface] Could not find WebView child window to subclass for cursor blocking (no top-level match).");
                    return;
                }

                IntPtr renderChild = IntPtr.Zero;

                EnumChildProc findRender = (hwnd, lParam) =>
                {
                    sb.Clear();
                    int len = GetClassName(hwnd, sb, sb.Capacity);
                    if (len <= 0)
                        return true;

                    string cls = sb.ToString();

                    if (loggedCount < 20)
                    {
                        loggedCount++;
                    }

                    if (cls.Contains("Chrome_RenderWidgetHostHWND", StringComparison.OrdinalIgnoreCase) ||
                        cls.Contains("WebView", StringComparison.OrdinalIgnoreCase) ||
                        cls.Contains("Chrome_WidgetWin", StringComparison.OrdinalIgnoreCase))
                    {
                        renderChild = hwnd;
                        return false;
                    }

                    return true;
                };

                EnumChildWindows(topLevel, findRender, IntPtr.Zero);

                IntPtr candidate = renderChild != IntPtr.Zero ? renderChild : topLevel;

                if (!TrySubclassWebViewWindow(candidate, sb))
                {
                    if (candidate != topLevel && !TrySubclassWebViewWindow(topLevel, sb))
                    {
                        Logger.Warn("[WebViewSurface] Failed to subclass any WebView child; cursor suppression disabled for this surface.");
                    }
                }

            }
            catch (Exception ex)
            {
                Logger.Warn($"[WebViewSurface] HookChildForCursorAsync failed: {ex.Message}");
            }
        }

        private bool TrySubclassWebViewWindow(IntPtr hwnd, StringBuilder sb)
        {
            if (hwnd == IntPtr.Zero)
                return false;

            lock (_cursorHookLock)
            {
                if (_cursorHookInstalled && _webViewHwnd == hwnd && IsWindow(hwnd))
                    return true;

                if (_webViewHookProc == null)
                {
                    _webViewHookProc = WebViewWndProc;
                }

                IntPtr hookPtr = Marshal.GetFunctionPointerForDelegate(_webViewHookProc);

                Marshal.GetLastWin32Error();
                IntPtr prev = GetWindowLongPtrSafe(hwnd, GWLP_WNDPROC);
                int err = Marshal.GetLastWin32Error();

                if (prev == IntPtr.Zero && err != 0)
                {
                    return false;
                }

                if (prev == hookPtr)
                {
                    _webViewHwnd = hwnd;
                    _cursorHookInstalled = true;
                    return true;
                }

                if (_webViewOldWndProc == IntPtr.Zero)
                {
                    _webViewOldWndProc = prev;
                }

                Marshal.GetLastWin32Error();
                IntPtr res = SetWindowLongPtrSafe(hwnd, GWLP_WNDPROC, hookPtr);
                err = Marshal.GetLastWin32Error();

                if (res == IntPtr.Zero && err != 0)
                {
                    return false;
                }

                sb.Clear();
                GetClassName(hwnd, sb, sb.Capacity);

                _webViewHwnd = hwnd;
                _cursorHookInstalled = true;
                return true;
            }
        }

        private IntPtr WebViewWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == WM_SETCURSOR)
            {
                return new IntPtr(1);
            }

            var old = _webViewOldWndProc;

            if (old != IntPtr.Zero)
            {
                return CallWindowProc(old, hWnd, msg, wParam, lParam);
            }

            return DefWindowProc(hWnd, msg, wParam, lParam);
        }

        private void EnsureCursorHook()
        {
            if (!_initialized || _initFailed)
                return;

            if (_cursorHookInstalled && _webViewHwnd != IntPtr.Zero && IsWindow(_webViewHwnd))
                return;

            if (_webViewHwnd != IntPtr.Zero && !IsWindow(_webViewHwnd))
            {
                _webViewHwnd = IntPtr.Zero;
                _webViewOldWndProc = IntPtr.Zero;
                _webViewHookProc = null;
                _cursorHookInstalled = false;
            }

            long now = Environment.TickCount64;
            if (now - _lastCursorHookAttemptTicks < CursorHookRetryMs)
                return;

            _lastCursorHookAttemptTicks = now;

            _ = HookChildForCursorAsync();
        }

        public void Dispose()
        {
            try
            {
                if (_webViewHwnd != IntPtr.Zero && _webViewOldWndProc != IntPtr.Zero)
                {
                    SetWindowLongPtrSafe(_webViewHwnd, GWLP_WNDPROC, _webViewOldWndProc);
                }
            }
            catch
            {
            }

            _webViewHwnd = IntPtr.Zero;
            _webViewOldWndProc = IntPtr.Zero;
            _webViewHookProc = null;
            _cursorHookInstalled = false;

            try
            {
                _controller?.Close();
                _core = null;
                _controller = null;
                _env = null;
                _initialized = false;
                _hasLastBounds = false;
            }
            catch
            {
            }
        }
    }
}
