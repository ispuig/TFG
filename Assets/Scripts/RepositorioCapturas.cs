using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace TFG.Captura
{
    /// <summary>Catálogo reconstruible desde los manifiestos publicados. Ignora los directorios temporales.</summary>
    public sealed class RepositorioCapturas
    {
        private readonly string raiz;
        public RepositorioCapturas(string directorio) { raiz = Path.GetFullPath(directorio); }

        public async Task<List<DatosCaptura>> ListarAsync()
        {
            var documentos = await Task.Run(() =>
            {
                var datos = new List<KeyValuePair<string, string>>();
                if (!Directory.Exists(raiz)) return datos;
                foreach (string carpeta in Directory.GetDirectories(raiz))
                {
                    if (Path.GetFileName(carpeta).StartsWith(".", StringComparison.Ordinal)) continue;
                    try
                    {
                        string archivo = Path.Combine(carpeta, "captura.json");
                        if (File.Exists(archivo)) datos.Add(new KeyValuePair<string, string>(carpeta, File.ReadAllText(archivo)));
                    }
                    catch (IOException) { /* Puede haberse eliminado mientras se enumeraba. */ }
                    catch (UnauthorizedAccessException) { }
                }
                return datos;
            });
            var resultado = new List<DatosCaptura>();
            int procesadas = 0;
            foreach (var documento in documentos)
            {
                if (++procesadas % 32 == 0) await Task.Yield();
                DatosCaptura dato;
                try { dato = JsonUtility.FromJson<DatosCaptura>(documento.Value); }
                catch (ArgumentException) { continue; }
                if (dato == null || dato.version != 1 || dato.anchoOjo <= 0 || dato.altoOjo <= 0 ||
                    dato.disposicion != "SBS" || dato.ordenOjos != "LR") continue;
                bool foto = dato.tipo == "foto" && (dato.formato == "PNG" || dato.formato == "JPEG");
                bool video = dato.tipo == "video" && dato.formato == "MP4" && (dato.codec == "H264" || dato.codec == "HEVC") &&
                    dato.duracionSegundos > 0 && !double.IsInfinity(dato.duracionSegundos) && dato.framesGuardados > 0;
                if (!foto && !video) continue;
                dato.ruta = Path.Combine(documento.Key, video ? "video_sbs.mp4" : dato.formato == "PNG" ? "foto_sbs.png" : "foto_sbs.jpg");
                dato.miniatura = Path.Combine(documento.Key, "miniatura.jpg");
                if (File.Exists(dato.ruta)) resultado.Add(dato);
            }
            resultado.Sort((a, b) => string.CompareOrdinal(b.fechaUtc, a.fechaUtc));
            return resultado;
        }

        public Task EliminarAsync(DatosCaptura captura)
        {
            if (captura == null || string.IsNullOrEmpty(captura.ruta)) throw new ArgumentException("No hay captura seleccionada.");
            string carpeta = Path.GetDirectoryName(Path.GetFullPath(captura.ruta));
            if (!string.Equals(Path.GetDirectoryName(carpeta), raiz, StringComparison.OrdinalIgnoreCase) ||
                Path.GetFileName(carpeta).StartsWith(".", StringComparison.Ordinal))
                throw new InvalidOperationException("La captura no pertenece al almacén de esta aplicación.");
            return Task.Run(() => { if (Directory.Exists(carpeta)) Directory.Delete(carpeta, true); });
        }
    }
}
