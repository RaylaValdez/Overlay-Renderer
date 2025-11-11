using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Overlay_Renderer.ImGuiRenderer;

namespace Overlay_Renderer.Methods
{
    /// <summary>
    /// Central place to create and cache ImGui textures (files + URLs).
    /// Starboard and applets can call into this.
    /// </summary>
    public static class TextureService
    {
        private static ImGuiRendererD3D11? _renderer;
        private static readonly HttpClient _httpClient = new();
        private static readonly ConcurrentDictionary<string, nint> _urlCache = new();

        public static void Initialize(ImGuiRendererD3D11 renderer)
        {
            _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        }

        public static nint LoadTextureFromFile(string path, out int width, out int height)
        {
            if (_renderer == null)
                throw new InvalidOperationException("TextureService not initialized.");

            return _renderer.CreateTextureFromFile(path, out width, out height);
        }

        public static void DestroyTexture(nint id)
        {
            if (_renderer == null)
                throw new InvalidOperationException("TextureService not initialized.");

            _renderer.DestroyTexture(id);
        }

        /// <summary>
        /// Synchronous helper – fine for testing. Downloads the image and turns it into a texture.
        /// </summary>
        public static nint LoadTextureFromUrl(string url)
        {
            return LoadTextureFromUrlAsync(url).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Download an image from a URL, turn it into an ImGui texture, and cache by URL.
        /// </summary>
        public static async Task<nint> LoadTextureFromUrlAsync(string url)
        {
            if (_renderer == null)
                throw new InvalidOperationException("TextureService not initialized.");

            if (string.IsNullOrWhiteSpace(url))
                throw new ArgumentException("URL cannot be null or empty.", nameof(url));

            if (_urlCache.TryGetValue(url, out var cached))
                return cached;

            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead)
                                                  .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using var bmp = new Bitmap(stream);

            int w, h;
            var id = _renderer.CreateTextureFromBitmap(bmp, out w, out h);

            _urlCache[url] = id;
            return id;
        }

        /// <summary>
        /// Turn an existing Bitmap into an ImGui Texture
        /// Does NOT dispose the bitmap - caller owns it
        /// </summary>
        public static nint CreateTextureFromBitmap(Bitmap bmp, out int width, out int height)
        {
            if (_renderer == null)
                throw new InvalidOperationException("TextureService not initialized.");

            return _renderer.CreateTextureFromBitmap(bmp, out width, out height);
        }
    }
}
