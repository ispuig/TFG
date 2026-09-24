using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TFG.Captura
{
    /// <summary>El MP4 permanece oculto hasta que el codificador cierra y confirma la sesión.</summary>
    public sealed class SesionVideo : IDisposable
    {
        private readonly string temporal, destino;
        public string RutaVideo => Path.Combine(temporal, "video_sbs.mp4");
        public SesionVideo(string raiz)
        {
            raiz = Path.GetFullPath(raiz);
            string id = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff") + "_" + Guid.NewGuid().ToString("N");
            temporal = Path.Combine(raiz, "." + id + ".tmp");
            destino = Path.Combine(raiz, id);
            Directory.CreateDirectory(temporal);
        }
        public Task<string> PublicarAsync(string json, byte[] miniatura, CancellationToken cancelacion) => Task.Run(() =>
        {
            cancelacion.ThrowIfCancellationRequested();
            if (!File.Exists(RutaVideo) || new FileInfo(RutaVideo).Length < 32)
                throw new IOException("El codificador no produjo un vídeo válido.");
            File.WriteAllText(Path.Combine(temporal, "captura.json"), json, new UTF8Encoding(false));
            if (miniatura != null) File.WriteAllBytes(Path.Combine(temporal, "miniatura.jpg"), miniatura);
            cancelacion.ThrowIfCancellationRequested();
            Directory.Move(temporal, destino);
            return Path.Combine(destino, "video_sbs.mp4");
        }, cancelacion);
        public void Dispose()
        {
            if (Directory.Exists(temporal)) Directory.Delete(temporal, true);
        }
    }
}
