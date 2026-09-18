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
        List<StereoCapture> frames,
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
            // Capturamos el frame actual para evitar problemas de acceso concurrente a las RenderTextures, hilos ohh
            tasks.Add(Task.Run(() =>
            {
                try
                {
                    StereoCapture frame = new StereoCapture
                    {
                        LeftImage = frames[frameIndex].LeftImage,
                        RightImage = frames[frameIndex].RightImage
                    };
                    // Procesar ojo izquierdo
                    Texture2D leftTex = new Texture2D(eyeWidth, eyeHeight, TextureFormat.ARGB32, false);
                    RenderTexture.active = frame.LeftImage;
                    leftTex.ReadPixels(new Rect(0, 0, eyeWidth, eyeHeight), 0, 0);
                    leftTex.Apply();
                    byte[] leftPng = leftTex.EncodeToPNG();
                    File.WriteAllBytes(Path.Combine(leftDir, $"frame_{frameIndex:D4}_left.png"), leftPng);
                    UnityEngine.Object.Destroy(leftTex);
                    // Procesar ojo derecho
                    Texture2D rightTex = new Texture2D(eyeWidth, eyeHeight, TextureFormat.ARGB32, false);
                    RenderTexture.active = frame.RightImage;
                    rightTex.ReadPixels(new Rect(0, 0, eyeWidth, eyeHeight), 0, 0);
                    rightTex.Apply();
                    byte[] rightPng = rightTex.EncodeToPNG();
                    File.WriteAllBytes(Path.Combine(rightDir, $"frame_{frameIndex:D4}_right.png"), rightPng);
                    UnityEngine.Object.Destroy(rightTex);
                }
                finally // Se actualiza el progreso y se libera el semáforo incluso si ocurre una excepción
                {
                    onProgress?.Invoke((float)(frameIndex + 1) / frames.Count * 0.75f);
                    semaphore.Release();
                }}));
        }

        await Task.WhenAll(tasks);
    }
}