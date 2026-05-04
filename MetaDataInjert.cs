using System;
using System.IO;
using System.Linq;
using UnityEngine;

public class StereoMetadataPatcher : MonoBehaviour
{
    // Modos según el estándar ISO: 1 = Top-Bottom, 2 = Side-by-Side 
    public enum StereoMode : byte
    {
        TopBottom = 1,
        SideBySide = 2
    }

    public void PatchVideo(string inputPath, string outputPath, StereoMode mode)
    {
        byte videoData = File.ReadAllBytes(inputPath);
        
        // El átomo st3d tiene exactamente 13 bytes [1]
        // Estructura:[Version (1b)][Flags (3b)][Mode (1b)]
        byte st3dBox = new byte { 
            0x00, 0x00, 0x00, 0x0D, // Size: 13 bytes
            0x73, 0x74, 0x33, 0x64, // Type: 'st3d'
            0x00,                   // Version: 0
            0x00, 0x00, 0x00,       // Flags: 0
            (byte)mode              // 1 o 2
        };

        // Buscamos el punto de inyección: normalmente dentro de stsd/avc1 o stsd/hvc1
        // Buscamos la firma del códec (avc1 o hvc1) para insertar la extensión
        byte codecSignature = new byte { 0x61, 0x76, 0x63, 0x31 }; // 'avc1'
        int index = FindPattern(videoData, codecSignature);

        if (index == -1)
        {
            codecSignature = new byte { 0x68, 0x76, 0x63, 0x31 }; // 'hvc1'
            index = FindPattern(videoData, codecSignature);
        }

        if (index!= -1)
        {
            // Nota crítica: Al insertar bytes en la cabecera (moov), 
            // los desplazamientos (offsets) del átomo 'stco' quedan invalidados.[3, 4]
            // Para un "primer script", la solución más segura es crear un nuevo archivo
            // que respete la estructura.
            
            using (FileStream fs = new FileStream(outputPath, FileMode.Create))
            {
                // Escribimos hasta después del encabezado del códec (típicamente +78 bytes en avc1)
                int injectionPoint = index + 82; 
                fs.Write(videoData, 0, injectionPoint);
                fs.Write(st3dBox, 0, st3dBox.Length);
                fs.Write(videoData, injectionPoint, videoData.Length - injectionPoint);
            }
            
            Debug.Log("Metadatos inyectados con éxito en: " + outputPath);
            Debug.LogWarning("Importante: Si el archivo no abre, deberás recalcular los 'stco' offsets.");
        }
        else
        {
            Debug.LogError("No se encontró el átomo de vídeo para parchear.");
        }
    }

    private int FindPattern(byte data, byte pattern)
    {
        for (int i = 0; i <= data.Length - pattern.Length; i++)
        {
            if (data.Skip(i).Take(pattern.Length).SequenceEqual(pattern))
                return i;
        }
        return -1;
    }
}