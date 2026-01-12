using System;
using System.Text;
using UnityEngine;

namespace ODDGames.UITest.AI
{
    /// <summary>
    /// Computes perceptual hashes of screenshots for similarity comparison.
    /// Uses average hash (aHash) algorithm for speed.
    /// </summary>
    public static class ScreenHash
    {
        private const int HashSize = 8; // 8x8 = 64 bits

        /// <summary>
        /// Computes a perceptual hash of a texture.
        /// Returns a hex string representing the hash.
        /// </summary>
        public static string ComputeHash(Texture2D texture)
        {
            if (texture == null)
                return null;

            // Resize to HashSize x HashSize
            var resized = ResizeTexture(texture, HashSize, HashSize);

            // Convert to grayscale and compute average
            float sum = 0f;
            float[] grayValues = new float[HashSize * HashSize];

            for (int y = 0; y < HashSize; y++)
            {
                for (int x = 0; x < HashSize; x++)
                {
                    var pixel = resized.GetPixel(x, y);
                    // Use luminance formula for grayscale
                    float gray = 0.299f * pixel.r + 0.587f * pixel.g + 0.114f * pixel.b;
                    grayValues[y * HashSize + x] = gray;
                    sum += gray;
                }
            }

            float average = sum / (HashSize * HashSize);

            // Generate hash: 1 if above average, 0 if below
            var sb = new StringBuilder();
            ulong hash = 0;

            for (int i = 0; i < grayValues.Length; i++)
            {
                if (grayValues[i] >= average)
                {
                    hash |= (1UL << i);
                }
            }

            // Convert to hex string
            return hash.ToString("X16");
        }

        /// <summary>
        /// Computes a perceptual hash from PNG bytes.
        /// </summary>
        public static string ComputeHashFromPng(byte[] pngBytes)
        {
            if (pngBytes == null || pngBytes.Length == 0)
                return null;

            var texture = new Texture2D(2, 2);
            if (!texture.LoadImage(pngBytes))
            {
                UnityEngine.Object.Destroy(texture);
                return null;
            }

            var hash = ComputeHash(texture);
            UnityEngine.Object.Destroy(texture);
            return hash;
        }

        /// <summary>
        /// Computes the Hamming distance between two hashes.
        /// Lower = more similar. 0 = identical.
        /// </summary>
        public static int CompareHashes(string hash1, string hash2)
        {
            if (string.IsNullOrEmpty(hash1) || string.IsNullOrEmpty(hash2))
                return int.MaxValue;

            if (hash1.Length != hash2.Length)
                return int.MaxValue;

            try
            {
                ulong h1 = Convert.ToUInt64(hash1, 16);
                ulong h2 = Convert.ToUInt64(hash2, 16);
                ulong xor = h1 ^ h2;

                // Count set bits (Hamming weight)
                int count = 0;
                while (xor != 0)
                {
                    count++;
                    xor &= xor - 1; // Clear lowest set bit
                }
                return count;
            }
            catch
            {
                return int.MaxValue;
            }
        }

        /// <summary>
        /// Checks if two hashes are similar (within threshold).
        /// Default threshold of 10 allows for minor differences.
        /// </summary>
        public static bool AreSimilar(string hash1, string hash2, int threshold = 10)
        {
            return CompareHashes(hash1, hash2) <= threshold;
        }

        /// <summary>
        /// Checks if two hashes are very similar (nearly identical).
        /// Uses a stricter threshold.
        /// </summary>
        public static bool AreVerySimilar(string hash1, string hash2)
        {
            return CompareHashes(hash1, hash2) <= 5;
        }

        /// <summary>
        /// Gets a similarity percentage between two hashes.
        /// 100% = identical, 0% = completely different.
        /// </summary>
        public static float GetSimilarityPercentage(string hash1, string hash2)
        {
            int distance = CompareHashes(hash1, hash2);
            if (distance == int.MaxValue) return 0f;

            // 64 bits total
            return (64 - distance) / 64f * 100f;
        }

        /// <summary>
        /// Resizes a texture to the specified dimensions.
        /// </summary>
        private static Texture2D ResizeTexture(Texture2D source, int width, int height)
        {
            var rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
            RenderTexture.active = rt;

            Graphics.Blit(source, rt);

            var result = new Texture2D(width, height, TextureFormat.ARGB32, false);
            result.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            result.Apply();

            RenderTexture.active = null;
            RenderTexture.ReleaseTemporary(rt);

            return result;
        }
    }
}
