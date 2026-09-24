using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TFG.Captura;

// Se compila con los archivos de producción. No necesita paquetes ni modifica datos del usuario.
internal static class PruebasCaptura
{
    private static int comprobaciones;
    private static void Verificar(bool condicion, string descripcion)
    {
        if (!condicion) throw new Exception(descripcion);
        comprobaciones++;
        Console.WriteLine("PASS " + descripcion);
    }

    public static async Task<int> Main(string[] args)
    {
        string raiz = Path.Combine(Path.GetFullPath(args[0]), "prueba_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(raiz);
        try
        {
            var control = new ControlCaptura();
            Verificar(!control.IntentarCapturar(), "No capturar durante inicialización");
            foreach (EstadoCaptura estado in Enum.GetValues(typeof(EstadoCaptura)))
            {
                control.Cambiar(estado, estado.ToString());
                Verificar(control.IntentarCapturar() == (estado == EstadoCaptura.Listo), "Puerta de captura en " + estado);
            }
            control.Cambiar(EstadoCaptura.Listo, "listo");
            Verificar(control.IntentarCapturar() && !control.IntentarCapturar(), "Doble pulsación produce una sola captura");

            const long t = 638_900_000_000_000_000;
            Verificar(ValidacionParStereo.EsValido(t, t + 10_000, t - 1, t - 1, 20), "Aceptar par fresco dentro de tolerancia");
            Verificar(!ValidacionParStereo.EsValido(t, t + 10_000, t, t - 1, 20), "Rechazar ojo izquierdo repetido");
            Verificar(!ValidacionParStereo.EsValido(t, t, t - 1, t, 20), "Rechazar ojo derecho repetido");
            Verificar(!ValidacionParStereo.EsValido(0, t, 0, 0, 20), "Rechazar timestamp vacío");
            Verificar(!ValidacionParStereo.EsValido(t, t + 300_000, 0, 0, 20), "Rechazar par con 30 ms de diferencia");
            Verificar(!ValidacionParStereo.EsValido(t, t, 0, 0, double.NaN), "Rechazar tolerancia no numérica");
            Verificar(!ValidacionParStereo.EsValido(t, t, 0, 0, -1), "Rechazar tolerancia negativa");
            Verificar(!ValidacionParStereo.EsValido(t, t + 1, 0, 0, 0), "Tolerancia cero distingue un tick sin perder precisión");
            Verificar(ValidacionParStereo.EsValido(t, t + 200_000, 0, 0, 20) &&
                !ValidacionParStereo.EsValido(t, t + 200_001, 0, 0, 20), "Comprobar el límite exacto de tolerancia");

            byte[] png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aRZkAAAAASUVORK5CYII=");
            var almacen = new AlmacenCapturas(raiz);
            string[] rutas = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ =>
                almacen.GuardarPngAsync(png, "{\"disposicion\":\"SBS\"}", CancellationToken.None)));
            Verificar(rutas.Distinct().Count() == 12, "Doce guardados concurrentes no sobrescriben capturas");
            Verificar(rutas.All(ruta => File.ReadAllBytes(ruta).SequenceEqual(png)), "PNG conservados byte por byte");
            Verificar(rutas.All(ruta => File.Exists(Path.Combine(Path.GetDirectoryName(ruta), "captura.json"))), "Todas las fotos publicadas tienen descripción");
            Verificar(!Directory.GetDirectories(raiz).Any(ruta => ruta.EndsWith(".tmp")), "No quedan carpetas temporales tras guardar");
            using (var cancelacion = new CancellationTokenSource())
            {
                cancelacion.Cancel();
                bool cancelado = false;
                try { await almacen.GuardarPngAsync(png, "{}", cancelacion.Token); }
                catch (OperationCanceledException) { cancelado = true; }
                Verificar(cancelado && Directory.GetDirectories(raiz).Length == 12, "Cancelar no publica una captura");
            }
            bool rechazado = false;
            try { await almacen.GuardarPngAsync(new byte[] { 1, 2, 3 }, "{}", CancellationToken.None); }
            catch (ArgumentException) { rechazado = true; }
            Verificar(rechazado, "Rechazar datos sin firma PNG");
            string bloqueo = Path.Combine(raiz, "archivo_en_vez_de_directorio");
            File.WriteAllText(bloqueo, "fixture");
            bool falloReal = false;
            try { await new AlmacenCapturas(bloqueo).GuardarPngAsync(png, "{}", CancellationToken.None); }
            catch (IOException) { falloReal = true; }
            Verificar(falloReal, "Propagar fallo de disco sin éxito falso");
            string temporalVideo;
            using (var video = new SesionVideo(raiz))
            {
                temporalVideo = Path.GetDirectoryName(video.RutaVideo);
                File.WriteAllBytes(video.RutaVideo, new byte[64]); // Fixture de almacenamiento, no vídeo decodificable.
                Verificar(Path.GetFileName(temporalVideo).StartsWith("."), "Vídeo en curso oculto al catálogo");
                using (var cancelacion = new CancellationTokenSource())
                {
                    cancelacion.Cancel();
                    bool cancelado = false;
                    try { await video.PublicarAsync("{}", null, cancelacion.Token); }
                    catch (OperationCanceledException) { cancelado = true; }
                    Verificar(cancelado, "Vídeo cancelado no se publica");
                }
            }
            Verificar(!Directory.Exists(temporalVideo), "Descartar vídeo limpia únicamente su sesión");
            string publicado;
            using (var video = new SesionVideo(raiz))
            {
                File.WriteAllBytes(video.RutaVideo, new byte[64]);
                publicado = await video.PublicarAsync("{\"tipo\":\"video\"}", new byte[] { 1 }, CancellationToken.None);
            }
            Verificar(File.Exists(publicado) && File.Exists(Path.Combine(Path.GetDirectoryName(publicado), "captura.json")),
                "Vídeo confirmado conserva archivo y descripción tras liberar sesión");
            Verificar(rutas.All(File.Exists), "Descartar vídeo conserva las fotos anteriores");
            Console.WriteLine("RESULTADO: " + comprobaciones + " comprobaciones correctas.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { Directory.Delete(raiz, true); }
    }
}
