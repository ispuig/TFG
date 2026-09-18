using UnityEngine;
using UnityEngine.XR;
using System.Collections.Generic;
using System;
using System.IO;

public class StereoFrameCapture : MonoBehaviour
{
    public RenderTexture _leftEyeRT; // RenderTexture del ojo izquierdo EN EL FRAME ACTUAL
    public RenderTexture _rightEyeRT;  // RenderTexture del ojo derecho EN EL FRAME ACTUAL
    private RenderTexture _combinedRT; // Render texture Side-by-Side final
    public Material stereoCompositeMaterial;
    public string ffmpegExecutablePath;

    // Ruta de guardado del archivo
    private string _savePath = Path.Combine(Application.persistentDataPath, "stereo_captures");

    void Awake()
    {
        // Resolucion de captura de cada ojo
        // Se puede alterar mediante la API de meta SDK, en el componente 
        int eyeWidth  = XRSettings.eyeTextureWidth;
        int eyeHeight = XRSettings.eyeTextureHeight;

        _leftEyeRT  = new RenderTexture(eyeWidth, eyeHeight, 0, RenderTextureFormat.ARGB32);
        _rightEyeRT = new RenderTexture(eyeWidth, eyeHeight, 0, RenderTextureFormat.ARGB32);

        // Side-by-Side: ancho doble
        _combinedRT = new RenderTexture(eyeWidth * 2, eyeHeight, 0, RenderTextureFormat.ARGB32);

        _leftEyeRT.Create();
        _rightEyeRT.Create();
        _combinedRT.Create();
        _eyesCombined = new KeyValuePair<RenderTexture, RenderTexture>(null, null);
    }
    RenderTexture ComposeStereoPair()
    {
        // Pasar ambas texturas al shader compositor
        stereoCompositeMaterial.SetTexture("_LeftEye",  _leftEyeRT);
        stereoCompositeMaterial.SetTexture("_RightEye", _rightEyeRT);

        // Blit al combined: el shader coloca L en mitad izquierda, R en mitad derecha
        Graphics.Blit(null, _combinedRT, stereoCompositeMaterial);

        // Codificar a PNG
        return _combinedRT; // Devolvemos el RenderTexture combinado, que luego se procesará para extraer cada ojo y codificar a PNG
    }

    byte[] EncodeToPNG()
    {
        // Leer RenderTexture a Texture2D
        Texture2D screenshot = new Texture2D(_combinedRT.width, _combinedRT.height, TextureFormat.ARGB32, false);
        RenderTexture.active = _combinedRT;
        screenshot.ReadPixels(new Rect(0, 0, _combinedRT.width, _combinedRT.height), 0, 0);
        screenshot.Apply();
        RenderTexture.active = null;

        // Codificar a PNG
        byte[] pngData = screenshot.EncodeToPNG();

        pngData = AddStereoMetadata(pngData);
        
        // Limpiar
        Destroy(screenshot);
        return pngData;
    }

    private byte[] AddStereoMetadata(byte[] pngData)
    {
        // la firma PNG son los primeros 8 bytes.
        const int sigLen = 8;
        // El chunk IHDR es el primer chunk después de la firma, y tiene un tamaño fijo de 25 bytes (4 de longitud, 4 de tipo, 13 de datos, 4 de CRC)
        const int ihdrTotal = 25;
        int insertPos = sigLen + ihdrTotal;
        // Crear un tEXt chunk, con la clave "stereo" y valor "parallel" (indica que es una imagen estéreo en formato paralelo, el side-by-side se asume por la resolución doble)
        string keyword = "stereo";
        string value = "parallel";
        byte[] data = System.Text.Encoding.ASCII.GetBytes(keyword + "\0" + value);
        uint length = (uint)data.Length;
        byte[] type = System.Text.Encoding.ASCII.GetBytes("tEXt");
        // Compute CRC of type + data
        uint crc = ComputeCRC32(type.Concat(data).ToArray());
        // Chunk: length (4 big endian), type (4), data, crc (4)
        List<byte> chunk = new List<byte>();
        chunk.AddRange(BitConverter.GetBytes(length).Reverse()); // big endian
        chunk.AddRange(type);
        chunk.AddRange(data);
        chunk.AddRange(BitConverter.GetBytes(crc).Reverse());
        // Now, insert into pngData
        List<byte> newPng = new List<byte>(pngData);
        newPng.InsertRange(insertPos, chunk);
        return newPng.ToArray();
    }

    private uint ComputeCRC32(byte[] data)
    {
        // Funcionamiento: https://en.wikipedia.org/wiki/Computation_of_CRC#Bitwise_algorithm
        uint crc = 0xFFFFFFFF;
        foreach (byte b in data)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++)
            {
                if ((crc & 1) != 0)
                    crc = (crc >> 1) ^ 0xEDB88320;
                else
                    crc >>= 1;
            }
        }
        return crc ^ 0xFFFFFFFF;
    }

    void SaveImageToDisk(string path, int width, int height, byte[] imageData)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            File.WriteAllBytes(path, imageData);

            using (AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            using (AndroidJavaObject context = activity.Call<AndroidJavaObject>("getApplicationContext"))
            using (AndroidJavaClass mediaStoreImagesMedia = new AndroidJavaClass("android.provider.MediaStore$Images$Media"))
            {
                string title = Path.GetFileNameWithoutExtension(path);
                string description = "Stereo capture";
                string insertedPath = mediaStoreImagesMedia.CallStatic<string>("insertImage",
                    context.Call<AndroidJavaObject>("getContentResolver"),
                    path,
                    title,
                    description);

                if (string.IsNullOrEmpty(insertedPath))
                {
                    Debug.LogWarning("No se pudo insertar la imagen en la galería de Android, se guardó en la ruta local.");
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("SaveImageToDisk Android fallback: " + e.Message);
            File.WriteAllBytes(path, imageData);
        }
#else
        File.WriteAllBytes(path, imageData);
#endif
    }

    // Codificar bytes continuos a Video 

    ContinuousCapture captureCoroutine;
    
    public void StartContinuousCapture(float intervalSeconds)
    {
        if (captureCoroutine != null) StopCoroutine(captureCoroutine);
        captureCoroutine = new ContinuousCapture(this, intervalSeconds);
        StartCoroutine(captureCoroutine);
    }

    public void StopContinuousCapture()
    {
        if (captureCoroutine != null)
        {
            StopCoroutine(captureCoroutine);
            captureCoroutine = null;
        }
    }
    private class ContinuousCapture : System.Collections.IEnumerator
    {
        private StereoFrameCapture parent;
        private List<StereoCapture> capturedFrames;
        private float interval;
        private float nextCaptureTime = 0f;
        private float totalCaptureTime = 0f;

        public ContinuousCapture(StereoFrameCapture parent, float intervalSeconds)
        {
            this.parent = parent;
            this.capturedFrames.add(new StereoCapture { LeftImage = parent._leftEyeRT, RightImage = parent._rightEyeRT }); // Captura inicial para asegurar que hay al menos un frame
            this.interval = intervalSeconds;
            this.nextCaptureTime = Time.time + interval;
        }

        public bool MoveNext()
        {
            if (Time.time >= nextCaptureTime)
            {
                parent.capturedFrames.Add(new KeyValuePair<RenderTexture, RenderTexture>(parent._leftEyeRT, parent._rightEyeRT));
                totalCaptureTime += interval;
                nextCaptureTime += interval;
            }
            return true; // Continuar indefinidamente
        }

        public void Reset() { }
        public object Current => null;

        // Método para detener la captura y comenzar la codificación
        public async void StopAndEncode()
        {
            StopContinuousCapture();

            StereoVideoEncoder.EncodingSettings settings = new StereoVideoEncoder.EncodingSettings
            {
                EyeWidth = XRSettings.eyeTextureWidth,
                EyeHeight = XRSettings.eyeTextureHeight,
                FrameInterval = interval,
                TotalDuration = totalCaptureTime,
                OutputPath = Path.Combine(Application.persistentDataPath, "output_stereo.mov"),
                FFmpegPath = parent.ffmpegExecutablePath,
                BitrateMbps = 40, // Fijo hasta nueva versión (post publicacion)
                Use10Bit = false,
                LeftEyePrimary = true
            };
            // 
            await StereoVideoEncoder.EncodeStereoVideoAsync(
                parent.capturedFrames,
                settings,
                progress => Debug.Log($"Encoding Progress: {progress:P}"),
                completedPath => Debug.Log("Stereo video saved to: " + completedPath),
                error => Debug.LogError(error));
        }
    }

}