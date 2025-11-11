using Microsoft.Web.WebView2.Core;
using Overlay_Renderer.Helpers;
using System;
using System.Drawing;
using System.Numerics;
using System.Threading.Tasks;
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
                    //Logger.Info($"[WebViewSurface] UpdateBounds -> {rect}");
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"[WebViewSurface] UpdateBounds failed: {ex.Message}");
            }
        }

        public void Dispose()
        {
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
