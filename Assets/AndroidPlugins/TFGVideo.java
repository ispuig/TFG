package com.tfg.capture;

import android.media.Image;
import android.media.MediaCodec;
import android.media.MediaCodecInfo;
import android.media.MediaCodecList;
import android.media.MediaFormat;
import android.media.MediaMuxer;
import android.os.SystemClock;
import java.io.IOException;
import java.nio.ByteBuffer;

/** Una sesión, un consumidor. C# espera cada frame: nunca hay una cola de imágenes creciente. */
public final class TFGVideo {
    private MediaCodec codec;
    private MediaMuxer muxer;
    private final MediaCodec.BufferInfo info = new MediaCodec.BufferInfo();
    private int width, height, track = -1, frames;
    private boolean started, ended;
    private long lastPts = -1;

    private static MediaFormat format(String mime, int w, int h, int fps) {
        MediaFormat f = MediaFormat.createVideoFormat(mime, w, h);
        f.setInteger(MediaFormat.KEY_COLOR_FORMAT, MediaCodecInfo.CodecCapabilities.COLOR_FormatYUV420Flexible);
        f.setInteger(MediaFormat.KEY_BIT_RATE, Math.min(6000000, Math.max(1000000, w * h * fps)));
        f.setInteger(MediaFormat.KEY_FRAME_RATE, fps);
        f.setInteger(MediaFormat.KEY_I_FRAME_INTERVAL, 1);
        f.setInteger(MediaFormat.KEY_COLOR_STANDARD, MediaFormat.COLOR_STANDARD_BT601_NTSC);
        f.setInteger(MediaFormat.KEY_COLOR_RANGE, MediaFormat.COLOR_RANGE_LIMITED);
        f.setInteger(MediaFormat.KEY_COLOR_TRANSFER, MediaFormat.COLOR_TRANSFER_SDR_VIDEO);
        return f;
    }

    public static boolean supports(String mime, int w, int h, int fps) {
        try {
            MediaCodecList list = new MediaCodecList(MediaCodecList.REGULAR_CODECS);
            return list.findEncoderForFormat(format(mime, w, h, fps)) != null &&
                list.findDecoderForFormat(MediaFormat.createVideoFormat(mime, w, h)) != null;
        }
        catch (RuntimeException e) { return false; }
    }

    public TFGVideo(String path, String mime, int w, int h, int fps) throws IOException {
        if (w <= 0 || h <= 0 || (w & 1) != 0 || (h & 1) != 0 || fps <= 0)
            throw new IllegalArgumentException("Dimensiones o FPS inválidos");
        width = w; height = h;
        try {
            MediaFormat f = format(mime, w, h, fps);
            String name = new MediaCodecList(MediaCodecList.REGULAR_CODECS).findEncoderForFormat(f);
            if (name == null) throw new IOException("Este dispositivo no admite el formato de vídeo seleccionado");
            codec = MediaCodec.createByCodecName(name);
            codec.configure(f, null, null, MediaCodec.CONFIGURE_FLAG_ENCODE);
            codec.start();
            muxer = new MediaMuxer(path, MediaMuxer.OutputFormat.MUXER_OUTPUT_MPEG_4);
        } catch (IOException | RuntimeException e) { close(); throw e; }
    }

    public synchronized void frame(byte[] rgba, long ptsUs) throws IOException {
        if (ended || codec == null || rgba.length != width * height * 4 || ptsUs < 0 || ptsUs <= lastPts)
            throw new IllegalArgumentException("Frame o timestamp inválido");
        int index = input();
        Image image = codec.getInputImage(index);
        if (image == null) throw new IOException("El codificador no ofrece entrada YUV flexible");
        try {
            Image.Plane[] planes = image.getPlanes();
            RgbaYuv.convert(rgba, width, height, plane(planes[0]), plane(planes[1]), plane(planes[2]));
        } finally { image.close(); }
        codec.queueInputBuffer(index, 0, width * height * 3 / 2, ptsUs, 0);
        lastPts = ptsUs; frames++;
        drain(false);
    }

    private static RgbaYuv.Plane plane(Image.Plane plane) {
        return new RgbaYuv.Plane(plane.getBuffer(), plane.getRowStride(), plane.getPixelStride());
    }

    private int input() throws IOException {
        long deadline = SystemClock.elapsedRealtime() + 5000;
        while (SystemClock.elapsedRealtime() < deadline) {
            int index = codec.dequeueInputBuffer(10000);
            if (index >= 0) return index;
            drain(false);
        }
        throw new IOException("El codificador no acepta más imágenes");
    }

    private void drain(boolean finishing) throws IOException {
        long deadline = SystemClock.elapsedRealtime() + 10000;
        while (SystemClock.elapsedRealtime() < deadline) {
            int index = codec.dequeueOutputBuffer(info, finishing ? 10000 : 0);
            if (index == MediaCodec.INFO_TRY_AGAIN_LATER) { if (!finishing) return; continue; }
            if (index == MediaCodec.INFO_OUTPUT_FORMAT_CHANGED) {
                if (started) throw new IOException("El formato cambió durante la grabación");
                track = muxer.addTrack(codec.getOutputFormat()); muxer.start(); started = true;
            } else if (index >= 0) {
                try {
                    if ((info.flags & MediaCodec.BUFFER_FLAG_CODEC_CONFIG) != 0) info.size = 0;
                    if (info.size > 0) {
                        if (!started) throw new IOException("Falta el formato del vídeo");
                        ByteBuffer output = codec.getOutputBuffer(index);
                        output.position(info.offset); output.limit(info.offset + info.size);
                        muxer.writeSampleData(track, output, info);
                    }
                    if ((info.flags & MediaCodec.BUFFER_FLAG_END_OF_STREAM) != 0) return;
                } finally { codec.releaseOutputBuffer(index, false); }
            }
        }
        throw new IOException("Tiempo agotado al finalizar el vídeo");
    }

    public synchronized void finish(long endUs) throws IOException {
        if (ended || frames == 0) throw new IOException("La grabación no contiene imágenes");
        int index = input();
        codec.queueInputBuffer(index, 0, 0, Math.max(endUs, lastPts + 1), MediaCodec.BUFFER_FLAG_END_OF_STREAM);
        drain(true);
        if (!started) throw new IOException("No se produjo ningún vídeo");
        // Una muestra vacía fija la duración de la última imagen en MP4.
        info.set(0, 0, Math.max(endUs, lastPts + 1), MediaCodec.BUFFER_FLAG_END_OF_STREAM);
        muxer.writeSampleData(track, ByteBuffer.allocate(0), info);
        muxer.stop(); started = false; ended = true;
    }

    public synchronized void close() {
        if (codec != null) { try { codec.release(); } catch (RuntimeException ignored) { } codec = null; }
        if (muxer != null) { try { muxer.release(); } catch (RuntimeException ignored) { } muxer = null; }
    }
}
