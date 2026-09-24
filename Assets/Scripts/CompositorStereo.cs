using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;

namespace TFG.Captura
{
    /// <summary>Congela el par SBS antes de que las cámaras sobrescriban sus texturas.</summary>
    public sealed class CompositorStereo : IDisposable
    {
        private readonly Material material;
        private static readonly int LeftTex = Shader.PropertyToID("_LeftTex");
        private static readonly int RightTex = Shader.PropertyToID("_RightTex");
        public CompositorStereo(Material plantilla)
        {
            if (!plantilla || !plantilla.shader || !plantilla.shader.isSupported ||
                !plantilla.HasProperty(LeftTex) || !plantilla.HasProperty(RightTex))
                throw new InvalidOperationException("Asigna el material StereoComposite con las propiedades _LeftTex y _RightTex.");
            material = new Material(plantilla);
        }

        public async Task<byte[]> CapturarPngAsync(ParStereo par, CancellationToken cancelacion)
        {
            return (await CapturarAsync(par, FormatoFoto.PNG, 90, cancelacion)).Imagen;
        }

        public async Task<FotoProcesada> CapturarAsync(ParStereo par, FormatoFoto formato, int calidadJpeg, CancellationToken cancelacion)
        {
            if (!Enum.IsDefined(typeof(FormatoFoto), formato) || calidadJpeg < 1 || calidadJpeg > 100)
                throw new ArgumentException("Formato o calidad de fotografía incorrectos.");
            cancelacion.ThrowIfCancellationRequested();
            if (!par.Izquierda || !par.Derecha) throw new InvalidOperationException("No hay imágenes de ambos ojos.");
            if (par.Izquierda.width != par.Derecha.width || par.Izquierda.height != par.Derecha.height)
                throw new InvalidOperationException("Las vistas de ambos ojos deben tener el mismo tamaño.");
            int ancho = checked(par.AnchoOjo * 2), alto = par.AltoOjo;
            if (ancho > SystemInfo.maxTextureSize || alto > SystemInfo.maxTextureSize)
                throw new InvalidOperationException("El tamaño estéreo excede la capacidad gráfica del dispositivo.");
            var rt = RenderTexture.GetTemporary(ancho, alto, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Texture2D imagen = null;
            try
            {
                var activaAnterior = RenderTexture.active;
                bool srgbAnterior = GL.sRGBWrite;
                try
                {
                    material.SetTexture(LeftTex, par.Izquierda);
                    material.SetTexture(RightTex, par.Derecha);
                    GL.sRGBWrite = QualitySettings.activeColorSpace == ColorSpace.Linear;
                    Graphics.Blit(null, rt, material);
                }
                finally
                {
                    GL.sRGBWrite = srgbAnterior;
                    RenderTexture.active = activaAnterior;
                }

                if (SystemInfo.supportsAsyncGPUReadback)
                {
                    var solicitud = AsyncGPUReadback.Request(rt, 0, TextureFormat.RGBA32);
                    // Conservar la RT hasta que la GPU termine, incluso si se cancela la operación.
                    while (!solicitud.done) await Task.Yield();
                    cancelacion.ThrowIfCancellationRequested();
                    if (solicitud.hasError) throw new InvalidOperationException("La GPU no pudo leer la captura.");
                    imagen = new Texture2D(ancho, alto, TextureFormat.RGBA32, false);
                    imagen.LoadRawTextureData(solicitud.GetData<byte>());
                }
                else
                {
                    // Respaldo para fotos; no está pensado para vídeo continuo.
                    imagen = new Texture2D(ancho, alto, TextureFormat.RGBA32, false);
                    activaAnterior = RenderTexture.active;
                    try
                    {
                        RenderTexture.active = rt;
                        imagen.ReadPixels(new Rect(0, 0, ancho, alto), 0, 0, false);
                    }
                    finally { RenderTexture.active = activaAnterior; }
                }
                cancelacion.ThrowIfCancellationRequested();
                byte[] datos = formato == FormatoFoto.PNG ? imagen.EncodeToPNG() : imagen.EncodeToJPG(calidadJpeg);
                // Solo se sube esta instantánea para generar una miniatura pequeña del ojo izquierdo.
                imagen.Apply(false, false);
                return new FotoProcesada(datos, CrearMiniatura(imagen, par.AnchoOjo, par.AltoOjo));
            }
            finally
            {
                LiberarObjeto(imagen);
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        public async Task<byte[]> CapturarRgbaAsync(ParStereo par, int anchoOjo, int altoOjo, CancellationToken cancelacion)
        {
            cancelacion.ThrowIfCancellationRequested();
            if (!SystemInfo.supportsAsyncGPUReadback)
                throw new NotSupportedException("El vídeo necesita lectura asíncrona de la GPU.");
            if (!par.Izquierda || !par.Derecha || anchoOjo <= 0 || altoOjo <= 0)
                throw new ArgumentException("Faltan imágenes o dimensiones válidas para vídeo.");
            var rt = RenderTexture.GetTemporary(checked(anchoOjo * 2), altoOjo, 0,
                RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            try
            {
                var anterior = RenderTexture.active;
                bool srgb = GL.sRGBWrite;
                try
                {
                    material.SetTexture(LeftTex, par.Izquierda);
                    material.SetTexture(RightTex, par.Derecha);
                    GL.sRGBWrite = QualitySettings.activeColorSpace == ColorSpace.Linear;
                    Graphics.Blit(null, rt, material);
                }
                finally { RenderTexture.active = anterior; GL.sRGBWrite = srgb; }
                var lectura = AsyncGPUReadback.Request(rt, 0, TextureFormat.RGBA32);
                while (!lectura.done) await Task.Yield();
                cancelacion.ThrowIfCancellationRequested();
                if (lectura.hasError) throw new InvalidOperationException("No se pudo leer el frame de vídeo.");
                return lectura.GetData<byte>().ToArray(); // Propiedad exclusiva del consumidor hasta terminar.
            }
            finally { RenderTexture.ReleaseTemporary(rt); }
        }

        public static byte[] MiniaturaRgba(byte[] rgba, int anchoOjo, int altoOjo)
        {
            var imagen = new Texture2D(anchoOjo * 2, altoOjo, TextureFormat.RGBA32, false);
            try
            {
                imagen.LoadRawTextureData(rgba);
                imagen.Apply(false, false);
                return CrearMiniatura(imagen, anchoOjo, altoOjo);
            }
            finally { LiberarObjeto(imagen); }
        }

        public void Dispose()
        {
            LiberarObjeto(material);
        }

        private static byte[] CrearMiniatura(Texture imagen, int anchoOjo, int altoOjo)
        {
            int ancho = Math.Min(240, anchoOjo), alto = Math.Max(1, altoOjo * Math.Min(240, anchoOjo) / anchoOjo);
            var temporal = RenderTexture.GetTemporary(ancho, alto, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var anterior = RenderTexture.active;
            bool srgbAnterior = GL.sRGBWrite;
            Texture2D miniatura = null;
            try
            {
                GL.sRGBWrite = QualitySettings.activeColorSpace == ColorSpace.Linear;
                Graphics.Blit(imagen, temporal, new Vector2(0.5f, 1), Vector2.zero);
                RenderTexture.active = temporal;
                miniatura = new Texture2D(ancho, alto, TextureFormat.RGB24, false);
                miniatura.ReadPixels(new Rect(0, 0, ancho, alto), 0, 0, false);
                return miniatura.EncodeToJPG(80);
            }
            finally
            {
                GL.sRGBWrite = srgbAnterior;
                RenderTexture.active = anterior;
                LiberarObjeto(miniatura);
                RenderTexture.ReleaseTemporary(temporal);
            }
        }

        private static void LiberarObjeto(UnityEngine.Object objeto)
        {
            if (!objeto) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(objeto);
            else UnityEngine.Object.DestroyImmediate(objeto);
        }
    }
}
