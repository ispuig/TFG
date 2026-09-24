package com.tfg.capture;

import java.nio.ByteBuffer;

/** Conversión BT.601 limitada, independiente del SDK para probar color, orientación y strides. */
public final class RgbaYuv {
    private RgbaYuv() { }
    public static final class Plane {
        private final ByteBuffer buffer;
        private final int origin, rowStride, pixelStride;
        public Plane(ByteBuffer buffer, int rowStride, int pixelStride) {
            this.buffer = buffer; this.origin = buffer.position();
            this.rowStride = rowStride; this.pixelStride = pixelStride;
        }
        private void put(int x, int y, int value) {
            buffer.put(origin + y * rowStride + x * pixelStride, (byte)Math.max(0, Math.min(255, value)));
        }
    }
    public static void convert(byte[] rgba, int width, int height, Plane yPlane, Plane uPlane, Plane vPlane) {
        if (width <= 0 || height <= 0 || (width & 1) != 0 || (height & 1) != 0 ||
            rgba == null || rgba.length != (long)width * height * 4)
            throw new IllegalArgumentException("Dimensiones RGBA inválidas");
        // Unity comienza abajo; el vídeo comienza arriba. Cada ojo conserva su mitad horizontal.
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++) {
            int p = ((height - 1 - y) * width + x) * 4;
            int r = rgba[p] & 255, g = rgba[p + 1] & 255, b = rgba[p + 2] & 255;
            yPlane.put(x, y, ((66*r + 129*g + 25*b + 128) >> 8) + 16);
            if ((x & 1) == 0 && (y & 1) == 0) {
                int sr = 0, sg = 0, sb = 0;
                for (int dy = 0; dy < 2; dy++) for (int dx = 0; dx < 2; dx++) {
                    int q = ((height - 1 - y - dy) * width + x + dx) * 4;
                    sr += rgba[q] & 255; sg += rgba[q+1] & 255; sb += rgba[q+2] & 255;
                }
                r = sr / 4; g = sg / 4; b = sb / 4;
                uPlane.put(x/2, y/2, ((-38*r - 74*g + 112*b + 128) >> 8) + 128);
                vPlane.put(x/2, y/2, ((112*r - 94*g - 18*b + 128) >> 8) + 128);
            }
        }
    }
}
