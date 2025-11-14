using Microsoft.Web.WebView2.Core;
using Overlay_Renderer.Helpers;
using System.Drawing;
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


        // For logging / debugging placement
        private Rectangle _lastBounds;
        private bool _hasLastBounds;

        public bool IsInitialized => _initialized && !_initFailed;

        // --- cursor blocking for WebView child HWND ---

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
                Logger.Info("[WebViewSurface] Creating WebView2 environment (per-surface).");
                _env = await CoreWebView2Environment.CreateAsync();

                Logger.Info("[WebViewSurface] Creating WebView2 controller (HWND host).");
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

                // Initial bounds – will be updated every frame
                _controller.Bounds = new Rectangle(0, 0, 1, 1);
                _controller.IsVisible = _active;

                // Async: find & subclass the WebView child HWND to kill WM_SETCURSOR
                _ = HookChildForCursorAsync();

                _initialized = true;
                _initFailed = false;
                Logger.Info("[WebViewSurface] WebView2 surface initialized.");
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

                // Optional: pause JS timers etc. when inactive
                if (_core != null)
                {
                    string policy = active ? "advance" : "pause";
                    _core.CallDevToolsProtocolMethodAsync(
                        "Emulation.setVirtualTimePolicy",
                        $"{{\"policy\":\"{policy}\"}}");
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
                Logger.Info($"[WebViewSurface] navigating to {url}");
                _core.Navigate(url);
            }
            catch (Exception ex)
            {
                Logger.Warn($"[WebViewSurface] navigate failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Can the embedded browser navigate back?
        /// </summary>
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

        /// <summary>
        /// Navigate back in the embedded browser, if possible.
        /// </summary>
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

        /// <summary>
        /// Position and size the WebView to cover the given ImGui rect.
        /// imageMinClient is in *overlay client coordinates* (ImGui main viewport).
        /// drawSize is the pixel size of the desired area.
        /// </summary>
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

                // Log only when bounds actually change so we don't spam
                if (!_hasLastBounds || !_lastBounds.Equals(rect))
                {
                    _hasLastBounds = true;
                    _lastBounds = rect;
                    // Logger.Info($"[WebViewSurface] UpdateBounds -> {rect}");
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"[WebViewSurface] UpdateBounds failed: {ex.Message}");
            }

            EnsureCursorHook();
        }

        // -----------------------------
        // Cursor suppression hook
        // -----------------------------
        private async Task HookChildForCursorAsync()
        {
            try
            {
                // Give WebView2 some time to spin up its window hierarchy
                await Task.Delay(500).ConfigureAwait(false);

                IntPtr parent = _parentHwnd;
                if (parent == IntPtr.Zero)
                {
                    Logger.Warn("[WebViewSurface] HookChildForCursorAsync: parent HWND is 0.");
                    return;
                }

                Logger.Info("[WebViewSurface] Enumerating child windows of overlay for cursor hook...");

                IntPtr topLevel = IntPtr.Zero;
                var sb = new StringBuilder(256);
                int loggedCount = 0;

                // 1) Find the top-level WebView/Chrome child of the overlay
                EnumChildProc findTop = (hwnd, lParam) =>
                {
                    sb.Clear();
                    int len = GetClassName(hwnd, sb, sb.Capacity);
                    if (len <= 0)
                        return true;

                    string cls = sb.ToString();

                    if (loggedCount < 10)
                    {
                        Logger.Info($"[WebViewSurface] Child HWND=0x{hwnd.ToInt64():X}, class='{cls}'");
                        loggedCount++;
                    }

                    if (cls.Contains("WebView", StringComparison.OrdinalIgnoreCase) ||
                        cls.Contains("Chrome_WidgetHost", StringComparison.OrdinalIgnoreCase) ||
                        cls.Contains("Chrome_RenderWidgetHostHWND", StringComparison.OrdinalIgnoreCase) ||
                        cls.Contains("Chrome_WidgetWin", StringComparison.OrdinalIgnoreCase))
                    {
                        topLevel = hwnd;
                        return false; // stop, we found a candidate
                    }

                    return true;
                };

                EnumChildWindows(parent, findTop, IntPtr.Zero);

                if (topLevel == IntPtr.Zero)
                {
                    Logger.Warn("[WebViewSurface] Could not find WebView child window to subclass for cursor blocking (no top-level match).");
                    return;
                }

                // 2) Try to find an actual render widget under that top-level window
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
                        Logger.Info($"[WebViewSurface]   Sub-child HWND=0x{hwnd.ToInt64():X}, class='{cls}'");
                        loggedCount++;
                    }

                    if (cls.Contains("Chrome_RenderWidgetHostHWND", StringComparison.OrdinalIgnoreCase) ||
                        cls.Contains("WebView", StringComparison.OrdinalIgnoreCase) ||
                        cls.Contains("Chrome_WidgetWin", StringComparison.OrdinalIgnoreCase))
                    {
                        renderChild = hwnd;
                        return false; // good enough
                    }

                    return true;
                };

                EnumChildWindows(topLevel, findRender, IntPtr.Zero);

                // Prefer the deepest child, but fall back to top-level
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
                // If we already hooked this exact window and it still exists, do nothing.
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
                    Logger.Warn($"[WebViewSurface] GetWindowLongPtr(GWLP_WNDPROC) failed for hwnd=0x{hwnd.ToInt64():X}, error={err}");
                    return false;
                }

                // If the window is already using our hook stub, someone (probably us) already subclassed it.
                // In that case, DO NOT overwrite _webViewOldWndProc with the hook pointer.
                if (prev == hookPtr)
                {
                    Logger.Info($"[WebViewSurface] hwnd=0x{hwnd.ToInt64():X} already subclassed; keeping existing old proc.");
                    _webViewHwnd = hwnd;
                    _cursorHookInstalled = true;
                    return true;
                }

                // First time we see this window: capture its original WndProc ONCE.
                if (_webViewOldWndProc == IntPtr.Zero)
                {
                    _webViewOldWndProc = prev;
                }

                Marshal.GetLastWin32Error();
                IntPtr res = SetWindowLongPtrSafe(hwnd, GWLP_WNDPROC, hookPtr);
                err = Marshal.GetLastWin32Error();

                if (res == IntPtr.Zero && err != 0)
                {
                    Logger.Warn($"[WebViewSurface] SetWindowLongPtr(GWLP_WNDPROC) failed for hwnd=0x{hwnd.ToInt64():X}, error={err}");
                    return false;
                }

                sb.Clear();
                GetClassName(hwnd, sb, sb.Capacity);
                Logger.Info($"[WebViewSurface] Subclassed WebView child hwnd=0x{hwnd.ToInt64():X}, class='{sb}' to suppress WM_SETCURSOR.");

                _webViewHwnd = hwnd;
                _cursorHookInstalled = true;
                return true;
            }
        }



        private IntPtr WebViewWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == WM_SETCURSOR)
            {
                // Hard block: tell user32 this has been handled so Chrome/WebView
                // never sees WM_SETCURSOR and can't call SetCursor.
                Logger.Info($"[WebViewSurface] WM_SETCURSOR blocked for hwnd=0x{hWnd.ToInt64():X}");

                return new IntPtr(1); // TRUE
            }

            var old = _webViewOldWndProc;

            if (old != IntPtr.Zero)
            {
                return CallWindowProc(old, hWnd, msg, wParam, lParam);
            }

            // Safety fallback: if we somehow lost the old proc, don't recurse; just use DefWindowProc.
            return DefWindowProc(hWnd, msg, wParam, lParam);
        }


        /// <summary>
        /// Ensure that we still have a valid subclass on a WebView child window.
        /// If the hwnd disappeared or was never hooked, re-run the hook logic (throttled).
        /// Call this from a per-frame path (e.g. UpdateBounds).
        /// </summary>
        private void EnsureCursorHook()
        {
            // only makes sense once WebView is initialized and not failed
            if (!_initialized || _initFailed)
                return;

            // if we already have a window and it still exists, nothing to do
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

            // fire-and-forget re-scan; no await here, we’re on the render thread
            _ = HookChildForCursorAsync();
        }

        public void Dispose()
        {
            try
            {
                if (_webViewHwnd != IntPtr.Zero && _webViewOldWndProc != IntPtr.Zero)
                {
                    SetWindowLongPtrSafe(_webViewHwnd, GWLP_WNDPROC, _webViewOldWndProc);
                    Logger.Info($"[WebViewSurface] Restored original WndProc for WebView HWND=0x{_webViewHwnd.ToInt64():X}.");
                }
            }
            catch
            {
                // ignore
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
                // ignore
            }
        }
    }
}
