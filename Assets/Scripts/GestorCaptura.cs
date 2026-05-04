using UnityEngine;
using UnityEngine.XR;
using System.Collections.Generic;
using System.IO;

public class StereoFrameCapture : MonoBehaviour
{
    public RenderTexture _leftEyeRT; // RenderTexture del ojo izquierdo EN EL FRAME ACTUAL
    public RenderTexture _rightEyeRT;  // RenderTexture del ojo derecho EN EL FRAME ACTUAL
    private RenderTexture _combinedRT; // Render texture Side-by-Side final
    private KeyValuePair<RenderTexture, RenderTexture> _eyesCombined; // Último par de ojos capturados (para evitar duplicados)
    private List<KeyValuePair<RenderTexture, RenderTexture>> capturedFrames = new List<KeyValuePair<RenderTexture, RenderTexture>>();
    // Material con shader de composición estéreo
    public Material stereoCompositeMaterial;

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

    // Llamar en OnRenderImage o via Camera.onPostRender
    KeyValuePair<RenderTexture, RenderTexture> CaptureEyes()
    {
        Camera[] cameras = Camera.allCameras;
        foreach (var cam in cameras)
        {
            // En OpenXR Unity, la cámara XR renderiza ambos ojos
            // Puedes forzar renders separados:
            cam.stereoTargetEye = StereoTargetEyeMask.Left;
            cam.targetTexture = _leftEyeRT;
            cam.Render();

            cam.stereoTargetEye = StereoTargetEyeMask.Right;
            cam.targetTexture = _rightEyeRT;
            cam.Render();

            // Restaurar
            cam.stereoTargetEye = StereoTargetEyeMask.Both;
            cam.targetTexture = null;
        }

        return ComposeStereoPair();
    }

    KeyValuePair<RenderTexture, RenderTexture> ComposeStereoPair()
    {
        // Pasar ambas texturas al shader compositor
        stereoCompositeMaterial.SetTexture("_LeftEye",  _leftEyeRT);
        stereoCompositeMaterial.SetTexture("_RightEye", _rightEyeRT);

        // Blit al combined: el shader coloca L en mitad izquierda, R en mitad derecha
        Graphics.Blit(null, _combinedRT, stereoCompositeMaterial);

        // Codificar a PNG
        return new KeyValuePair<RenderTexture, RenderTexture>(_leftEyeRT, _rightEyeRT);
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
        
        // Limpiar
        Destroy(screenshot);
        return pngData;
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
        List<RenderTexture> capturedFrames = new List<RenderTexture>(); //Lista de frames en RAW
        private float interval;
        private float nextCaptureTime = 0f;

        public ContinuousCapture(StereoFrameCapture parent, float intervalSeconds)
        {
            this.parent = parent;
            this.interval = intervalSeconds;
            this.nextCaptureTime = Time.time + interval;
        }

        public bool MoveNext()
        {
            if (Time.time >= nextCaptureTime)
            {
                parent.capturedFrames.Add(parent.CaptureEyes());
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
                FFmpegPath = ffmpegExecutablePath,
                BitrateMbps = 40,
                Use10Bit = false,
                LeftEyePrimary = true
            };
            // 
            await StereoVideoEncoder.EncodeStereoVideoAsync(
                capturedFrames,
                settings,
                progress => Debug.Log($"Encoding Progress: {progress:P}"),
                completedPath => Debug.Log("Stereo video saved to: " + completedPath),
                error => Debug.LogError(error));
        }
    }

}