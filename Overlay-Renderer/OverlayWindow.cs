using System;
using System.Drawing;
using System.Runtime.InteropServices;
using Overlay_Renderer.Methods;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.UI.WindowsAndMessaging;
using Overlay_Renderer;

namespace Overlay_Renderer;

internal sealed class OverlayWindow : IDisposable
{
    private const string WindowClassName = "OverlayRendererOverlayWindowClass";
    private const int DefaultClientWidth = 1280;
    private const int DefaultClientHeight = 720;
    private const uint DwmBlurEnableFlag = 0x1u;

    private readonly HWND _ownerHwnd;
    private WNDPROC _wndProcThunk;
    public readonly HWND Hwnd;

    public int ClientWidth { get; private set; } = DefaultClientWidth;
    public int ClientHeight { get; private set; } = DefaultClientHeight;

    private bool _isVisible = false;
    private bool _forcePassThrough = false;
    private RECT[] _hitRegions = Array.Empty<RECT>(); // client-space rects

    // Hit test values for WM_NCHITTEST return
    private const int HTCLIENT = 1;
    private const int HTTRANSPARENT = -1;


    public bool Visible
    {
        get => _isVisible;
        set
        {
            _isVisible = value;
            PInvoke.ShowWindow(Hwnd, value
                ? SHOW_WINDOW_CMD.SW_SHOWNOACTIVATE
                : SHOW_WINDOW_CMD.SW_HIDE);
        }
    }

    public OverlayWindow(HWND owner)
    {
        _ownerHwnd = owner;
        _wndProcThunk = new WNDPROC(WndProc);

        unsafe
        {
            fixed (char* pClass = WindowClassName)
            {
                var wc = new WNDCLASSEXW
                {
                    cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
                    lpfnWndProc = _wndProcThunk,
                    hInstance = PInvoke.GetModuleHandle((PCWSTR)null),
                    lpszClassName = new PCWSTR(pClass)
                };
                PInvoke.RegisterClassEx(wc);

                // WS_EX_TRANSPARENT alone does NOT make the window click-through.
                // We implement selective pass-through via WM_NCHITTEST instead.
                var ex = WINDOW_EX_STYLE.WS_EX_TOOLWINDOW
                          | WINDOW_EX_STYLE.WS_EX_NOACTIVATE
                          | WINDOW_EX_STYLE.WS_EX_TRANSPARENT;
                var style = WINDOW_STYLE.WS_POPUP;

                Hwnd = PInvoke.CreateWindowEx(
                    ex,
                    wc.lpszClassName,
                    (PCWSTR)null,
                    style,
                    0,
                    0,
                    100,
                    100,
                    _ownerHwnd,
                    HMENU.Null,
                    PInvoke.GetModuleHandle((PCWSTR)null),
                    null);
            }
        }

        // Disable DWM blur
        unsafe
        {
            var bb = new DWM_BLURBEHIND
            {
                fEnable = false,
                dwFlags = DwmBlurEnableFlag,
                hRgnBlur = default,
                fTransitionOnMaximized = false
            };
            PInvoke.DwmEnableBlurBehindWindow(Hwnd, bb);
        }

        PInvoke.ShowWindow(Hwnd, SHOW_WINDOW_CMD.SW_HIDE);
        PInvoke.UpdateWindow(Hwnd);
    }

    /// <summary>
    /// Update the interactive regions (client coords).
    /// Called by HitTestRegions.ApplyToOverlay once per frame.
    /// </summary>
    public void SetHitTestRegions(ReadOnlySpan<RECT> regions)
    {
        _hitRegions = regions.ToArray();
    }

    /// <summary>
    /// Force everything to pass-through (kept for future use).
    /// </summary>
    public void ToggleClickThrough() => _forcePassThrough = !_forcePassThrough;

    /// <summary>
    /// Mirror the owner’s client rect (screen coords in).
    /// </summary>
    public void UpdateBounds(RECT screenRect)
    {
        int w = screenRect.right - screenRect.left;
        int h = screenRect.bottom - screenRect.top;
        ClientWidth = Math.Max(1, w);
        ClientHeight = Math.Max(1, h);

        PInvoke.SetWindowPos(
            Hwnd,
            HWND.Null,
            screenRect.left,
            screenRect.top,
            w,
            h,
            SET_WINDOW_POS_FLAGS.SWP_NOZORDER |
            SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE |
            SET_WINDOW_POS_FLAGS.SWP_NOSENDCHANGING);
    }

    private LRESULT WndProc(HWND hwnd, uint msg, WPARAM wParam, LPARAM lParam)
    {
        switch (msg)
        {
            case PInvoke.WM_NCHITTEST:
            {
                // Optional global “everything passes through” toggle.
                if (_forcePassThrough)
                    return (LRESULT)(nint)HTTRANSPARENT;

                // Extract screen coordinates from lParam
                long v = (long)lParam.Value;
                int x = (short)(v & 0xFFFF);
                int y = (short)((v >> 16) & 0xFFFF);

                var ptScreen = new Point(x, y);
                var ptClient = ptScreen;
                PInvoke.ScreenToClient(hwnd, ref ptClient);

                // Check if the point is inside any ImGui-defined hit region
                foreach (var r in _hitRegions)
                {
                    if (ptClient.X >= r.left && ptClient.X < r.right &&
                        ptClient.Y >= r.top && ptClient.Y < r.bottom)
                    {
                        // This area is handled by the overlay / ImGui
                        return (LRESULT)(nint)HTCLIENT;
                    }
                }

                // Everywhere else: let the click pass through to the window behind
                return (LRESULT)(nint)HTTRANSPARENT;
            }

            case PInvoke.WM_MOUSEWHEEL:
            {
                // This may rarely fire depending on styles, but we keep it
                // in case we later adjust window flags.
                int delta = (short)((ulong)wParam.Value >> 16);
                float steps = delta / 120.0f;
                ImGuiInput.AddMouseWheelDelta(steps);
                return (LRESULT)0;
            }

            case PInvoke.WM_MOUSEHWHEEL:
            {
                int delta = (short)((ulong)wParam.Value >> 16);
                float steps = delta / 120.0f;
                ImGuiInput.AddMouseWheelDelta(0f, steps);
                return (LRESULT)0;
            }

            case PInvoke.WM_DESTROY:
                PInvoke.PostQuitMessage(0);
                break;
        }

        return PInvoke.DefWindowProc(hwnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        if (!Hwnd.IsNull)
            PInvoke.DestroyWindow(Hwnd);
    }
}
