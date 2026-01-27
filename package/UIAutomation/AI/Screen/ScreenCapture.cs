using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;

namespace ODDGames.UIAutomation.AI
{
    /// <summary>
    /// Captures screenshots with optional element annotation overlays.
    /// </summary>
    public static class AIScreenCapture
    {
        /// <summary>
        /// Maximum screenshot dimension for AI (keeps aspect ratio).
        /// 1024px is a good balance between clarity and file size.
        /// </summary>
        public static int MaxScreenshotDimension = 1024;

        /// <summary>
        /// JPEG quality for screenshots sent to AI (0-100).
        /// 75 gives good quality at ~1/10th the size of PNG.
        /// </summary>
        public static int JpegQuality = 75;

        /// <summary>
        /// Captures the current screen state including screenshot and element discovery.
        /// </summary>
        public static async UniTask<ScreenState> CaptureAsync(bool annotateScreenshot = true)
        {
            // Capture screenshot asynchronously
            Texture2D screenshot;
            try
            {
                screenshot = await CaptureScreenshotAsync();
            }
            catch (System.OperationCanceledException)
            {
                Debug.LogWarning("[AIScreenCapture] Screenshot capture cancelled");
                // Still discover elements even if screenshot failed
                var elementsOnly = ElementDiscovery.DiscoverElements();
                return new ScreenState
                {
                    Elements = elementsOnly,
                    Timestamp = Time.realtimeSinceStartup,
                    ElementStateHash = ScreenHash.ComputeElementStateHash(elementsOnly)
                };
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[AIScreenCapture] Screenshot capture failed: {ex.Message}");
                // Still discover elements even if screenshot failed
                var elementsOnly = ElementDiscovery.DiscoverElements();
                return new ScreenState
                {
                    Elements = elementsOnly,
                    Timestamp = Time.realtimeSinceStartup,
                    ElementStateHash = ScreenHash.ComputeElementStateHash(elementsOnly)
                };
            }

            if (screenshot == null)
            {
                Debug.LogWarning("[AIScreenCapture] Screenshot returned null");
                // Still discover elements even if screenshot is null
                var elementsOnly = ElementDiscovery.DiscoverElements();
                return new ScreenState
                {
                    Elements = elementsOnly,
                    Timestamp = Time.realtimeSinceStartup,
                    ElementStateHash = ScreenHash.ComputeElementStateHash(elementsOnly)
                };
            }

            // Discover elements
            var elements = ElementDiscovery.DiscoverElements();

            // Annotate screenshot if requested
            Texture2D annotated = null;
            if (annotateScreenshot && elements.Count > 0)
            {
                annotated = AnnotateScreenshot(screenshot, elements);
            }

            // Compute visual hash from original before any resizing
            var screenHash = ScreenHash.ComputeHash(screenshot);

            // Compute element state hash (detects toggle/slider/dropdown state changes)
            var elementStateHash = ScreenHash.ComputeElementStateHash(elements);

            // Get the texture to encode (annotated or original)
            var textureToEncode = annotated ?? screenshot;

            // Resize for AI efficiency (smaller = faster + cheaper)
            var resized = ResizeForAI(textureToEncode);
            var finalTexture = resized ?? textureToEncode;

            // Encode as JPEG for smaller file size (PNG is ~10x larger)
            var imageBytes = finalTexture.EncodeToJPG(JpegQuality);

            var result = new ScreenState
            {
                ScreenshotPng = imageBytes, // Still called Png but now JPEG
                Elements = elements,
                Timestamp = Time.realtimeSinceStartup,
                ScreenHash = screenHash,
                ElementStateHash = elementStateHash
            };

            // Clean up textures
            Object.Destroy(screenshot);
            if (annotated != null)
                Object.Destroy(annotated);
            if (resized != null)
                Object.Destroy(resized);

            return result;
        }

        /// <summary>
        /// Captures just the screenshot without element discovery.
        /// </summary>
        public static async UniTask<byte[]> CaptureScreenshotOnlyAsync()
        {
            try
            {
                var screenshot = await CaptureScreenshotAsync();
                if (screenshot == null) return null;

                // Resize and compress for efficiency
                var resized = ResizeForAI(screenshot);
                var finalTexture = resized ?? screenshot;
                var jpg = finalTexture.EncodeToJPG(JpegQuality);

                Object.Destroy(screenshot);
                if (resized != null)
                    Object.Destroy(resized);

                return jpg;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Resizes texture if it exceeds MaxScreenshotDimension while maintaining aspect ratio.
        /// Returns null if no resize needed.
        /// </summary>
        private static Texture2D ResizeForAI(Texture2D source)
        {
            if (source == null) return null;

            var width = source.width;
            var height = source.height;
            var maxDim = MaxScreenshotDimension;

            // Check if resize is needed
            if (width <= maxDim && height <= maxDim)
                return null;

            // Calculate new size maintaining aspect ratio
            float scale;
            if (width > height)
            {
                scale = (float)maxDim / width;
            }
            else
            {
                scale = (float)maxDim / height;
            }

            var newWidth = Mathf.RoundToInt(width * scale);
            var newHeight = Mathf.RoundToInt(height * scale);

            // Use RenderTexture for GPU-accelerated resize
            var rt = RenderTexture.GetTemporary(newWidth, newHeight, 0, RenderTextureFormat.ARGB32);
            rt.filterMode = FilterMode.Bilinear;

            var previous = RenderTexture.active;
            RenderTexture.active = rt;

            Graphics.Blit(source, rt);

            var resized = new Texture2D(newWidth, newHeight, TextureFormat.RGB24, false);
            resized.ReadPixels(new Rect(0, 0, newWidth, newHeight), 0, 0);
            resized.Apply();

            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);

            return resized;
        }

        /// <summary>
        /// Captures the current screen to a texture asynchronously.
        /// Uses AsyncGPUReadback to avoid blocking and "ReadPixels not inside drawing frame" errors.
        /// </summary>
        private static async UniTask<Texture2D> CaptureScreenshotAsync()
        {
            if (!Application.isPlaying)
                return null;

            // Create RenderTexture and capture into it (this works outside of rendering)
            var rt = RenderTexture.GetTemporary(Screen.width, Screen.height, 0, RenderTextureFormat.ARGB32);

            try
            {
                // Wait a frame to ensure rendering is complete
                try
                {
                    await UniTask.DelayFrame(1, PlayerLoopTiming.PostLateUpdate);
                }
                catch
                {
                    return null;
                }

                if (!Application.isPlaying)
                    return null;

                // Capture screen into RenderTexture
                ScreenCapture.CaptureScreenshotIntoRenderTexture(rt);

                // Use AsyncGPUReadback to read without blocking
                var tcs = new UniTaskCompletionSource<Texture2D>();

                _ = AsyncGPUReadback.Request(rt, 0, TextureFormat.RGBA32, (request) =>
                {
                    if (request.hasError || !Application.isPlaying)
                    {
                        tcs.TrySetResult(null);
                        return;
                    }

                    try
                    {
                        var width = Screen.width;
                        var height = Screen.height;
                        var rawData = request.GetData<byte>();

                        // GPU readback returns flipped image - we need to flip it vertically
                        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
                        var flippedData = new byte[rawData.Length];
                        int rowSize = width * 4; // RGBA32 = 4 bytes per pixel

                        for (int y = 0; y < height; y++)
                        {
                            int srcRow = (height - 1 - y) * rowSize;
                            int dstRow = y * rowSize;
                            for (int x = 0; x < rowSize; x++)
                            {
                                flippedData[dstRow + x] = rawData[srcRow + x];
                            }
                        }

                        texture.LoadRawTextureData(flippedData);
                        texture.Apply();
                        tcs.TrySetResult(texture);
                    }
                    catch
                    {
                        tcs.TrySetResult(null);
                    }
                });

                // Wait for result with timeout to prevent hanging
                var timeoutTask = UniTask.Delay(2000, ignoreTimeScale: true);
                var resultTask = tcs.Task;

                var (winIndex, timeoutResult, captureResult) = await UniTask.WhenAny(
                    timeoutTask.ContinueWith(() => (Texture2D)null),
                    resultTask
                );

                // winIndex 0 = timeout completed first, 1 = capture completed first
                return winIndex == 0 ? null : captureResult;
            }
            catch
            {
                return null;
            }
            finally
            {
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        /// <summary>
        /// Annotates a screenshot with bounding boxes and labels for elements.
        /// </summary>
        private static Texture2D AnnotateScreenshot(Texture2D source, List<ElementInfo> elements)
        {
            // Create a copy to annotate
            var annotated = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
            annotated.SetPixels(source.GetPixels());

            foreach (var element in elements)
            {
                // Get color based on element type
                var color = GetColorForType(element.type);

                // Convert screen-space bounds to texture-space bounds
                // Screen space: Y=0 at bottom, increases upward
                // Texture space: Y=0 at bottom for SetPixel, but our screenshot was flipped
                // So we need to flip Y coordinates to match the flipped screenshot
                var textureBounds = new Rect(
                    element.bounds.x,
                    source.height - element.bounds.y - element.bounds.height,
                    element.bounds.width,
                    element.bounds.height
                );

                // Draw bounding box
                DrawRect(annotated, textureBounds, color, 2);

                // Draw element ID label
                DrawLabel(annotated, element.id, textureBounds, color);
            }

            annotated.Apply();
            return annotated;
        }

        /// <summary>
        /// Gets a color for a given element type.
        /// </summary>
        private static Color GetColorForType(string type)
        {
            if (type.StartsWith("button")) return new Color(0.2f, 0.8f, 0.2f, 1f); // Green
            if (type.StartsWith("toggle")) return new Color(0.8f, 0.8f, 0.2f, 1f); // Yellow
            if (type.StartsWith("input")) return new Color(0.2f, 0.6f, 1f, 1f);    // Blue
            if (type.StartsWith("slider")) return new Color(0.8f, 0.4f, 0.2f, 1f); // Orange
            if (type.StartsWith("dropdown")) return new Color(0.8f, 0.2f, 0.8f, 1f); // Purple
            if (type.StartsWith("scroll")) return new Color(0.4f, 0.8f, 0.8f, 1f); // Cyan
            return new Color(0.7f, 0.7f, 0.7f, 1f); // Gray
        }

        /// <summary>
        /// Draws a rectangle outline on the texture.
        /// </summary>
        private static void DrawRect(Texture2D texture, Rect bounds, Color color, int thickness)
        {
            int x = Mathf.RoundToInt(bounds.x);
            int y = Mathf.RoundToInt(bounds.y);
            int w = Mathf.RoundToInt(bounds.width);
            int h = Mathf.RoundToInt(bounds.height);

            // Top edge
            for (int t = 0; t < thickness; t++)
            {
                for (int px = x; px < x + w; px++)
                {
                    SetPixelSafe(texture, px, y + h - t, color);
                }
            }

            // Bottom edge
            for (int t = 0; t < thickness; t++)
            {
                for (int px = x; px < x + w; px++)
                {
                    SetPixelSafe(texture, px, y + t, color);
                }
            }

            // Left edge
            for (int t = 0; t < thickness; t++)
            {
                for (int py = y; py < y + h; py++)
                {
                    SetPixelSafe(texture, x + t, py, color);
                }
            }

            // Right edge
            for (int t = 0; t < thickness; t++)
            {
                for (int py = y; py < y + h; py++)
                {
                    SetPixelSafe(texture, x + w - t, py, color);
                }
            }
        }

        /// <summary>
        /// Draws a simple label on the texture.
        /// </summary>
        private static void DrawLabel(Texture2D texture, string text, Rect bounds, Color color)
        {
            // Draw a small filled rectangle for the label background
            int labelWidth = text.Length * 8 + 4;
            int labelHeight = 12;
            int labelX = Mathf.RoundToInt(bounds.x);
            int labelY = Mathf.RoundToInt(bounds.y + bounds.height);

            // Background
            for (int px = labelX; px < labelX + labelWidth && px < texture.width; px++)
            {
                for (int py = labelY; py < labelY + labelHeight && py < texture.height; py++)
                {
                    SetPixelSafe(texture, px, py, new Color(0, 0, 0, 0.7f));
                }
            }

            // Draw text using simple pixel font
            DrawSimpleText(texture, text, labelX + 2, labelY + 2, color);
        }

        /// <summary>
        /// Draws simple text using a basic pixel font.
        /// </summary>
        private static void DrawSimpleText(Texture2D texture, string text, int x, int y, Color color)
        {
            // Very simple 5x7 pixel font for digits and letters
            foreach (char c in text.ToUpperInvariant())
            {
                var pattern = GetCharPattern(c);
                if (pattern != null)
                {
                    for (int row = 0; row < 7; row++)
                    {
                        for (int col = 0; col < 5; col++)
                        {
                            if ((pattern[row] & (1 << (4 - col))) != 0)
                            {
                                SetPixelSafe(texture, x + col, y + (6 - row), color);
                            }
                        }
                    }
                }
                x += 6;
            }
        }

        /// <summary>
        /// Gets the 5x7 pixel pattern for a character.
        /// </summary>
        private static int[] GetCharPattern(char c)
        {
            return c switch
            {
                'E' => new[] { 0x1F, 0x10, 0x10, 0x1E, 0x10, 0x10, 0x1F },
                '0' => new[] { 0x0E, 0x11, 0x13, 0x15, 0x19, 0x11, 0x0E },
                '1' => new[] { 0x04, 0x0C, 0x04, 0x04, 0x04, 0x04, 0x0E },
                '2' => new[] { 0x0E, 0x11, 0x01, 0x02, 0x04, 0x08, 0x1F },
                '3' => new[] { 0x1F, 0x02, 0x04, 0x02, 0x01, 0x11, 0x0E },
                '4' => new[] { 0x02, 0x06, 0x0A, 0x12, 0x1F, 0x02, 0x02 },
                '5' => new[] { 0x1F, 0x10, 0x1E, 0x01, 0x01, 0x11, 0x0E },
                '6' => new[] { 0x06, 0x08, 0x10, 0x1E, 0x11, 0x11, 0x0E },
                '7' => new[] { 0x1F, 0x01, 0x02, 0x04, 0x08, 0x08, 0x08 },
                '8' => new[] { 0x0E, 0x11, 0x11, 0x0E, 0x11, 0x11, 0x0E },
                '9' => new[] { 0x0E, 0x11, 0x11, 0x0F, 0x01, 0x02, 0x0C },
                _ => null
            };
        }

        /// <summary>
        /// Sets a pixel safely, checking bounds.
        /// </summary>
        private static void SetPixelSafe(Texture2D texture, int x, int y, Color color)
        {
            if (x >= 0 && x < texture.width && y >= 0 && y < texture.height)
            {
                if (color.a < 1f)
                {
                    // Blend with existing pixel
                    var existing = texture.GetPixel(x, y);
                    var blended = Color.Lerp(existing, color, color.a);
                    texture.SetPixel(x, y, blended);
                }
                else
                {
                    texture.SetPixel(x, y, color);
                }
            }
        }
    }

    /// <summary>
    /// Represents the captured state of the screen.
    /// </summary>
    public class ScreenState
    {
        /// <summary>PNG-encoded screenshot (optionally annotated)</summary>
        public byte[] ScreenshotPng { get; set; }

        /// <summary>Discovered UI elements</summary>
        public List<ElementInfo> Elements { get; set; }

        /// <summary>Timestamp of capture</summary>
        public float Timestamp { get; set; }

        /// <summary>Perceptual hash of the screenshot (visual similarity)</summary>
        public string ScreenHash { get; set; }

        /// <summary>Hash of element states (toggle on/off, slider values, etc.)</summary>
        public string ElementStateHash { get; set; }

        /// <summary>
        /// Gets the element list formatted for the AI prompt.
        /// </summary>
        public string GetElementListPrompt()
        {
            return ElementDiscovery.BuildElementListPrompt(Elements);
        }

        /// <summary>
        /// Finds an element by ID.
        /// </summary>
        public ElementInfo FindElement(string elementId)
        {
            return ElementDiscovery.FindElementById(Elements, elementId);
        }
    }
}
