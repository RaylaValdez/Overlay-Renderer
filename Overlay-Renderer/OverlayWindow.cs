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

    // Hit-test result constants (from WinUser.h)
    private const int HTNOWHERE = 0;
    private const int HTCLIENT = 1;
    private const int HTTRANSPARENT = -1;

    // Mouse activate result constants
    private const int MA_ACTIVATE = 1;
    private const int MA_NOACTIVATE = 3;

    private readonly HWND _ownerHwnd;
    private WNDPROC _wndProcThunk;
    public readonly HWND Hwnd;

    public int ClientWidth { get; private set; } = DefaultClientWidth;
    public int ClientHeight { get; private set; } = DefaultClientHeight;

    private bool _isVisible = false;

    // Global override to force full pass-through (if you ever want a hotkey)
    private bool _forcePassThrough = false;

    // ImGui hit rectangles in client coordinates
    private RECT[] _hitRegions = Array.Empty<RECT>();

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

                // NOTE:
                //  - WS_EX_NOACTIVATE: never becomes the active/focused window
                //  - WS_EX_TOOLWINDOW: hides from Alt-Tab
                //  - NO WS_EX_TRANSPARENT here; we do fine-grained pass-through via WM_NCHITTEST.
                var ex = WINDOW_EX_STYLE.WS_EX_TOOLWINDOW
                          | WINDOW_EX_STYLE.WS_EX_NOACTIVATE;

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

        // Disable DWM blur (just in case)
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
            // Prevent activation even if Windows tries
            case PInvoke.WM_MOUSEACTIVATE:
            {
                // MA_NOACTIVATE: don't activate this window, but still allow the click
                return new LRESULT(MA_NOACTIVATE);
            }

            case PInvoke.WM_NCHITTEST:
            {
                // Global override: everything passes through
                if (_forcePassThrough)
                    return new LRESULT(HTTRANSPARENT);

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
                        return new LRESULT(HTCLIENT);
                    }
                }

                // Everywhere else: let the click pass through to the window behind
                return new LRESULT(HTTRANSPARENT);
            }

            case PInvoke.WM_MOUSEWHEEL:
            {
                int delta = (short)((ulong)wParam.Value >> 16);
                float steps = delta / 120.0f;
                ImGuiInput.AddMouseWheelDelta(steps);
                return new LRESULT(0);
            }

            case PInvoke.WM_MOUSEHWHEEL:
            {
                int delta = (short)((ulong)wParam.Value >> 16);
                float steps = delta / 120.0f;
                ImGuiInput.AddMouseWheelDelta(0f, steps);
                return new LRESULT(0);
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
