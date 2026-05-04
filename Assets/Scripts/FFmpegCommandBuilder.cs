using System.Globalization;

public static class FFmpegCommandBuilder
{
    public static string BuildAppleStereoHEVCCommand(
        StereoVideoEncoder.EncodingSettings settings,
        string leftDir,
        string rightDir)
    {
        float fps = 1f / settings.FrameInterval;
        string pixelFormat = settings.Use10Bit ? "yuv420p10le" : "yuv420p";
        string profileBase = settings.Use10Bit ? "main10" : "main";
        string profileStereo = settings.Use10Bit ? "multiview-main10" : "multiview-main";

        return
            $"-y " +
            $"-framerate {fps.ToString(CultureInfo.InvariantCulture)} -i \"{leftDir}/frame_%06d.png\" " +
            $"-framerate {fps.ToString(CultureInfo.InvariantCulture)} -i \"{rightDir}/frame_%06d.png\" " +
            $"-filter_complex \"[0:v][1:v]hstack=inputs=2[stereo]\" " +
            $"-map \"[stereo]\" " +
            $"-c:v libx265 " +
            $"-tag:v hvc1 " +
            $"-pix_fmt {pixelFormat} " +
            $"-profile:v {profileBase} " +
            $"-x265-params \"frame-packing=3:repeat-headers=1:aud=1:hrd=1:bitrate={settings.BitrateMbps * 1000}:vbv-maxrate={settings.BitrateMbps * 1000}:vbv-bufsize={settings.BitrateMbps * 2000}\" " +
            $"\"{settings.OutputPath}\"";
    }
}