using System;
using System.IO;
using System.Linq;
using System.Reflection;
using TFG.Captura;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

// Inspecciona la escena y dibuja la interfaz real en una copia temporal del proyecto.
public static class PruebasProyectoUnity
{
    public static void Ejecutar()
    {
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
            Invocar(interfaz, "Mostrar", "Ajustes");
            Invocar(interfaz, "Update");
            Render(interfaz, canvas, camara, "interfaz_ajustes.png");
            File.WriteAllText("resultado_proyecto.txt", "PASS: importación Java, escenas sin scripts perdidos, referencias raíz e interfaz dentro del lienzo.");
            EditorApplication.Exit(0);
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }

    private static void Invocar(object instancia, string metodo, params object[] args) =>
        instancia.GetType().GetMethod(metodo, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(instancia, args);

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
