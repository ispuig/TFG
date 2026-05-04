
using UnityEngine;
using System;
using System.IO;
/*
public static class BitmapEncoder
{
    public static void WriteBitmap(Stream stream, int width, int height, byte[] imageData)
    {
        using (BinaryWriter bw = new BinaryWriter(stream))
        {
            // Bitmap file header
            bw.Write((UInt16)0x4D42);                             // bfType
            bw.Write((UInt32)(14 + 40 + (width * height * 4)));   // bfSize
            bw.Write((UInt16)0);                                  // bfReserved1
            bw.Write((UInt16)0);                                  // bfReserved2
            bw.Write((UInt32)(14 + 40));                          // bfOffBits

            // Bitmap info header
            bw.Write((UInt32)40);                                 // biSize
            bw.Write((Int32)width);                               // biWidth
            bw.Write((Int32)height);                              // biHeight
            bw.Write((UInt16)1);                                  // biPlanes
            bw.Write((UInt16)32);                                 // biBitCount
            bw.Write((UInt32)0);                                  // biCompression
            bw.Write((UInt32)(width * height * 4));               // biSizeImage
            bw.Write((Int32)0);                                   // biXPelsPerMeter
            bw.Write((Int32)0);                                   // biYPelsPerMeter
            bw.Write((UInt32)0);                                  // biClrUsed
            bw.Write((UInt32)0);                                  // biClrImportant

            // RGB -> BGRA
            for (int i = 0; i < imageData.Length; i += 3)
            {
                bw.Write(imageData[i + 2]);
                bw.Write(imageData[i + 1]);
                bw.Write(imageData[i + 0]);
                bw.Write((byte)255);
            }
        }
    }

    /// <summary>
    /// Codifica datos RGB (3 bytes por pixel) en JPG y los escribe al stream.
    /// </summary>
    /// <param name="stream">Stream destino.</param>
    /// <param name="width">Ancho de la imagen en píxeles.</param>
    /// <param name="height">Alto de la imagen en píxeles.</param>
    /// <param name="imageData">Buffer RGB (longitud = width * height * 3).</param>
    /// <param name="quality">Calidad JPG entre 0 y 100.</param>
    public static void WriteJpeg(Stream stream, int width, int height, byte[] imageData, int quality)
    {
        if (stream == null) throw new ArgumentNullException(nameof(stream));
        if (imageData == null) throw new ArgumentNullException(nameof(imageData));
        if (width <= 0 || height <= 0)
            throw new ArgumentException("El ancho y alto deben ser mayores que cero.");
        if (imageData.Length != width * height * 3)
            throw new ArgumentException("imageData debe tener exactamente width * height * 3 bytes (RGB).");

        // Clamp de calidad al rango permitido por Unity
        quality = Mathf.Clamp(quality, 1, 100);

        // Unity espera RGBA32 y origen abajo-izquierda; convertimos y volteamos verticalmente
        // para que el resultado coincida con la orientación natural de un BMP.
        byte[] rgba = new byte[width * height * 4];

        for (int y = 0; y < height; y++)
        {
            int srcRow = (height - 1 - y) * width * 3; // voltear vertical
            int dstRow = y * width * 4;

            for (int x = 0; x < width; x++)
            {
                int s = srcRow + x * 3;
                int d = dstRow + x * 4;

                rgba[d + 0] = imageData[s + 0]; // R
                rgba[d + 1] = imageData[s + 1]; // G
                rgba[d + 2] = imageData[s + 2]; // B
                rgba[d + 3] = 255;              // A
            }
        }

        byte[] jpgBytes = ImageConversion.EncodeArrayToJPG(
            rgba,
            GraphicsFormat.R8G8B8A8_UNorm, // formato del buffer de entrada
            (uint)width,
            (uint)height,
            0,                              // rowBytes = 0 -> tightly packed
            quality
        );

        stream.Write(jpgBytes, 0, jpgBytes.Length);
    }
}
*/