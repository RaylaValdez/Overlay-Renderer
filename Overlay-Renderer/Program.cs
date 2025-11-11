using ImGuiNET;
using System.Numerics;
using Overlay_Renderer.Helpers;
using Overlay_Renderer.ImGuiRenderer;
using Overlay_Renderer.Methods;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;


namespace Overlay_Renderer
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Logger.Info("Overlay-Renderer starting...");

            // Get process name: arg0 or ask user
            string processName;
            if (args.Length > 0)
            {
                processName = args[0].Trim();
            }
            else
            {
                Logger.Info("Enter target process name (without .exe): ");
                processName = Console.ReadLine()?.Trim() ?? string.Empty;
            }

            if (string.IsNullOrEmpty(processName))
            {
                Logger.Error("No process name provided. Exiting.");
                return;
            }

            Logger.Info($"Waiting for main window of process '{processName}'...");
            var hwndIntPtr = FindProcess.WaitForMainWindow(processName, retries: 20, delayMs: 500);

            if (hwndIntPtr == IntPtr.Zero)
            {
                Logger.Error($"Could not find main window for process '{processName}'. Exiting.");
                return;
            }

            var targetHwnd = new HWND(hwndIntPtr);
            unsafe
            {
                Logger.Info($"Attached to window 0x{(nuint)targetHwnd.Value:X}");
            }

            using var overlay = new OverlayWindow(targetHwnd);
            overlay.Visible = false;

            if (!targetHwnd.IsNull && PInvoke.IsWindow(targetHwnd))
            {
                PInvoke.SetForegroundWindow(targetHwnd);
            }

            using var d3dHost = new D3DHost(overlay.Hwnd);
            using var imguiRenderer = new ImGuiRendererD3D11(d3dHost.Device, d3dHost.Context);

            TextureService.Initialize(imguiRenderer);
            int browserWidth = 1024;
            int browserHeight = 768;

            WebBrowserManager.Initialize(overlay.Hwnd, browserWidth, browserHeight);

            ImGuiStylePresets.ApplyDark();

            var cts = new CancellationTokenSource();

            // Track target window, keeping overlay aligned and resizing when needed.
            bool firstSize = true;
            var trackingTask = WindowTracker.StartTrackingAsync(
                targetHwnd,
                overlay,
                cts.Token,
                (w, h) =>
                {
                    d3dHost.EnsureSize(w, h);

                    if (firstSize)
                    {
                        firstSize = false;
                        overlay.Visible = true;
                    }
                });

            RunMessageAndRenderLoop(overlay, d3dHost, imguiRenderer, cts);

            cts.Cancel();
            try { trackingTask.Wait(500); } catch { /* ignore */ }

            Logger.Info("Overlay-Renderer shutting down.");
        }

        private static void RunMessageAndRenderLoop(
            OverlayWindow overlay,
            D3DHost d3dHost,
            ImGuiRendererD3D11 imguiRenderer,
            CancellationTokenSource cts)
        {
            MSG msg;

            while (!cts.IsCancellationRequested)
            {
                // Pump Win32 messages
                while (PInvoke.PeekMessage(out msg, HWND.Null, 0, 0,
                    PEEK_MESSAGE_REMOVE_TYPE.PM_REMOVE))
                {
                    if (msg.message == PInvoke.WM_QUIT)
                    {
                        cts.Cancel();
                        return;
                    }

                    PInvoke.TranslateMessage(msg);
                    PInvoke.DispatchMessage(msg);
                }

                if (overlay.Hwnd.IsNull)
                {
                    cts.Cancel();
                    return;
                }

                // Render Frame
                d3dHost.BeginFrame();
                imguiRenderer.NewFrame(overlay.ClientWidth, overlay.ClientHeight);

                ImGuiInput.UpdateMouse(overlay);
                ImGuiInput.UpdateKeyboard();

                HitTestRegions.BeginFrame();
                DrawDemoUi();
                HitTestRegions.ApplyToOverlay(overlay);

                imguiRenderer.Render(d3dHost.SwapChain);
                d3dHost.Present();

                Thread.Sleep(16);
            }
        }

        private static void DrawDemoUi()
        {
            var io = ImGui.GetIO();
            var bg = ImGui.GetBackgroundDrawList();

            uint tintColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.35f));

            //bg.AddRectFilled(
            //    new Vector2(0, 0),
            //    io.DisplaySize,
            //    tintColor
            //);

            ImGui.Begin("Overlay Demo");
            ImGui.SetWindowPos(new Vector2(10, 10), ImGuiCond.Once);
            HitTestRegions.AddCurrentWindow();

            ImGui.Text("Hello from Overlay-Renderer!");
            ImGui.Text("This ImGui window should block clicks behind it.");
            ImGui.Text("Drag it around; the hit-test region will follow.");

            ImGui.End();
        }
    }
}