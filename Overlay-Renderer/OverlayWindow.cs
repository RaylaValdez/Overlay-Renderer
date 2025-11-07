using System;
using System.Drawing;
using System.Runtime.InteropServices;
using Overlay_Renderer.Methods;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.UI.WindowsAndMessaging;
using Overlay_Renderer;
using Windows.Win32.Graphics.Gdi;

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

    // GDI region ops for shaping the window
    private const int RGN_OR = 2;

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);

    [DllImport("gdi32.dll")]
    private static extern int CombineRgn(IntPtr hrgnDest, IntPtr hrgnSrc1, IntPtr hrgnSrc2, int fnCombineMode);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

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

    // Native window region handle (for shaping)
    private IntPtr _windowRegion = IntPtr.Zero;

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

        if (_hitRegions.Length == 0)
        {
            PInvoke.SetWindowRgn(Hwnd, (Windows.Win32.Graphics.Gdi.HRGN)IntPtr.Zero, true);
            return;
        }

        IntPtr unionRgn = IntPtr.Zero;

        for (int i = 0; i < _hitRegions.Length; i++)
        {
            var r = _hitRegions[i];
            IntPtr rectRgn = CreateRectRgn(r.left, r.top, r.right, r.bottom);
            if (rectRgn == IntPtr.Zero)
                continue;

            if (unionRgn == IntPtr.Zero)
            {
                unionRgn = rectRgn;
            }
            else
            {
                CombineRgn(unionRgn, unionRgn, rectRgn, RGN_OR);
                DeleteObject(rectRgn);
            }
        }

        if (unionRgn != IntPtr.Zero)
        {
            PInvoke.SetWindowRgn(Hwnd, (Windows.Win32.Graphics.Gdi.HRGN)unionRgn, true);
        }
        else
        {
            PInvoke.SetWindowRgn(Hwnd, (Windows.Win32.Graphics.Gdi.HRGN)IntPtr.Zero, true);
        }
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
                if (_forcePassThrough)
                    return new LRESULT(HTTRANSPARENT);

                return new LRESULT(HTCLIENT);
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
