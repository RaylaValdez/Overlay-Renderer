using System.Drawing;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace Overlay_Renderer.Methods
{
    public static class WindowTracker
    {
        public static Task StartTrackingAsync(
            HWND targetHwnd,
            OverlayWindow overlay,
            CancellationToken token,
            Action<int, int>? onSizeChanged = null)
        {
            return Task.Run(() =>
            {
                RECT lastRect = default;
                bool hasLast = false;

                while (!token.IsCancellationRequested)
                {
                    if (targetHwnd.IsNull || !PInvoke.IsWindow(targetHwnd))
                        break;

                    // Get client rect in client coords
                    if (!PInvoke.GetClientRect(targetHwnd, out RECT clientRect))
                    {
                        Thread.Sleep(50);
                        continue;
                    }

                    // Converting to screen coords
                    var topLeft = new Point { X = clientRect.left, Y = clientRect.top };
                    var bottomRight = new Point { X = clientRect.right, Y = clientRect.bottom };
                    PInvoke.ClientToScreen(targetHwnd, ref topLeft);
                    PInvoke.ClientToScreen(targetHwnd, ref bottomRight);

                    RECT screenRect = new()
                    {
                        left = topLeft.X,
                        top = topLeft.Y,
                        right = bottomRight.X,
                        bottom = bottomRight.Y
                    };

                    if (!hasLast || !RectsEqual(lastRect, screenRect))
                    {
                        hasLast = true;
                        lastRect = screenRect;

                        // Follow Window
                        overlay.UpdateBounds(screenRect);

                        int width = screenRect.right - screenRect.left;
                        int height = screenRect.bottom - screenRect.top;
                        onSizeChanged?.Invoke(width, height);
                    }

                    Thread.Sleep(16);
                }
            }, token);
        }
        private static bool RectsEqual(RECT a, RECT b) =>
            a.left == b.left && a.top == b.top && 
            a.right == b.right && a.bottom == b.bottom;
    }
}
