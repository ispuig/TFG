using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TFG.Captura
{
    /// <summary>Publica imagen y descripción juntas mediante un cambio de nombre en el mismo volumen.</summary>
    public sealed class AlmacenCapturas
    {
        private readonly string raiz;
        public AlmacenCapturas(string directorio)
        {
            if (string.IsNullOrWhiteSpace(directorio)) throw new ArgumentException("Falta el directorio de capturas.");
            raiz = Path.GetFullPath(directorio);
        }

        public Task<string> GuardarPngAsync(byte[] png, string metadatosJson, CancellationToken cancelacion)
        {
            return GuardarFotoAsync(png, null, metadatosJson, FormatoFoto.PNG, cancelacion);
        }

        public Task<string> GuardarFotoAsync(byte[] imagen, byte[] miniatura, string metadatosJson,
            FormatoFoto formato, CancellationToken cancelacion)
        {
            if (!Enum.IsDefined(typeof(FormatoFoto), formato)) throw new ArgumentException("Formato de foto desconocido.");
            bool pngValido = imagen != null && imagen.Length >= 8 && imagen[0] == 137 && imagen[1] == 80 && imagen[2] == 78 &&
                imagen[3] == 71 && imagen[4] == 13 && imagen[5] == 10 && imagen[6] == 26 && imagen[7] == 10;
            bool jpgValido = imagen != null && imagen.Length >= 4 && imagen[0] == 255 && imagen[1] == 216 &&
                imagen[imagen.Length - 2] == 255 && imagen[imagen.Length - 1] == 217;
            if (formato == FormatoFoto.PNG ? !pngValido : !jpgValido)
                throw new ArgumentException("Los datos no corresponden al formato de imagen seleccionado.", nameof(imagen));
            if (string.IsNullOrWhiteSpace(metadatosJson)) throw new ArgumentException("Faltan los datos de la captura.");
            return Task.Run(() =>
            {
                cancelacion.ThrowIfCancellationRequested();
                Directory.CreateDirectory(raiz);
                string id = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff") + "_" + Guid.NewGuid().ToString("N");
                string temporal = Path.Combine(raiz, "." + id + ".tmp");
                string destino = Path.Combine(raiz, id);
                string archivo = formato == FormatoFoto.PNG ? "foto_sbs.png" : "foto_sbs.jpg";
                try
                {
                    Directory.CreateDirectory(temporal);
                    File.WriteAllBytes(Path.Combine(temporal, archivo), imagen);
                    if (miniatura != null) File.WriteAllBytes(Path.Combine(temporal, "miniatura.jpg"), miniatura);
                    File.WriteAllText(Path.Combine(temporal, "captura.json"), metadatosJson, new UTF8Encoding(false));
                    cancelacion.ThrowIfCancellationRequested();
                    // Tras este punto está confirmada: no devolver cancelación con un archivo publicado.
                    Directory.Move(temporal, destino);
                    return Path.Combine(destino, archivo);
                }
                finally
                {
                    // Solo el temporal de esta operación, nunca capturas anteriores.
                    if (Directory.Exists(temporal)) Directory.Delete(temporal, true);
                }
            }, cancelacion);
        }
    }
}
