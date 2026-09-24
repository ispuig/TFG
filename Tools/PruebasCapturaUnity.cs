using System;
using System.IO;
using System.Threading;
using TFG.Captura;
using UnityEditor;
using UnityEngine;

// Se copia a Assets/Editor de un proyecto de pruebas aislado, nunca a la escena de trabajo.
public static class PruebasCapturaUnity
{
    public static async void Ejecutar()
    {
        int salida = 1;
        Texture2D izquierda = null, derecha = null, decodificada = null;
        Material material = null;
        try
        {
            material = AssetDatabase.LoadAssetAtPath<Material>("Assets/CurrentMaterialOutput/StereoComposite.mat");
            if (!material) throw new Exception("No se importó el material.");
            izquierda = Crear(Color.red, Color.green);
            derecha = Crear(Color.blue, Color.yellow);
            using (var compositor = new CompositorStereo(material))
            {
                byte[] png = await compositor.CapturarPngAsync(new ParStereo(izquierda, derecha, 1, 1), CancellationToken.None);
                decodificada = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!decodificada.LoadImage(png)) throw new Exception("El PNG no se puede decodificar.");
                if (decodificada.width != 8 || decodificada.height != 2) throw new Exception("Dimensiones SBS incorrectas.");
                ComprobarColor(decodificada.GetPixel(0, 0), Color.red, "L abajo");
                ComprobarColor(decodificada.GetPixel(0, 1), Color.green, "L arriba");
                ComprobarColor(decodificada.GetPixel(4, 0), Color.blue, "R abajo");
                ComprobarColor(decodificada.GetPixel(4, 1), Color.yellow, "R arriba");
                ComprobarVisor(decodificada, 0, Color.red, Color.green);
                ComprobarVisor(decodificada, 1, Color.blue, Color.yellow);
                byte[] rgba = await compositor.CapturarRgbaAsync(new ParStereo(izquierda, derecha, 1, 1), 4, 2, CancellationToken.None);
                if (rgba.Length != 8 * 2 * 4) throw new Exception("Frame RGBA de vídeo incompleto.");
                if (rgba[0] != 255 || rgba[1] != 0 || rgba[2] != 0 || rgba[3] != 255)
                    throw new Exception("El frame de vídeo no respeta el contrato RGBA.");
                // LoadImage puede cambiar el formato interno; el consumidor de vídeo exige RGBA32.
                decodificada.Reinitialize(8, 2, TextureFormat.RGBA32, false);
                decodificada.LoadRawTextureData(rgba);
                ComprobarColor(decodificada.GetPixel(0, 0), Color.red, "Vídeo L abajo");
                ComprobarColor(decodificada.GetPixel(0, 1), Color.green, "Vídeo L arriba");
                ComprobarColor(decodificada.GetPixel(4, 0), Color.blue, "Vídeo R abajo");
                ComprobarColor(decodificada.GetPixel(4, 1), Color.yellow, "Vídeo R arriba");
                File.WriteAllBytes(Path.Combine(Application.dataPath, "../primera_captura.png"), png);
                // Sobrescribir la fuente y demostrar que la primera instantánea no cambia.
                izquierda.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white,
                    Color.white, Color.white, Color.white, Color.white });
                izquierda.Apply();
                byte[] segunda = await compositor.CapturarPngAsync(new ParStereo(izquierda, derecha, 2, 2), CancellationToken.None);
                ComprobarColor(decodificada.GetPixel(0, 0), Color.red, "Instantánea anterior independiente");
                decodificada.LoadImage(segunda);
                ComprobarColor(decodificada.GetPixel(0, 0), Color.white, "Segunda captura actualizada");
                FotoProcesada jpeg = await compositor.CapturarAsync(new ParStereo(izquierda, derecha, 3, 3), FormatoFoto.JPEG, 90, CancellationToken.None);
                if (!decodificada.LoadImage(jpeg.Imagen) || decodificada.width != 8 || decodificada.height != 2)
                    throw new Exception("JPEG inválido.");
                if (!decodificada.LoadImage(jpeg.Miniatura) || decodificada.width != 4 || decodificada.height != 2)
                    throw new Exception("Miniatura JPEG inválida.");
                string almacenPath = Path.Combine(Application.dataPath, "../capturas_prueba");
                var almacen = new AlmacenCapturas(almacenPath);
                var dato = new DatosCaptura { anchoOjo = 4, altoOjo = 2, formato = "JPEG", fechaUtc = DateTime.UtcNow.ToString("O") };
                string ruta = await almacen.GuardarFotoAsync(jpeg.Imagen, jpeg.Miniatura, JsonUtility.ToJson(dato), FormatoFoto.JPEG, CancellationToken.None);
                var catalogo = new RepositorioCapturas(almacenPath);
                var lista = await catalogo.ListarAsync();
                if (lista.Count != 1 || lista[0].ruta != ruta || lista[0].formato != "JPEG") throw new Exception("Catálogo no recuperable.");
                // Una carpeta incompleta/corrupta no impide consultar las capturas correctas.
                Directory.CreateDirectory(Path.Combine(almacenPath, "corrupta"));
                File.WriteAllText(Path.Combine(almacenPath, "corrupta/captura.json"), "INVALIDO");
                if ((await catalogo.ListarAsync()).Count != 1) throw new Exception("No se ignora la entrada corrupta.");
                bool rutaRechazada = false;
                try { await catalogo.EliminarAsync(new DatosCaptura { ruta = Path.Combine(Application.dataPath, "ajeno/foto.png") }); }
                catch (InvalidOperationException) { rutaRechazada = true; }
                if (!rutaRechazada) throw new Exception("El catálogo admite borrar fuera de su raíz.");
                await catalogo.EliminarAsync(lista[0]);
                if ((await catalogo.ListarAsync()).Count != 0 || File.Exists(ruta)) throw new Exception("Eliminación incompleta.");
                using (var cancelacion = new CancellationTokenSource())
                {
                    cancelacion.Cancel();
                    bool cancelado = false;
                    try { await compositor.CapturarPngAsync(new ParStereo(izquierda, derecha, 3, 3), cancelacion.Token); }
                    catch (OperationCanceledException) { cancelado = true; }
                    if (!cancelado) throw new Exception("Se ignoró la cancelación.");
                }
                Debug.Log("PASS: compositor GPU, PNG/JPEG, miniatura, visor por ojo, orientación, instantáneas independientes, catálogo, corrupción, eliminación y cancelación.");
                File.WriteAllText(Path.Combine(Application.dataPath, "../resultado.txt"), "PASS\n" + SystemInfo.graphicsDeviceName);
                salida = 0;
            }
        }
        catch (Exception error) { Debug.LogException(error); }
        finally
        {
            if (izquierda) UnityEngine.Object.DestroyImmediate(izquierda);
            if (derecha) UnityEngine.Object.DestroyImmediate(derecha);
            if (decodificada) UnityEngine.Object.DestroyImmediate(decodificada);
            EditorApplication.Exit(salida);
        }
    }

    private static Texture2D Crear(Color inferior, Color superior)
    {
        var imagen = new Texture2D(4, 2, TextureFormat.RGBA32, false);
        imagen.filterMode = FilterMode.Point;
        imagen.SetPixels(new[] { inferior, inferior, inferior, inferior, superior, superior, superior, superior });
        imagen.Apply();
        return imagen;
    }

    private static void ComprobarColor(Color actual, Color esperado, string etiqueta)
    {
        if (Math.Abs(actual.r - esperado.r) > 0.025 || Math.Abs(actual.g - esperado.g) > 0.025 ||
            Math.Abs(actual.b - esperado.b) > 0.025 || actual.a < 0.99)
            throw new Exception(etiqueta + ": esperado " + esperado + ", obtenido " + actual);
    }

    private static void ComprobarVisor(Texture2D sbs, int ojo, Color abajo, Color arriba)
    {
        Shader shader = Shader.Find("TFG/StereoViewer");
        if (!shader || !shader.isSupported) throw new Exception("Shader del visor no disponible.");
        var material = new Material(shader);
        var rt = RenderTexture.GetTemporary(4, 2, 0);
        var anterior = RenderTexture.active;
        var lectura = new Texture2D(4, 2, TextureFormat.RGBA32, false);
        try
        {
            material.SetTexture("_MainTex", sbs);
            material.SetFloat("_EyeOverride", ojo);
            RenderTexture.active = rt;
            GL.Clear(true, true, Color.black);
            GL.PushMatrix();
            try
            {
                GL.LoadOrtho();
                material.SetPass(0);
                GL.Begin(GL.QUADS);
                GL.Color(Color.white);
                GL.TexCoord2(0, 0); GL.Vertex3(0, 0, 0);
                GL.TexCoord2(0, 1); GL.Vertex3(0, 1, 0);
                GL.TexCoord2(1, 1); GL.Vertex3(1, 1, 0);
                GL.TexCoord2(1, 0); GL.Vertex3(1, 0, 0);
                GL.End();
            }
            finally { GL.PopMatrix(); }
            lectura.ReadPixels(new Rect(0, 0, 4, 2), 0, 0, false);
            ComprobarColor(lectura.GetPixel(0, 0), abajo, "Visor ojo " + ojo + " abajo");
            ComprobarColor(lectura.GetPixel(0, 1), arriba, "Visor ojo " + ojo + " arriba");
        }
        finally
        {
            RenderTexture.active = anterior;
            RenderTexture.ReleaseTemporary(rt);
            UnityEngine.Object.DestroyImmediate(material);
            UnityEngine.Object.DestroyImmediate(lectura);
        }
    }
}
