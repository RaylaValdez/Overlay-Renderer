using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using ImGuiNET;
using Windows.Win32.Foundation;
using Overlay_Renderer;

namespace Overlay_Renderer.Methods
{
    public static class HitTestRegions
    {
        private static readonly List<RECT> _regions = new();

        ///<summary>
        ///Clear regions at start of each ImGui frame.
        ///</summary>
        public static void BeginFrame()
        {
            _regions.Clear();
        }

        ///<summary>
        ///Add a rectangle in ImGui/overlay client coordinates.
        ///</summary>
        public static void AddRect(float x, float y , float width, float height)
        {
            RECT rect = new()
            {
                left = (int)x,
                top = (int)y,
                right = (int)(x + width),
                bottom = (int)(y + height)
            };
            _regions.Add(rect);
        }

        public static void AddRect(Vector2 pos, Vector2 size)
            => AddRect(pos.X, pos.Y, size.X, size.Y);

        ///<summary>
        ///Convenience: add the current ImGui window as a hit region.
        ///Call between ImGui.Begin/End for that window.
        ///</summary>
        public static void AddCurrentWindow()
        {
            Vector2 pos = ImGui.GetWindowPos();
            Vector2 size = ImGui.GetWindowSize();
            AddRect(pos, size);
        }

        ///<summary>
        ///Apply all collected regions to the overlay window.
        ///Call once per frame after you've built your ImGui UI.
        ///</summary>
        public static void ApplyToOverlay(OverlayWindow overlay)
        {
            if (_regions.Count == 0)
            {
                overlay.SetHitTestRegions(ReadOnlySpan<RECT>.Empty);
                return;
            }
            
            // Clamp rects to overlay client size
            for (int i = 0; i < _regions.Count; i++)
            {
                var r = _regions[i];
                r.left = Math.Max(0, r.left);
                r.top = Math.Max(0, r.top);
                r.right = Math.Min(overlay.ClientWidth, r.right);
                r.bottom = Math.Min(overlay.ClientHeight, r.bottom);
                _regions[i] = r;
            }
            
            var span = CollectionsMarshal.AsSpan(_regions);
            overlay.SetHitTestRegions(span);
        }
    }
}
