using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TFG.Captura;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using UnityEditor.Build.Reporting;

// Inspecciona la escena y dibuja la interfaz real en una copia temporal del proyecto.
public static class PruebasProyectoUnity
{
    public static void CompilarAndroid()
    {
        try
        {
            // Solo en la copia temporal: API 35 está instalada en el SDK local.
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevel35;
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            var informe = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/SampleScene.unity" }, target = BuildTarget.Android,
                locationPathName = "Captura-Prueba.apk", options = BuildOptions.Development
            });
            if (informe.summary.result != BuildResult.Succeeded)
                throw new Exception("La compilación Android terminó en " + informe.summary.result + " con " + informe.summary.totalErrors + " errores.");
            File.WriteAllText("resultado_android.txt", "PASS: APK de desarrollo ARM64/IL2CPP, target API 35, SampleScene. No instalado ni ejecutado en Quest.");
            EditorApplication.Exit(0);
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }

    public static async void Ejecutar()
    {
        var errores = new List<string>();
        Application.logMessageReceived += (mensaje, _, tipo) =>
        {
            if (tipo == LogType.Error || tipo == LogType.Exception) errores.Add(mensaje);
        };
        try
        {
            foreach (string archivo in new[] { "TFGVideo", "RgbaYuv" })
            {
                var plugin = AssetImporter.GetAtPath("Assets/AndroidPlugins/" + archivo + ".java") as PluginImporter;
                if (!plugin || !plugin.GetCompatibleWithPlatform(BuildTarget.Android) || plugin.GetCompatibleWithEditor())
                    throw new Exception("Importación Android incorrecta: " + archivo);
            }
            foreach (string ruta in new[] { "Assets/_Recovery/0.unity", "Assets/Scenes/SampleScene.unity" })
            {
                var escena = EditorSceneManager.OpenScene(ruta);
                foreach (var raiz in escena.GetRootGameObjects())
                    foreach (var transform in raiz.GetComponentsInChildren<Transform>(true))
                        if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject) > 0)
                            throw new Exception("Componente perdido en " + ruta + ": " + transform.name);
            }
            var gestor = UnityEngine.Object.FindFirstObjectByType<GestorCaptura>();
            var interfaz = UnityEngine.Object.FindFirstObjectByType<InterfazCaptura>();
            if (!gestor || !interfaz) throw new Exception("Falta la raíz o la interfaz en SampleScene.");
            var serializado = new SerializedObject(gestor);
            foreach (string nombre in new[] { "camaraIzquierda", "camaraDerecha", "stereoCompositeMaterial" })
                if (!serializado.FindProperty(nombre).objectReferenceValue) throw new Exception("Referencia vacía: " + nombre);

            // Solo para esta comprobación de distribución 2D. XR y URP se verifican en dispositivo.
            GraphicsSettings.defaultRenderPipeline = null;
            QualitySettings.renderPipeline = null;
            Invocar(gestor, "Awake");
            var proveedor = new ProveedorCamarasMeta(
                (Meta.XR.PassthroughCameraAccess)serializado.FindProperty("camaraIzquierda").objectReferenceValue,
                (Meta.XR.PassthroughCameraAccess)serializado.FindProperty("camaraDerecha").objectReferenceValue, true);
            gestor.GetType().GetField("proveedor", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(gestor, proveedor);
            typeof(ControlCaptura).GetMethod("Cambiar", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(gestor.Control, new object[] { EstadoCaptura.Listo, "SIMULACIÓN · Cámaras listas · 320 × 240 por ojo" });
            Invocar(interfaz, "Start");
            Invocar(interfaz, "Update");
            var canvas = new SerializedObject(interfaz).FindProperty("lienzo").objectReferenceValue as Canvas;
            canvas.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            canvas.transform.localScale = Vector3.one * 0.002f;
            var camara = new GameObject("Cámara de prueba UI").AddComponent<Camera>();
            camara.transform.position = new Vector3(0, 0, -2);
            camara.orthographic = true; camara.orthographicSize = 0.60f;
            camara.cullingMask = 1 << canvas.gameObject.layer;
            camara.clearFlags = CameraClearFlags.SolidColor;
            camara.backgroundColor = new Color(0.015f, 0.025f, 0.04f);
            camara.enabled = false;
            canvas.worldCamera = camara;
            Render(interfaz, canvas, camara, "interfaz_captura.png");
            LimpiarFixture(interfaz);
            Invocar(interfaz, "Mostrar", "Ajustes");
            Invocar(interfaz, "Update");
            Render(interfaz, canvas, camara, "interfaz_ajustes.png");

            string raizCapturas = Path.GetFullPath("capturas_prueba_ui");
            var repositorio = new RepositorioCapturas(raizCapturas);
            interfaz.GetType().GetField("repositorio", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(interfaz, repositorio);
            if (!proveedor.IntentarObtener(out var par)) throw new Exception("Falta el par simulado.");
            using (var compositor = new CompositorStereo((Material)serializado.FindProperty("stereoCompositeMaterial").objectReferenceValue))
            {
                var foto = await compositor.CapturarAsync(par, FormatoFoto.PNG, 90, CancellationToken.None);
                var datos = new DatosCaptura { anchoOjo = par.AnchoOjo, altoOjo = par.AltoOjo, fechaUtc = DateTime.UtcNow.ToString("O"), simulacion = true };
                await new AlmacenCapturas(raizCapturas).GuardarFotoAsync(foto.Imagen, foto.Miniatura, JsonUtility.ToJson(datos), FormatoFoto.PNG, CancellationToken.None);
            }
            LimpiarFixture(interfaz);
            Invocar(interfaz, "MostrarGaleria");
            await EsperarUI(interfaz);
            Invocar(interfaz, "Update");
            Render(interfaz, canvas, camara, "interfaz_galeria.png");
            var lista = await repositorio.ListarAsync();
            LimpiarFixture(interfaz);
            Invocar(interfaz, "Abrir", lista[0]);
            await EsperarUI(interfaz);
            Invocar(interfaz, "Update");
            Render(interfaz, canvas, camara, "interfaz_visor.png");
            if (errores.Count > 0) throw new Exception("La prueba registró errores: " + string.Join(" | ", errores));
            File.WriteAllText("resultado_proyecto.txt", "PASS: importación Java, escenas sin scripts perdidos, referencias raíz, Captura/Ajustes/Galería/Visor dentro del lienzo y sin errores durante la prueba.");
            EditorApplication.Exit(0);
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }

    private static void Invocar(object instancia, string metodo, params object[] args) =>
        instancia.GetType().GetMethod(metodo, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(instancia, args);

    private static async Task EsperarUI(InterfazCaptura interfaz)
    {
        double limite = EditorApplication.timeSinceStartup + 15;
        var campo = interfaz.GetType().GetField("cargando", BindingFlags.Instance | BindingFlags.NonPublic);
        while ((bool)campo.GetValue(interfaz))
        {
            if (EditorApplication.timeSinceStartup > limite) throw new TimeoutException("Carga UI bloqueada.");
            await Task.Yield();
        }
    }

    private static void LimpiarFixture(InterfazCaptura interfaz)
    {
        // El código de producción usa Destroy al final del frame. Esta prueba de distribución
        // se ejecuta fuera de Play: retirar explícitamente sus objetos antes de navegar.
        var contenido = (RectTransform)interfaz.GetType().GetField("contenido", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(interfaz);
        foreach (Transform hijo in contenido.Cast<Transform>().ToArray()) UnityEngine.Object.DestroyImmediate(hijo.gameObject);
        var texturas = (List<Texture2D>)interfaz.GetType().GetField("texturas", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(interfaz);
        foreach (var textura in texturas) if (textura) UnityEngine.Object.DestroyImmediate(textura);
        texturas.Clear();
    }

    private static void Render(InterfazCaptura interfaz, Canvas canvas, Camera camara, string archivo)
    {
        Canvas.ForceUpdateCanvases();
        var lienzo = (RectTransform)canvas.transform;
        var botones = canvas.GetComponentsInChildren<Button>().Where(b => b.gameObject.activeInHierarchy).ToArray();
        var esquinas = new Vector3[4];
        foreach (var boton in botones)
        {
            ((RectTransform)boton.transform).GetWorldCorners(esquinas);
            foreach (var esquina in esquinas)
                if (!lienzo.rect.Contains(lienzo.InverseTransformPoint(esquina)))
                    throw new Exception("Botón fuera de la interfaz: " + boton.name);
        }
        var rt = RenderTexture.GetTemporary(1000, 830, 24);
        var anterior = RenderTexture.active;
        var imagen = new Texture2D(1000, 830, TextureFormat.RGB24, false);
        try
        {
            camara.targetTexture = rt; camara.Render();
            RenderTexture.active = rt;
            imagen.ReadPixels(new Rect(0, 0, 1000, 830), 0, 0);
            File.WriteAllBytes(archivo, imagen.EncodeToPNG());
        }
        finally
        {
            camara.targetTexture = null; RenderTexture.active = anterior;
            RenderTexture.ReleaseTemporary(rt); UnityEngine.Object.DestroyImmediate(imagen);
        }
    }
}
