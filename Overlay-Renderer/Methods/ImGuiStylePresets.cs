using ImGuiNET;
using System.Numerics;

namespace Overlay_Renderer.Methods
{
    public class ImGuiStylePresets
    {
        public static void ApplyDark()
        {
            var style = ImGui.GetStyle();
            var colors = style.Colors;

            style.WindowRounding = 12f;
            style.FrameRounding = 8f;
            style.ScrollbarRounding = 12f;
            style.GrabRounding = 6f;
            style.FrameBorderSize = 0f;
            style.WindowBorderSize = 1f;
            style.IndentSpacing = 22f;
            style.WindowPadding = new Vector2(8, 8);
            style.ItemSpacing = new Vector2(6, 4);

            colors[(int)ImGuiCol.Text] = new Vector4(1.00f, 1.00f, 1.00f, 1.00f);
            colors[(int)ImGuiCol.TextDisabled] = new Vector4(0.50f, 0.50f, 0.50f, 1.00f);
            colors[(int)ImGuiCol.WindowBg] = new Vector4(0.145f, 0.157f, 0.161f, 0.90f);
            colors[(int)ImGuiCol.ChildBg] = new Vector4(0.00f, 0.00f, 0.00f, 0.00f);
            colors[(int)ImGuiCol.PopupBg] = new Vector4(0.08f, 0.08f, 0.08f, 0.94f);
            colors[(int)ImGuiCol.Border] = new Vector4(0.43f, 0.43f, 0.50f, 0.50f);
            colors[(int)ImGuiCol.FrameBg] = new Vector4(0.20f, 0.20f, 0.20f, 1.00f);
            colors[(int)ImGuiCol.FrameBgHovered] = new Vector4(0.25f, 0.25f, 0.25f, 1.00f);
            colors[(int)ImGuiCol.FrameBgActive] = new Vector4(0.30f, 0.30f, 0.30f, 1.00f);
            colors[(int)ImGuiCol.TitleBg] = new Vector4(0.04f, 0.04f, 0.04f, 1.00f);
            colors[(int)ImGuiCol.TitleBgActive] = new Vector4(0.10f, 0.10f, 0.10f, 1.00f);
            colors[(int)ImGuiCol.MenuBarBg] = new Vector4(0.14f, 0.14f, 0.14f, 1.00f);
            colors[(int)ImGuiCol.ScrollbarBg] = new Vector4(0.0f, 0.0f, 0.0f, 0.0f);
            colors[(int)ImGuiCol.ScrollbarGrab] = new Vector4(0.31f, 0.31f, 0.31f, 1.00f);
            colors[(int)ImGuiCol.ScrollbarGrabHovered] = new Vector4(0.41f, 0.41f, 0.41f, 1.00f);
            colors[(int)ImGuiCol.ScrollbarGrabActive] = new Vector4(0.51f, 0.51f, 0.51f, 1.00f);
            colors[(int)ImGuiCol.CheckMark] = new Vector4(0.584f, 0.655f, 0.675f, 1.00f);
            colors[(int)ImGuiCol.SliderGrab] = new Vector4(0.584f, 0.655f, 0.675f, 1.00f);
            colors[(int)ImGuiCol.SliderGrabActive] = new Vector4(0.2f, 0.9f, 0.4f, 0.9f);
            colors[(int)ImGuiCol.Button] = new Vector4(0.35f, 0.35f, 0.35f, 0.40f);
            colors[(int)ImGuiCol.ButtonHovered] = new Vector4(0.45f, 0.45f, 0.45f, 1.00f);
            colors[(int)ImGuiCol.ButtonActive] = new Vector4(0.584f, 0.655f, 0.675f, 1.00f);

        }

        public static void ApplyLight()
        {
            var style = ImGui.GetStyle();
            var colors = style.Colors;

            ImGui.StyleColorsLight();

            style.WindowRounding = 4f;
            style.FrameRounding = 2f;
            style.ScrollbarRounding = 4f;
            style.GrabRounding = 2f;
            style.FrameBorderSize = 0f;
        }

        public static void ApplyRetroTerminal()
        {
            var style = ImGui.GetStyle();
            var colors = style.Colors;

            style.WindowRounding = 0f;
            style.FrameRounding = 0f;
            style.ScrollbarRounding = 0f;
            style.GrabRounding = 0f;
            style.WindowBorderSize = 1f;
            style.FrameBorderSize = 1f;

            var green = new Vector4(0.1f, 1.0f, 0.1f, 1.0f);
            var black = new Vector4(0.0f, 0.0f, 0.0f, 1.0f);
            var dark = new Vector4(0.0f, 0.2f, 0.0f, 1.0f);

            colors[(int)ImGuiCol.Text] = green;
            colors[(int)ImGuiCol.WindowBg] = black;
            colors[(int)ImGuiCol.ChildBg] = black;
            colors[(int)ImGuiCol.Border] = dark;
            colors[(int)ImGuiCol.FrameBg] = new Vector4(0.0f, 0.3f, 0.0f, 1.0f);
            colors[(int)ImGuiCol.FrameBgHovered] = new Vector4(0.0f, 0.5f, 0.0f, 1.0f);
            colors[(int)ImGuiCol.FrameBgActive] = new Vector4(0.0f, 0.7f, 0.0f, 1.0f);
            colors[(int)ImGuiCol.Button] = new Vector4(0.0f, 0.3f, 0.0f, 1.0f);
            colors[(int)ImGuiCol.ButtonHovered] = new Vector4(0.0f, 0.5f, 0.0f, 1.0f);
            colors[(int)ImGuiCol.ButtonActive] = new Vector4(0.0f, 0.7f, 0.0f, 1.0f);
            colors[(int)ImGuiCol.TitleBg] = dark;
            colors[(int)ImGuiCol.TitleBgActive] = dark;
        }
    }
}
