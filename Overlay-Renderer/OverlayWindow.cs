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
    private bool _forcePassThrough = false;        // still here for future use
    private RECT[] _hitRegions = Array.Empty<RECT>(); // client-space rects

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

                // IMPORTANT: WS_EX_TRANSPARENT makes the window hit-test transparent.
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
    /// Update the interactive regions (client coords). Kept for future hook-based input.
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
            // NOTE: No WM_NCHITTEST handler anymore – WS_EX_TRANSPARENT
            // makes the whole window hit-test transparent by default.

            case PInvoke.WM_MOUSEWHEEL:
            {
                // This will rarely fire with WS_EX_TRANSPARENT, but we keep it
                // in case we later change styles or add a non-transparent input window.
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
