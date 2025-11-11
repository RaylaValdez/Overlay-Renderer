using Windows.Win32.Foundation;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DirectComposition;
using Vortice.DXGI;
using Vortice.Mathematics;
using static Vortice.Direct3D11.D3D11;
using static Vortice.DirectComposition.DComp;


namespace Overlay_Renderer;

/// <summary>
/// Hosts the Direct3D11 device and a composition swap chain wired to a target HWND.
/// This renders a premultiplied-alpha surface and hands it to DirectComposition so the
/// overlay can be fully transparent except where we draw.
/// </summary>
public sealed class D3DHost : IDisposable
{
    // --- Constants / "magic numbers" ---
    private const int InitialBackBufferWidth = 1;   // DirectComposition requires a swap chain; we'll resize later
    private const int InitialBackBufferHeight = 1;
    private const Format SwapChainFormat = Format.B8G8R8A8_UNorm; // Alpha-capable format compatible with composition
    private const int SwapChainBufferCount = 2;     // Double-buffering
    private const int PresentSyncInterval = 1;      // VSync on
    private static readonly Color4 ClearColor = new Color4(0, 0, 0, 0); // Fully transparent

    // --- Public device objects (used by the renderer) ---
    public readonly ID3D11Device Device;
    public readonly ID3D11DeviceContext Context;
    public readonly IDXGISwapChain1 SwapChain;

    // --- Private DXGI/DirectComposition plumbing ---
    private readonly IDXGIDevice _dxgiDevice;
    private readonly IDXGIAdapter _dxgiAdapter;
    private readonly IDXGIFactory2 _dxgiFactory;

    private readonly IDCompositionDevice _dcompDevice;
    private readonly IDCompositionTarget _dcompTarget;
    private readonly IDCompositionVisual _dcompVisual;

    private readonly HWND _targetHwnd;

    private int _backBufferWidth = InitialBackBufferWidth;
    private int _backBufferHeight = InitialBackBufferHeight;

    public D3DHost(HWND hwnd)
    {
        _targetHwnd = hwnd;

        // Create a BGRA-capable D3D11 device (BGRA is required for composition).
        D3D11CreateDevice(
            null,
            DriverType.Hardware,
            DeviceCreationFlags.BgraSupport,
            new[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0 },
            out Device,
            out _,
            out Context
        );

        _dxgiDevice = Device.QueryInterface<IDXGIDevice>();
        _dxgiDevice.GetAdapter(out _dxgiAdapter);
        _dxgiAdapter.GetParent(out _dxgiFactory);

        // Create a composition swap chain (premultiplied alpha) — we'll resize it every frame to match the overlay.
        var swapChainDesc = new SwapChainDescription1
        {
            Width = InitialBackBufferWidth,
            Height = InitialBackBufferHeight,
            Format = SwapChainFormat,
            Stereo = false,
            SampleDescription = new SampleDescription(1, 0),
            BufferUsage = Usage.RenderTargetOutput,
            BufferCount = SwapChainBufferCount,
            Scaling = Scaling.Stretch,
            SwapEffect = SwapEffect.FlipDiscard,
            AlphaMode = AlphaMode.Premultiplied,
            Flags = 0
        };

        SwapChain = _dxgiFactory.CreateSwapChainForComposition(Device, swapChainDesc);

        // Hook the swap chain into DirectComposition so Windows will composite it as a transparent overlay.
        _dcompDevice = DCompositionCreateDevice<IDCompositionDevice>(_dxgiDevice);

        // CreateTargetForHwnd uses an out-parameter in Vortice 3.6.x – store in a local,
        // then assign to readonly field (allowed in a constructor).
        IDCompositionTarget dcompTargetLocal;
        _dcompDevice.CreateTargetForHwnd(_targetHwnd, true, out dcompTargetLocal);
        _dcompTarget = dcompTargetLocal;

        _dcompVisual = _dcompDevice.CreateVisual();
        _dcompVisual.SetContent(SwapChain);
        _dcompTarget.SetRoot(_dcompVisual);
        _dcompDevice.Commit();
    }

    /// <summary>
    /// Bind and clear the current back buffer to a fully transparent color.
    /// </summary>
    public void BeginFrame()
    {
        using var backBuffer = SwapChain.GetBuffer<ID3D11Texture2D>(0);
        using var renderTargetView = Device.CreateRenderTargetView(backBuffer);
        Context.OMSetRenderTargets(renderTargetView, null);
        Context.ClearRenderTargetView(renderTargetView, ClearColor);
    }

    /// <summary>
    /// Present with vsync and commit the DirectComposition tree.
    /// </summary>
    public void Present()
    {
        SwapChain.Present(PresentSyncInterval, PresentFlags.None);
        _dcompDevice.Commit();
    }

    /// <summary>
    /// Ensure the swap chain matches the overlay client size.
    /// Uses Format.Unknown and a buffer count of 0 to keep existing settings.
    /// </summary>
    public void EnsureSize(int width, int height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        if (width == _backBufferWidth && height == _backBufferHeight) return;

        // 0 = preserve buffer count, Format.Unknown = keep current format
        SwapChain.ResizeBuffers(0, (uint)width, (uint)height, Format.Unknown, 0);
        _backBufferWidth = width;
        _backBufferHeight = height;

        // Commit is cheap; keeps DComp state in sync.
        _dcompDevice.Commit();
    }

    public void Dispose()
    {
        _dcompVisual?.Dispose();
        _dcompTarget?.Dispose();
        _dcompDevice?.Dispose();
        SwapChain?.Dispose();
        _dxgiFactory?.Dispose();
        _dxgiAdapter?.Dispose();
        _dxgiDevice?.Dispose();
        Context?.Dispose();
        Device?.Dispose();
    }
}