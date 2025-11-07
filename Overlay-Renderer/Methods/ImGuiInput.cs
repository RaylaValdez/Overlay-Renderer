using System.Drawing;
using System.Numerics;
using ImGuiNET;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Overlay_Renderer;

namespace Overlay_Renderer.Methods
{
    internal class ImGuiInput
    {
        // Mouse Wheel State (per frame)
        private static float _pendingWheelY;
        private static float _pendingWheelX;

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
            io.MouseWheel += _pendingWheelY;
            io.MouseWheelH += _pendingWheelX;
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

        public static void AddMouseWheelDelta(float deltaY, float deltaX = 0f)
        {
            _pendingWheelY = deltaY;
            _pendingWheelX = deltaX;
        }
    }
}
