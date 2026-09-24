using System;
using System.Threading.Tasks;
using UnityEngine;

namespace TFG.Captura
{
    /// <summary>Consumidor de capacidad uno: el productor espera cada llamada, sin acumular frames.</summary>
    public sealed class CodificadorVideoAndroid
    {
        public static readonly PerfilVideo[] Perfiles =
        {
            new PerfilVideo(640, 480, 10), new PerfilVideo(320, 240, 15), new PerfilVideo(640, 480, 15)
        };
        public static bool Disponible => Application.platform == RuntimePlatform.Android && !Application.isEditor;
        private AndroidJavaObject nativo;
        private const string Clase = "com.tfg.capture.TFGVideo";
        public static string Mime(bool hevc) => hevc ? "video/hevc" : "video/avc";

        public static bool Compatible(bool hevc, PerfilVideo perfil)
        {
            if (!Disponible) return false;
            try
            {
                using var clase = new AndroidJavaClass(Clase);
                return clase.CallStatic<bool>("supports", Mime(hevc), perfil.AnchoOjo * 2, perfil.AltoOjo, perfil.Fps);
            }
            catch (Exception error) { Debug.LogWarning("Vídeo no disponible: " + error.Message); return false; }
        }

        public Task AbrirAsync(string ruta, bool hevc, PerfilVideo perfil) => EnHilo(() =>
        {
            nativo = new AndroidJavaObject(Clase, ruta, Mime(hevc), perfil.AnchoOjo * 2, perfil.AltoOjo, perfil.Fps);
        });
        public Task EscribirAsync(byte[] rgba, long tiempoUs) => EnHilo(() => nativo.Call("frame", rgba, tiempoUs));
        public Task FinalizarAsync(long tiempoUs) => EnHilo(() => nativo.Call("finish", tiempoUs));
        public Task CerrarAsync() => EnHilo(() =>
        {
            if (nativo == null) return;
            try { nativo.Call("close"); }
            finally { nativo.Dispose(); nativo = null; }
        });

        private static Task EnHilo(Action accion) => Task.Run(() =>
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            AndroidJNI.AttachCurrentThread();
            try { accion(); }
            finally { AndroidJNI.DetachCurrentThread(); }
#else
            throw new NotSupportedException("La grabación requiere un dispositivo Android compatible.");
#endif
        });
    }

    public readonly struct PerfilVideo
    {
        public readonly int AnchoOjo, AltoOjo, Fps;
        public PerfilVideo(int ancho, int alto, int fps) { AnchoOjo = ancho; AltoOjo = alto; Fps = fps; }
        public override string ToString() => $"{AnchoOjo} × {AltoOjo} / ojo · {Fps} FPS";
    }
}
