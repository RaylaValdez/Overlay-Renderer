using ImGuiNET;
using Overlay_Renderer.Helpers;
using System.Numerics;
using Windows.Win32.Foundation;

namespace Overlay_Renderer.Methods
{
    public static class WebBrowserManager
    {
        private static HWND _overlayHwnd;
        private static bool _initialized;

        private static readonly Dictionary<string, WebViewSurface> _surfaces = new();
        private static readonly Dictionary<string, Task> _initTasks = new();

        private static string? _activeAppletId;
        private static bool _mouseOverAnyWebRegion;

        public static bool MouseOverWebRegion => _mouseOverAnyWebRegion;

        public static Func<string?>? GlobalDocumentScriptProvider { get; set; }

        internal static string? GetGlobalDocumentScript()
        {
            try
            {
                return GlobalDocumentScriptProvider?.Invoke();
            }
            catch (Exception ex)
            {
                Logger.Warn($"[WebBrowserManager] GlobalDocumentScriptPriveder threw: {ex.Message}");
                return null;
            }
        }

        public static void Initialize(HWND overlayHwnd, int width, int height)
        {
            if (_initialized)
                return;

            _overlayHwnd = overlayHwnd;
            _initialized = true;

            unsafe
            {
                //Logger.Info($"[WebBrowserManager] Initialize overlayHwnd=0x{(nuint)_overlayHwnd.Value:X}");
            }
        }

        public static void BeginFrame()
        {
            _mouseOverAnyWebRegion = false;
        }




        private static WebViewSurface GetOrCreateSurface(string appletId)
        {
            if (_surfaces.TryGetValue(appletId, out var surface))
                return surface;

            if (_overlayHwnd == HWND.Null)
                throw new InvalidOperationException("WebBrowserManager.Initialize must be called first.");

            var s = new WebViewSurface(_overlayHwnd);
            _surfaces[appletId] = s;

            if (_activeAppletId == null || _activeAppletId == appletId)
            {
                s.SetActive(true);
            }

            var initTask = s.InitializeAsync();
            _initTasks[appletId] = initTask;

            //Logger.Info($"[WebBrowserManager] Created WebViewSurface for '{appletId}'");
            return s;
        }

        public static void SetActiveApplet(string? appletId)
        {
            _activeAppletId = appletId;

            foreach (var kvp in _surfaces)
            {
                bool active = (appletId != null) && kvp.Key == appletId;
                kvp.Value.SetActive(active);
            }
        }

        /// <summary>
        /// Optional explicit navigate (e.g. on pressing Enter in URL bar).
        /// </summary>
        public static void Navigate(string appletId, string url)
        {
            var surf = GetOrCreateSurface(appletId);
            surf.NavigateIfNeeded(url);
        }

        public static bool ActiveCanGoBack()
        {
            if (_activeAppletId == null)
                return false;

            if (!_surfaces.TryGetValue(_activeAppletId, out var surf))
                return false;

            return surf.CanGoBack;
        }

        public static void GoBackOnActiveApplet()
        {
            if (_activeAppletId == null)
                return;

            if (_surfaces.TryGetValue(_activeAppletId, out var surf))
            {
                surf.GoBack();
            }
        }

        /// <summary>
        /// Draws and positions the WebView for a given appletId and URL.
        /// </summary>
        public static void DrawWebPage(string appletId, string url, Vector2 availableSize)
        {
            if (!_initialized)
            {
                ImGui.TextDisabled("Web browser system not initialized.");
                return;
            }

            var surface = GetOrCreateSurface(appletId);

            if (_initTasks.TryGetValue(appletId, out var t) && !t.IsCompleted)
            {
                ImGui.Text("Starting WebView2...");
                return;
            }

            Vector2 drawSize = availableSize;
            if (drawSize.X <= 0f || drawSize.Y <= 0f)
            {
                ImGui.TextDisabled("No space for web view.");
                return;
            }

            Vector2 screenMin = ImGui.GetCursorScreenPos();
            Vector2 screenMax = screenMin + drawSize;

            ImGui.InvisibleButton($"##webview_{appletId}", drawSize);

            var io = ImGui.GetIO();
            var mouse = io.MousePos;

            bool inside =
                mouse.X >= screenMin.X && mouse.X <= screenMax.X &&
                mouse.Y >= screenMin.Y && mouse.Y <= screenMax.Y;

            if (inside)
                _mouseOverAnyWebRegion = true;

            surface.NavigateIfNeeded(url);
            surface.UpdateBounds(screenMin, drawSize);
        }
    }
}
