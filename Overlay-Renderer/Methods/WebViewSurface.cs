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

        private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        private delegate bool EnumChildProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool EnumChildWindows(IntPtr hWndParent, EnumChildProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

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
        }

        // -----------------------------
        // Cursor suppression hook
        // -----------------------------
        private async Task HookChildForCursorAsync()
        {
            try
            {
                // Wait a bit for WebView internals to create child windows
                await Task.Delay(300).ConfigureAwait(false);

                IntPtr parent = _parentHwnd;
                if (parent == IntPtr.Zero)
                {
                    Logger.Warn("[WebViewSurface] HookChildForCursorAsync: parent HWND is 0.");
                    return;
                }

                Logger.Info("[WebViewSurface] Enumerating child windows of overlay for cursor hook...");

                IntPtr found = IntPtr.Zero;
                var sb = new StringBuilder(256);
                int loggedCount = 0;

                EnumChildProc cb = (hwnd, lParam) =>
                {
                    sb.Clear();
                    int len = GetClassName(hwnd, sb, sb.Capacity);
                    if (len <= 0)
                        return true;

                    string cls = sb.ToString();

                    // Log first few child classes so we can see what's there
                    if (loggedCount < 10)
                    {
                        Logger.Info($"[WebViewSurface] Child HWND=0x{hwnd.ToInt64():X}, class='{cls}'");
                        loggedCount++;
                    }

                    // Look for likely WebView / Chromium host
                    if (cls.Contains("WebView", StringComparison.OrdinalIgnoreCase) ||
                        cls.Contains("Chrome_WidgetHost", StringComparison.OrdinalIgnoreCase) ||
                        cls.Contains("Chrome_RenderWidgetHostHWND", StringComparison.OrdinalIgnoreCase))
                    {
                        found = hwnd;
                        return false; // stop enumeration
                    }

                    return true;
                };

                EnumChildWindows(parent, cb, IntPtr.Zero);

                if (found == IntPtr.Zero)
                {
                    Logger.Warn("[WebViewSurface] Could not find WebView child window to subclass for cursor blocking.");
                    return;
                }

                _webViewHwnd = found;

                // Subclass: intercept WM_SETCURSOR
                _webViewHookProc = WebViewWndProc;
                IntPtr hookPtr = Marshal.GetFunctionPointerForDelegate(_webViewHookProc);

                IntPtr prev = GetWindowLongPtr(_webViewHwnd, GWLP_WNDPROC);
                if (prev == IntPtr.Zero)
                {
                    Logger.Warn("[WebViewSurface] GetWindowLongPtr returned 0 for WebView child HWND.");
                    _webViewHookProc = null;
                    _webViewHwnd = IntPtr.Zero;
                    return;
                }

                IntPtr prev2 = SetWindowLongPtr(_webViewHwnd, GWLP_WNDPROC, hookPtr);
                if (prev2 == IntPtr.Zero)
                {
                    Logger.Warn("[WebViewSurface] SetWindowLongPtr failed when subclassing WebView child.");
                    _webViewHookProc = null;
                    _webViewHwnd = IntPtr.Zero;
                    return;
                }

                _webViewOldWndProc = prev;
                Logger.Info($"[WebViewSurface] Subclassed WebView child hwnd=0x{_webViewHwnd.ToInt64():X} to suppress WM_SETCURSOR.");
            }
            catch (Exception ex)
            {
                Logger.Warn($"[WebViewSurface] HookChildForCursorAsync error: {ex}");
            }
        }

        private IntPtr WebViewWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == WM_SETCURSOR)
            {
                // Eat all cursor updates from the embedded WebView.
                // Star Citizen is the only thing allowed to touch the cursor.
                return IntPtr.Zero;
            }

            if (_webViewOldWndProc != IntPtr.Zero)
                return CallWindowProc(_webViewOldWndProc, hWnd, msg, wParam, lParam);

            return IntPtr.Zero;
        }

        public void Dispose()
        {
            try
            {
                if (_webViewHwnd != IntPtr.Zero && _webViewOldWndProc != IntPtr.Zero)
                {
                    SetWindowLongPtr(_webViewHwnd, GWLP_WNDPROC, _webViewOldWndProc);
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
