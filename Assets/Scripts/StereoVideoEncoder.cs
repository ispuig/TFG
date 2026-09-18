using UnityEngine;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

public class StereoVideoEncoder
{
    public class EncodingSettings
    {
        public int EyeWidth;
        public int EyeHeight;
        public float FrameInterval;
        public float TotalDuration;
        public string OutputPath;
        public int BitrateMbps = 40;
        public bool Use10Bit = false;
        public bool LeftEyePrimary = true;
        public string FFmpegPath;
        public int MaxParallelPngDecoders = 4;
    }

    public static async Task EncodeStereoVideoAsync(
        List<StereoCapture> sideBySideFrames,
        EncodingSettings settings, //settings del video a codificar
        Action<float> onProgress = null,
        Action<string> onCompleted = null,
        Action<Exception> onError = null)
    {
        try
        {
            if (sideBySideFrames == null || sideBySideFrames.Count == 0)
                throw new Exception("No frames available for encoding.");

            string tempRoot = Path.Combine(Application.temporaryCachePath, "StereoEncoding");
            string leftDir = Path.Combine(tempRoot, "left");
            string rightDir = Path.Combine(tempRoot, "right");

            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, true);

            Directory.CreateDirectory(leftDir);
            Directory.CreateDirectory(rightDir);

            await FramePngProcessor.ProcessFramesAsync(
                sideBySideFrames,
                leftDir,
                rightDir,
                settings.EyeWidth,
                settings.EyeHeight,
                settings.MaxParallelPngDecoders,
                onProgress);

            string ffmpegCommand = FFmpegCommandBuilder.BuildAppleStereoHEVCCommand(
                settings,
                leftDir,
                rightDir);

            await StereoEncodingWorker.RunFFmpegAsync(
                settings.FFmpegPath,
                ffmpegCommand,
                progress =>
                {
                    onProgress?.Invoke(0.75f + progress * 0.25f);
                });

            onCompleted?.Invoke(settings.OutputPath);
        }
        catch (Exception ex)
        {
            onError?.Invoke(ex);
        }
    }
}
