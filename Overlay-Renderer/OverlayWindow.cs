using Overlay_Renderer.Helpers;
using Overlay_Renderer.Methods;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Overlay_Renderer;

public sealed class OverlayWindow : IDisposable
{
    private const int DefaultClientWidth = 1280;
    private const int DefaultClientHeight = 720;
    private const uint DwmBlurEnableFlag = 0x1u;
    private const int HTCLIENT = 1;
    private const int HTTRANSPARENT = -1;
    private const int RGN_OR = 2;
    private const int WM_DROPFILES = 0x0233;
    private int _hitInflatePx = 4;

    private bool _isVisible;
    private bool _forcePassThrough;

    private const string WindowClassName = "OverlayRendererOverlayWindowClass";

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);

    [DllImport("gdi32.dll")]
    private static extern int CombineRgn(IntPtr hrgnDest, IntPtr hrgnSrc1, IntPtr hrgnSrc2, int fnCombineMode);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    private readonly HWND _ownerHwnd;
    private readonly WNDPROC _wndProcThunk;
    private RECT[] _hitRegions = Array.Empty<RECT>();
    private readonly List<RECT> _lastRegions = new();
    private static readonly List<(string Path, Point Pt)> _pendingFileDrops = new();

    public int ClientWidth { get; private set; } = DefaultClientWidth;
    public int ClientHeight { get; private set; } = DefaultClientHeight;

    public bool Visible
    {
        get => _isVisible;
        set
        {
            _isVisible = value;
            PInvoke.ShowWindow(Hwnd, value ? SHOW_WINDOW_CMD.SW_SHOW : SHOW_WINDOW_CMD.SW_HIDE);
        }
    }

    public readonly HWND Hwnd;

    public void SetHitRegionInflate(int pixels)
    {
        _hitInflatePx = Math.Max(0, pixels);
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

                var ex = WINDOW_EX_STYLE.WS_EX_TOOLWINDOW;
                var style = WINDOW_STYLE.WS_POPUP;

                Hwnd = PInvoke.CreateWindowEx(
                    ex,
                    wc.lpszClassName,
                    (PCWSTR)null,
                    style,
                    0, 0, 100, 100,
                    HWND.Null,
                    HMENU.Null,
                    PInvoke.GetModuleHandle((PCWSTR)null),
                    null);
            }
        }

        unsafe
        {
            var bb = new DWM_BLURBEHIND
            {
                fEnable = false,
                dwFlags = DwmBlurEnableFlag
            };
            PInvoke.DwmEnableBlurBehindWindow(Hwnd, bb);
        }

        PInvoke.DragAcceptFiles(Hwnd, true);
        PInvoke.ShowWindow(Hwnd, SHOW_WINDOW_CMD.SW_HIDE);
        PInvoke.UpdateWindow(Hwnd);
    }

    public void SetHitTestRegions(ReadOnlySpan<RECT> regions)
    {
        _hitRegions = regions.ToArray();

        var inflated = new RECT[_hitRegions.Length];
        for (int i = 0; i < _hitRegions.Length; i++)
        {
            var r = _hitRegions[i];
            r.left -= _hitInflatePx;
            r.top -= _hitInflatePx;
            r.right += _hitInflatePx;
            r.bottom += _hitInflatePx;

            r.left = Math.Max(0, r.left);
            r.top = Math.Max(0, r.top);
            r.right = Math.Min(ClientWidth, r.right);
            r.bottom = Math.Min(ClientHeight, r.bottom);

            if (r.right < r.left) r.right = r.left;
            if (r.bottom < r.top) r.bottom = r.top;

            inflated[i] = r;
        }

        bool changed = inflated.Length != _lastRegions.Count;
        if (!changed)
        {
            for (int i = 0; i < inflated.Length; i++)
            {
                if (!inflated[i].Equals(_lastRegions[i])) { changed = true; break; }
            }
        }
        if (!changed) return;

        _lastRegions.Clear();
        _lastRegions.AddRange(inflated);

        IntPtr unionRgn = IntPtr.Zero;

        for (int i = 0; i < inflated.Length; i++)
        {
            var r = inflated[i];
            IntPtr rectRgn = CreateRectRgn(r.left, r.top, r.right, r.bottom);
            if (rectRgn == IntPtr.Zero)
                continue;

            if (unionRgn == IntPtr.Zero)
                unionRgn = rectRgn;
            else
            {
                CombineRgn(unionRgn, unionRgn, rectRgn, RGN_OR);
                DeleteObject(rectRgn);
            }
        }

        PInvoke.SetWindowRgn(Hwnd,
            (HRGN)(unionRgn == IntPtr.Zero ? IntPtr.Zero : unionRgn),
            true);
    }

    public void ToggleClickThrough() => _forcePassThrough = !_forcePassThrough;

    public void UpdateBounds(RECT screenRect)
    {
        int w = screenRect.right - screenRect.left;
        int h = screenRect.bottom - screenRect.top;
        ClientWidth = Math.Max(1, w);
        ClientHeight = Math.Max(1, h);

        var flags = SET_WINDOW_POS_FLAGS.SWP_NOSENDCHANGING;
        if (PInvoke.GetForegroundWindow() != Hwnd) flags |= SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE;

        PInvoke.SetWindowPos(
            Hwnd,
            new HWND(-1),
            screenRect.left, screenRect.top,
            w, h,
            flags);
    }

    private static unsafe void HandleDropFiles(WPARAM wParam)
    {
        var hDrop = new Windows.Win32.UI.Shell.HDROP((void*)wParam.Value);

        Point pt = new();
        PInvoke.DragQueryPoint(hDrop, &pt);
        uint fileCount = PInvoke.DragQueryFile(hDrop, 0xFFFFFFFF, null, 0);

        if (fileCount == 0)
        {
            PInvoke.DragFinish(hDrop);
            return;
        }

        uint maxLen = 0;
        for (uint i = 0; i < fileCount; i++)
            maxLen = Math.Max(maxLen, PInvoke.DragQueryFile(hDrop, i, null, 0));

        Span<char> buffer = stackalloc char[(int)maxLen + 1];

        for (uint i = 0; i < fileCount; i++)
        {
            fixed (char* p = buffer)
            {
                uint len = PInvoke.DragQueryFile(hDrop, i, p, maxLen + 1);
                string path = new(p, 0, (int)len);

                lock (_pendingFileDrops)
                    _pendingFileDrops.Add((path, pt));
            }
        }

        PInvoke.DragFinish(hDrop);
    }

    public static List<(string Path, Point Pt)> TakePendingFileDrops()
    {
        lock (_pendingFileDrops)
        {
            var copy = new List<(string Path, Point Pt)>(_pendingFileDrops);
            _pendingFileDrops.Clear();
            return copy;
        }
    }

    private LRESULT WndProc(HWND hwnd, uint msg, WPARAM wParam, LPARAM lParam)
    {
        switch (msg)
        {
            case PInvoke.WM_NCHITTEST:
            {
                if (_forcePassThrough)
                    return new LRESULT(HTTRANSPARENT);

                int sx = (short)((ulong)lParam.Value & 0xFFFF);
                int sy = (short)(((ulong)lParam.Value >> 16) & 0xFFFF);
                var pt = new Point { X = sx, Y = sy };
                PInvoke.ScreenToClient(hwnd, ref pt);

                bool inside = ContainsPointInHitRegions(pt.X, pt.Y);
                return new LRESULT(inside ? HTCLIENT : HTTRANSPARENT);
            }

            case PInvoke.WM_MOUSEWHEEL:
            {
                int w = (int)wParam.Value;
                short delta = (short)((w >> 16) & 0xFFFF);
                float wheel = delta / (float)PInvoke.WHEEL_DELTA;

                var io = ImGuiNET.ImGui.GetIO();
                if (io.KeyShift) io.MouseWheelH += wheel;
                else io.MouseWheel += wheel;
                return new LRESULT(0);
            }

            case PInvoke.WM_MOUSEHWHEEL:
            {
                short delta = (short)((wParam.Value.ToUInt64() >> 16) & 0xffff);
                float wheel = delta / (float)PInvoke.WHEEL_DELTA;
                ImGuiInput.OnMouseWheel(wheel, 0f);
                return new LRESULT(0);
            }

            case PInvoke.WM_SETCURSOR:
            {
                int hitTest = (short)((ulong)lParam.Value & 0xFFFF);
                if (hitTest == HTCLIENT)
                {
                    ImGuiInput.UpdateMouseCursor();
                    return new LRESULT(1);
                }
                return PInvoke.DefWindowProc(hwnd, msg, wParam, lParam);
            }

            case PInvoke.WM_CHAR:
                return PInvoke.DefWindowProc(hwnd, msg, wParam, lParam);

            case WM_DROPFILES:
                HandleDropFiles(wParam);
                return new LRESULT(0);
        }

        return PInvoke.DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private bool ContainsPointInHitRegions(int x, int y)
    {
        for (int i = 0; i < _hitRegions.Length; i++)
        {
            var r = _hitRegions[i];
            if (x >= r.left && x < r.right && y >= r.top && y < r.bottom)
                return true;
        }
        return false;
    }

    public void Dispose()
    {
        if (!Hwnd.IsNull)
            PInvoke.DestroyWindow(Hwnd);
    }
}
