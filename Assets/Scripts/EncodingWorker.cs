using System;
using System.Diagnostics;
using System.Threading.Tasks;

public static class StereoEncodingWorker
{
    public static async Task RunFFmpegAsync(
        string ffmpegPath,
        string arguments,
        Action<float> onProgress)
    {
        await Task.Run(() =>
        {
            Process process = new Process();
            process.StartInfo.FileName = ffmpegPath;
            process.StartInfo.Arguments = arguments;
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.RedirectStandardOutput = true;

            process.ErrorDataReceived += (sender, e) =>
            {
                if (string.IsNullOrEmpty(e.Data)) return;

                if (e.Data.Contains("frame="))
                {
                    onProgress?.Invoke(0.5f);
                }
            };

            process.Start();
            process.BeginErrorReadLine();
            process.WaitForExit();

            if (process.ExitCode != 0)
                throw new Exception("FFmpeg encoding failed.");
        });
    }
}