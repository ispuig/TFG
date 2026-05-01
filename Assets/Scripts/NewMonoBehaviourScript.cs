using UnityEngine;
using UnityEngine.XR;
using System.Collections.Generic;
using System.IO;

public class StereoFrameCapture : MonoBehaviour
{
    private RenderTexture _leftEyeRT;
    private RenderTexture _rightEyeRT;
    private RenderTexture _combinedRT; // Side-by-Side final

    // Material con shader de composición estéreo
    public Material stereoCompositeMaterial;

    void Awake()
    {
        // Quest 3 renderiza a 2064x2208 por ojo (resolución recomendada
        // Puedes bajarla para rendimiento
        int eyeWidth  = XRSettings.eyeTextureWidth;
        int eyeHeight = XRSettings.eyeTextureHeight;

        _leftEyeRT  = new RenderTexture(eyeWidth, eyeHeight, 0, RenderTextureFormat.ARGB32);
        _rightEyeRT = new RenderTexture(eyeWidth, eyeHeight, 0, RenderTextureFormat.ARGB32);

        // Side-by-Side: ancho doble
        _combinedRT = new RenderTexture(eyeWidth * 2, eyeHeight, 0, RenderTextureFormat.ARGB32);

        _leftEyeRT.Create();
        _rightEyeRT.Create();
        _combinedRT.Create();
    }

    // Llamar en OnRenderImage o via Camera.onPostRender
    void CaptureEyes()
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

        ComposeStereoPair();
    }

    void ComposeStereoPair()
    {
        // Pasar ambas texturas al shader compositor
        stereoCompositeMaterial.SetTexture("_LeftEye",  _leftEyeRT);
        stereoCompositeMaterial.SetTexture("_RightEye", _rightEyeRT);

        // Blit al combined: el shader coloca L en mitad izquierda, R en mitad derecha
        Graphics.Blit(null, _combinedRT, stereoCompositeMaterial);

        // Codificar a PNG
        EncodeToPNG();
    }

    void EncodeToPNG()
    {
        // Leer RenderTexture a Texture2D
        Texture2D screenshot = new Texture2D(_combinedRT.width, _combinedRT.height, TextureFormat.ARGB32, false);
        RenderTexture.active = _combinedRT;
        screenshot.ReadPixels(new Rect(0, 0, _combinedRT.width, _combinedRT.height), 0, 0);
        screenshot.Apply();
        RenderTexture.active = null;

        // Codificar a PNG
        byte[] pngData = screenshot.EncodeToPNG();
        
        // Guardar archivo
        string path = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.Desktop), 
            "stereo_" + System.DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + ".png");
        File.WriteAllBytes(path, pngData);
        
        Debug.Log("Imagen guardada en: " + path);
        
        // Limpiar
        Destroy(screenshot);
    }
}