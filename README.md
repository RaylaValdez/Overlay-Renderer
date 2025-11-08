# 🪟 Overlay-Renderer

**Overlay-Renderer** is a lightweight, fully-customizable overlay framework for Direct3D 11 + ImGui.NET on Windows.

It’s designed for real-time overlays that render on top of existing applications (like games or productivity tools)  
without stealing focus or blocking input — except where you want it to.

---

## ✨ Features

- 🖼️ **True transparent overlay** – Renders ImGui windows and content over any window.
- 🧩 **Click-through regions** – Only interactive where your ImGui windows are.
- 🎨 **ImGui styling system** – Fully supports custom ImGui themes and style presets.
- 🧠 **Automatic window tracking** – Follows the target app’s position and size in real time.
- 🖱️ **Input passthrough logic** – Keyboard and mouse events go to the right target.
- ⚡ **Direct3D 11 renderer** – Efficient compositing through DirectComposition.
- 💬 **Simple integration** – Drop it into any .NET app and call a few setup methods.

---

## 🧱 Architecture Overview

Overlay-Renderer contains the following components:

| Component | Purpose |
|------------|----------|
| **`OverlayWindow`** | Manages the topmost transparent window, input hit-regions, focus behavior. |
| **`D3DHost`** | Handles Direct3D 11 device creation, swap chain, and frame rendering. |
| **`ImGuiRendererD3D11`** | Bridges ImGui.NET with D3D11 — responsible for frame lifecycle and texture uploads. |
| **`HitTestRegions`** | Calculates where the overlay should receive clicks (ImGui window bounds). |
| **`ImGuiInput`** | Feeds global mouse/keyboard state into ImGui. |
| **`WindowTracker`** | Keeps the overlay window aligned with its target application window. |
| **`FindProcess`** | Locates and waits for a process’ main window handle. |
| **`Logger`** | Simple thread-safe logging helper for console and file output. |

---

## 🧰 Technologies

- **.NET 8.0**
- **C# 11**
- **Direct3D 11** via [Win32 interop (CsWin32)](https://github.com/microsoft/CsWin32)
- **ImGui.NET** for GUI rendering
- **DirectComposition** for hardware-accelerated transparency
- **Windows API** (`HWND`, `RECT`, `SetWindowRgn`, `WM_NCHITTEST`, etc.)

---

## 🚀 Quick Start

### 1. Add the project reference

Add **Overlay-Renderer** to your solution and reference it from your app project:

```
Right-click your project → Add → Project Reference → check "Overlay-Renderer"
```

### 2. Create and attach an overlay

```csharp
using Overlay_Renderer;
using Overlay_Renderer.Methods;
using Overlay_Renderer.ImGuiRenderer;
using Overlay_Renderer.Helpers;
using ImGuiNET;
using Windows.Win32.Foundation;

IntPtr targetHwnd = FindProcess.WaitForMainWindow("SomeApp");
if (targetHwnd == IntPtr.Zero)
{
    Logger.Error("App not found!");
    return;
}

using var overlay = new OverlayWindow(new HWND(targetHwnd));
using var d3d = new D3DHost(overlay.Hwnd);
using var imgui = new ImGuiRendererD3D11(d3d.Device, d3d.Context);

// Optional: apply built-in style
ImGuiStylePresets.ApplyDark();

// Example render loop
while (true)
{
    d3d.BeginFrame();
    imgui.NewFrame(overlay.ClientWidth, overlay.ClientHeight);

    ImGui.Begin("Overlay Test");
    ImGui.Text("Hello from Overlay-Renderer!");
    HitTestRegions.AddCurrentWindow();
    ImGui.End();

    HitTestRegions.ApplyToOverlay(overlay);

    imgui.Render(d3d.SwapChain);
    d3d.Present();
}
```

This creates a fully functional overlay window aligned with the target process.

---

## 🧠 How It Works

Overlay-Renderer uses a layered, transparent, non-activating window (`WS_EX_LAYERED | WS_EX_NOACTIVATE`) and shapes it each frame using `SetWindowRgn()` to match your ImGui windows.  
That means:
- Areas outside ImGui are *not part of the window* (click-through).
- Areas under ImGui are fully interactive (for sliders, buttons, etc).
- The app underneath always keeps keyboard focus.

---

## 🎨 Styling

Custom styles live in `ImGuiStylePresets.cs`.  
You can define your own look easily:

```csharp
var style = ImGui.GetStyle();
style.WindowRounding = 6f;
style.FrameRounding = 3f;
style.Colors[(int)ImGuiCol.WindowBg] = new Vector4(0.1f, 0.1f, 0.1f, 0.94f);
```

Then apply it on startup:

```csharp
ImGuiStylePresets.ApplyDark();
```

---

## 🧩 Integrating with Other Projects

If you’re writing your own overlay (like **[Starboard](https://github.com/RaylaValdez/Starboard)**):

1. Add **Overlay-Renderer** as a **Project Reference**.
2. Import the namespaces:
   ```csharp
   using Overlay_Renderer;
   using Overlay_Renderer.Methods;
   using Overlay_Renderer.ImGuiRenderer;
   using Overlay_Renderer.Helpers;
   ```
3. Use the provided utilities to manage:
   - process detection (`FindProcess`)
   - overlay lifecycle (`OverlayWindow`, `D3DHost`)
   - ImGui integration (`ImGuiRendererD3D11`)
   - region updates (`HitTestRegions`)
   - window sync (`WindowTracker`)

---

## 🧪 Example Project

See **[Starboard](https://github.com/RaylaValdez/Starboard)** for a real-world integration example:
- Tracks the *Star Citizen* client window
- Draws an animated “pill” UI element
- Demonstrates region shaping, input routing, and DPI handling

---

## 🧰 Development

### Requirements
- Visual Studio 2022 or Rider
- .NET 8 SDK
- Windows 10+ with Direct3D 11

---

## ⚖️ License

MIT License — you’re free to use, modify, and distribute.

---

## 💬 Credits

Built with ❤️ by **Rayla**  
Powered by [ImGui.NET](https://github.com/mellinoe/ImGui.NET) and [Microsoft CsWin32](https://github.com/microsoft/CsWin32)

---
