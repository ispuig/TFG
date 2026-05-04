using UnityEngine;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

// Clase encargada de procesar los frames capturados, separando el ojo izquierdo y derecho y guardándolos como PNG
public static class FramePngProcessor
{
    public static async Task ProcessFramesAsync(
        List<RenderTexture> frames,
        string leftDir,
        string rightDir,
        int eyeWidth,
        int eyeHeight,
        int maxParallel,
        Action<float> onProgress)
    {
        SemaphoreSlim semaphore = new SemaphoreSlim(maxParallel);
        List<Task> tasks = new List<Task>();

        for (int i = 0; i < frames.Count; i++)
        {
            int frameIndex = i;
            await semaphore.WaitAsync();

            tasks.Add(Task.Run(() =>
            {
                try
                {
                    RenderTexture frame = frames[frameIndex];
                    Texture2D combined = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    Graphics.CopyTexture(frame, combined);

                    Color[] combinedPixels = combined.GetPixels();

                    Texture2D left = new Texture2D(eyeWidth, eyeHeight, TextureFormat.RGBA32, false);
                    Texture2D right = new Texture2D(eyeWidth, eyeHeight, TextureFormat.RGBA32, false);

                    Color[] leftPixels = new Color[eyeWidth * eyeHeight];
                    Color[] rightPixels = new Color[eyeWidth * eyeHeight];

                    for (int y = 0; y < eyeHeight; y++)
                    {
                        for (int x = 0; x < eyeWidth; x++)
                        {
                            leftPixels[y * eyeWidth + x] = combinedPixels[y * eyeWidth * 2 + x];
                            rightPixels[y * eyeWidth + x] = combinedPixels[y * eyeWidth * 2 + x + eyeWidth];
                        }
                    }

                    left.SetPixels(leftPixels);
                    right.SetPixels(rightPixels);
                    left.Apply();
                    right.Apply();

                    File.WriteAllBytes(
                        Path.Combine(leftDir, $"frame_{frameIndex:D06}.png"),
                        left.EncodeToPNG());

                    File.WriteAllBytes(
                        Path.Combine(rightDir, $"frame_{frameIndex:D06}.png"),
                        right.EncodeToPNG());

                    UnityEngine.Object.DestroyImmediate(combined);
                    UnityEngine.Object.DestroyImmediate(left);
                    UnityEngine.Object.DestroyImmediate(right);
                }
                finally
                {
                    onProgress?.Invoke((float)(frameIndex + 1) / frames.Count * 0.75f);
                    semaphore.Release();
                }
            }));
        }

        await Task.WhenAll(tasks);
    }
}