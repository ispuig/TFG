using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace TFG.Captura
{
    public static class ExportadorCapturas
    {
        public static bool Disponible
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                return true;
#else
                return false;
#endif
            }
        }

        public static Task<string> ExportarAsync(DatosCaptura captura)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (captura == null || !File.Exists(captura.ruta)) throw new FileNotFoundException("La captura ya no existe.");
            return Task.Run(() =>
            {
                AndroidJNI.AttachCurrentThread();
                try
                {
                    using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                    using var actividad = player.GetStatic<AndroidJavaObject>("currentActivity");
                    using var resolver = actividad.Call<AndroidJavaObject>("getContentResolver");
                    bool video = captura.tipo == "video";
                    using var media = new AndroidJavaClass(video ? "android.provider.MediaStore$Video$Media" : "android.provider.MediaStore$Images$Media");
                    using var externo = media.GetStatic<AndroidJavaObject>("EXTERNAL_CONTENT_URI");
                    using var valores = new AndroidJavaObject("android.content.ContentValues");
                    using var pendiente = new AndroidJavaObject("java.lang.Integer", 1);
                    string nombre = "TFG_" + Path.GetFileName(Path.GetDirectoryName(captura.ruta)) + "_SBS_LR" + Path.GetExtension(captura.ruta);
                    valores.Call("put", "_display_name", nombre);
                    valores.Call("put", "mime_type", video ? "video/mp4" : captura.formato == "PNG" ? "image/png" : "image/jpeg");
                    valores.Call("put", "relative_path", video ? "Movies/TFG" : "Pictures/TFG");
                    valores.Call("put", "is_pending", pendiente);
                    using var uri = resolver.Call<AndroidJavaObject>("insert", externo, valores);
                    if (uri == null) throw new IOException("La galería del sistema no aceptó la fotografía.");
                    try
                    {
                        using (var salida = resolver.Call<AndroidJavaObject>("openOutputStream", uri))
                        {
                            if (salida == null) throw new IOException("No se pudo abrir la salida de la galería.");
                            try
                            {
                                using var entrada = File.OpenRead(captura.ruta);
                                byte[] buffer = new byte[65536];
                                int leidos;
                                while ((leidos = entrada.Read(buffer, 0, buffer.Length)) > 0)
                                    salida.Call("write", buffer, 0, leidos);
                            }
                            finally { salida.Call("close"); }
                        }
                        valores.Call("clear");
                        using var terminado = new AndroidJavaObject("java.lang.Integer", 0);
                        valores.Call("put", "is_pending", terminado);
                        int actualizadas = resolver.Call<int>("update", uri, valores, null, null);
                        if (actualizadas != 1) throw new IOException("No se pudo publicar la fotografía en la galería.");
                        return uri.Call<string>("toString");
                    }
                    catch
                    {
                        resolver.Call<int>("delete", uri, null, null);
                        throw;
                    }
                }
                finally { AndroidJNI.DetachCurrentThread(); }
            });
#else
            return Task.FromException<string>(new NotSupportedException("La exportación a la galería del sistema requiere Android."));
#endif
        }
    }
}
