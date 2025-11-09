using System.Drawing;
using System.Numerics;
using ImGuiNET;
using Windows.Win32;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using System.Runtime.InteropServices;
using Overlay_Renderer.Helpers;

namespace Overlay_Renderer.Methods
{
    public static class ImGuiInput
    {
        public static bool _useOsCursor = false;

        [DllImport("user32.dll", EntryPoint = "LoadCursor", CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadCursor(IntPtr hInstance, int lpCursorName);

        [DllImport("user32.dll")]
        private static extern IntPtr SetCursor(IntPtr hCursor);

        // Standard Cursor ID's
        private const int IDC_ARROW = 32512;
        private const int IDC_IBEAM = 32513;
        private const int IDC_HAND = 32649;
        private const int IDC_SIZEALL = 32646;
        private const int IDC_SIZENWSE = 32642;
        private const int IDC_SIZENESW = 32643;
        private const int IDC_SIZENS = 32645;
        private const int IDC_SIZEWE = 32644;

        // Mouse Wheel State (per frame)
        private static float _pendingWheelY;
        private static float _pendingWheelX;
        private static float _pendingWheel;

        // Keyboard States
        private static readonly (VIRTUAL_KEY vk, ImGuiKey key)[] KeyMap =
        {
            (VIRTUAL_KEY.VK_TAB,    ImGuiKey.Tab),
            (VIRTUAL_KEY.VK_LEFT,   ImGuiKey.LeftArrow),
            (VIRTUAL_KEY.VK_RIGHT,  ImGuiKey.RightArrow),
            (VIRTUAL_KEY.VK_UP,     ImGuiKey.UpArrow),
            (VIRTUAL_KEY.VK_DOWN,   ImGuiKey.DownArrow),
            (VIRTUAL_KEY.VK_PRIOR,  ImGuiKey.PageUp),
            (VIRTUAL_KEY.VK_NEXT,   ImGuiKey.PageDown),
            (VIRTUAL_KEY.VK_HOME,   ImGuiKey.Home),
            (VIRTUAL_KEY.VK_END,    ImGuiKey.End),
            (VIRTUAL_KEY.VK_INSERT, ImGuiKey.Insert),
            (VIRTUAL_KEY.VK_DELETE, ImGuiKey.Delete),
            (VIRTUAL_KEY.VK_BACK,   ImGuiKey.Backspace),
            (VIRTUAL_KEY.VK_SPACE,  ImGuiKey.Space),
            (VIRTUAL_KEY.VK_RETURN, ImGuiKey.Enter),
            (VIRTUAL_KEY.VK_ESCAPE, ImGuiKey.Escape),

            // Numbers
            (VIRTUAL_KEY.VK_0, ImGuiKey._0),
            (VIRTUAL_KEY.VK_1, ImGuiKey._1),
            (VIRTUAL_KEY.VK_2, ImGuiKey._2),
            (VIRTUAL_KEY.VK_3, ImGuiKey._3),
            (VIRTUAL_KEY.VK_4, ImGuiKey._4),
            (VIRTUAL_KEY.VK_5, ImGuiKey._5),
            (VIRTUAL_KEY.VK_6, ImGuiKey._6),
            (VIRTUAL_KEY.VK_7, ImGuiKey._7),
            (VIRTUAL_KEY.VK_8, ImGuiKey._8),
            (VIRTUAL_KEY.VK_9, ImGuiKey._9),

            // Letters
            (VIRTUAL_KEY.VK_A, ImGuiKey.A),
            (VIRTUAL_KEY.VK_B, ImGuiKey.B),
            (VIRTUAL_KEY.VK_C, ImGuiKey.C),
            (VIRTUAL_KEY.VK_D, ImGuiKey.D),
            (VIRTUAL_KEY.VK_E, ImGuiKey.E),
            (VIRTUAL_KEY.VK_F, ImGuiKey.F),
            (VIRTUAL_KEY.VK_G, ImGuiKey.G),
            (VIRTUAL_KEY.VK_H, ImGuiKey.H),
            (VIRTUAL_KEY.VK_I, ImGuiKey.I),
            (VIRTUAL_KEY.VK_J, ImGuiKey.J),
            (VIRTUAL_KEY.VK_K, ImGuiKey.K),
            (VIRTUAL_KEY.VK_L, ImGuiKey.L),
            (VIRTUAL_KEY.VK_M, ImGuiKey.M),
            (VIRTUAL_KEY.VK_N, ImGuiKey.N),
            (VIRTUAL_KEY.VK_O, ImGuiKey.O),
            (VIRTUAL_KEY.VK_P, ImGuiKey.P),
            (VIRTUAL_KEY.VK_Q, ImGuiKey.Q),
            (VIRTUAL_KEY.VK_R, ImGuiKey.R),
            (VIRTUAL_KEY.VK_S, ImGuiKey.S),
            (VIRTUAL_KEY.VK_T, ImGuiKey.T),
            (VIRTUAL_KEY.VK_U, ImGuiKey.U),
            (VIRTUAL_KEY.VK_V, ImGuiKey.V),
            (VIRTUAL_KEY.VK_W, ImGuiKey.W),
            (VIRTUAL_KEY.VK_X, ImGuiKey.X),
            (VIRTUAL_KEY.VK_Y, ImGuiKey.Y),
            (VIRTUAL_KEY.VK_Z, ImGuiKey.Z),

            // Function keys (add more if you like)
            (VIRTUAL_KEY.VK_F1, ImGuiKey.F1),
            (VIRTUAL_KEY.VK_F2, ImGuiKey.F2),
            (VIRTUAL_KEY.VK_F3, ImGuiKey.F3),
            (VIRTUAL_KEY.VK_F4, ImGuiKey.F4),
            (VIRTUAL_KEY.VK_F5, ImGuiKey.F5),
            (VIRTUAL_KEY.VK_F6, ImGuiKey.F6),
            (VIRTUAL_KEY.VK_F7, ImGuiKey.F7),
            (VIRTUAL_KEY.VK_F8, ImGuiKey.F8),
            (VIRTUAL_KEY.VK_F9, ImGuiKey.F9),
            (VIRTUAL_KEY.VK_F10, ImGuiKey.F10),
            (VIRTUAL_KEY.VK_F11, ImGuiKey.F11),
            (VIRTUAL_KEY.VK_F12, ImGuiKey.F12),
        };

        private static readonly bool[] KeyDown = new bool[KeyMap.Length];

        /// <summary>
        /// Update ImGui mouse position and button state based on the current cursor.
        /// Call once per frame before building your ImGui UI.
        /// </summary>
        public static void UpdateMouse(OverlayWindow overlay)
        {
            var io = ImGui.GetIO();

            // If overlay window is not created yet, mark mouse as outside
            if (overlay.Hwnd.IsNull)
            {
                io.MousePos = new Vector2(-1, -1);
                io.MouseDown[0] = io.MouseDown[1] = io.MouseDown[2] = false;
                io.MouseWheel = 0;
                io.MouseWheelH = 0;
                return;
            }

            // Get cursor in screen coords
            PInvoke.GetCursorPos(out Point ptScreen);

            // Convert to overlay coords
            var ptClient = new Point { X = ptScreen.X, Y = ptScreen.Y };
            PInvoke.ScreenToClient(overlay.Hwnd, ref ptClient);

            io.MousePos = new Vector2(ptClient.X, ptClient.Y);

            // Button states (global) - works regardless of which window has focus
            io.MouseDown[0] = (PInvoke.GetAsyncKeyState((int)VIRTUAL_KEY.VK_LBUTTON) & 0x8000) != 0;
            io.MouseDown[1] = (PInvoke.GetAsyncKeyState((int)VIRTUAL_KEY.VK_RBUTTON) & 0x8000) != 0;
            io.MouseDown[2] = (PInvoke.GetAsyncKeyState((int)VIRTUAL_KEY.VK_MBUTTON) & 0x8000) != 0;


            // Apply wheel accumulated from WndProc
            io.AddMouseWheelEvent(_pendingWheelX, _pendingWheelY);
            _pendingWheelY = 0;
            _pendingWheelX = 0;
        }

        /// <summary>
        /// Update ImGui keyboard states
        /// Call once per frame before building ImGui UI
        /// </summary>
        public static void UpdateKeyboard()
        {
            var io = ImGui.GetIO();

            // Modifiers (global)
            io.KeyCtrl = (PInvoke.GetAsyncKeyState((int)VIRTUAL_KEY.VK_CONTROL) & 0x8000) != 0;
            io.KeyShift = (PInvoke.GetAsyncKeyState((int)VIRTUAL_KEY.VK_SHIFT) & 0x8000) != 0;
            io.KeyAlt = (PInvoke.GetAsyncKeyState((int)VIRTUAL_KEY.VK_MENU) & 0x8000) != 0;
            io.KeySuper = (PInvoke.GetAsyncKeyState((int)VIRTUAL_KEY.VK_LWIN) & 0x8000) != 0 || (PInvoke.GetAsyncKeyState((int)VIRTUAL_KEY.VK_RWIN) & 0x8000) != 0;
            
            // Keys 
            for (int i = 0; i < KeyMap.Length; i++)
            {
                var (vk, key) = KeyMap[i];
                bool downNow = (PInvoke.GetAsyncKeyState((int)vk) & 0x8000) != 0;

                if (downNow != KeyDown[i])
                {
                    KeyDown[i] = downNow;
                    io.AddKeyEvent(key, downNow);
                }
            }
        }


        public static void OnMouseWheel(float deltaX, float deltaY)
        {
            _pendingWheelY += deltaY;
            _pendingWheelX += deltaX;
        }

        ///<summary>
        ///Update the OS cursor to match ImGui's requested cursor,
        ///Call this from WM_SETCURSOR in the overlay window
        ///</summary>
        public static void UpdateMouseCursor()
        {
            var io = ImGui.GetIO();

            if ((io.ConfigFlags & ImGuiConfigFlags.NoMouseCursorChange) != 0)
                return;

            var imguiCursor = ImGui.GetMouseCursor();

            if (imguiCursor == ImGuiMouseCursor.None || io.MouseDrawCursor)
            {
                SetCursor(IntPtr.Zero);
                return;
            }

            IntPtr hCursor = IntPtr.Zero;

            switch (imguiCursor)
            {
                default:
                case ImGuiMouseCursor.Arrow:
                    hCursor = LoadCursor(IntPtr.Zero, IDC_ARROW);
                    break;

                case ImGuiMouseCursor.TextInput:
                    hCursor = LoadCursor(IntPtr.Zero, IDC_IBEAM);
                    break;

                case ImGuiMouseCursor.ResizeAll:
                    hCursor = LoadCursor(IntPtr.Zero, IDC_SIZEALL);
                    break;

                case ImGuiMouseCursor.ResizeNS:
                    hCursor = LoadCursor(IntPtr.Zero, IDC_SIZENS);
                    break;

                case ImGuiMouseCursor.ResizeEW:
                    hCursor = LoadCursor(IntPtr.Zero, IDC_SIZEWE);
                    break;

                case ImGuiMouseCursor.ResizeNESW:
                    hCursor = LoadCursor(IntPtr.Zero, IDC_SIZENESW);
                    break;

                case ImGuiMouseCursor.ResizeNWSE:
                    hCursor = LoadCursor(IntPtr.Zero, IDC_SIZENWSE);
                    break;

                case ImGuiMouseCursor.Hand:
                    hCursor = LoadCursor(IntPtr.Zero, IDC_HAND);
                    break;

                case ImGuiMouseCursor.NotAllowed:
                    // No perfect stock cursor; arrow is usually fine, or you can pick something else.
                    hCursor = LoadCursor(IntPtr.Zero, IDC_ARROW);
                    break;
            }

            if (hCursor != IntPtr.Zero)
            {
                SetCursor(hCursor);
            }
        }

        public static void UseOsCursor(bool enable)
        {
            _useOsCursor = enable;

            var io = ImGui.GetIO();
            if (enable)
                io.ConfigFlags |= ImGuiConfigFlags.NoMouseCursorChange;
            else
                io.ConfigFlags &= ~ImGuiConfigFlags.NoMouseCursorChange;
        }

        public static bool TryGetPressedMappedKey(out VIRTUAL_KEY vk,out ImGuiKey imguiKey)
        {
            foreach (var (vkCode, key) in KeyMap)
            {
                if (ImGui.IsKeyPressed(key, false))
                {
                    vk = vkCode;
                    imguiKey = key;
                    return true;
                }
            }

            vk = 0;
            imguiKey = ImGuiKey.None;
            return false;
        }
    }
}
