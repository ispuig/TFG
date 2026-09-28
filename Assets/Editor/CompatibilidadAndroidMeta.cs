using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor.Android;
using UnityEditor.PackageManager;
using UnityEngine;

/// <summary>Adapta la compilación de Meta XR 83 a la comprobación de namespaces de AGP 9.</summary>
public sealed class CompatibilidadAndroidMeta : IPostGenerateGradleAndroidProject
{
    // Ejecutar después de los postprocesadores de Meta y de Unity.
    public int callbackOrder => int.MaxValue;

    public void OnPostGenerateGradleAndroidProject(string path)
    {
        var paquete = PackageInfo.FindForAssembly(typeof(OVRManager).Assembly);
        if (paquete == null || !paquete.version.StartsWith("83.", StringComparison.Ordinal)) return;

        string raiz = Directory.GetParent(path)?.FullName;
        if (raiz == null) return;
        string propiedades = Path.Combine(raiz, "gradle.properties");
        if (!File.Exists(propiedades)) return;

        var lineas = new List<string>(File.ReadAllLines(propiedades));
        string versionAgp = LeerPropiedad(lineas, "unity.agpVersion");
        if (versionAgp == null || !int.TryParse(versionAgp.Split('.')[0], out int mayor) || mayor < 9) return;

        // InteractionSdk, SDKTelemetry y OVRPlugin de Meta 83 comparten namespace.
        // La comprobación de clases duplicadas de Gradle permanece activa.
        const string clave = "android.uniquePackageNames";
        if (LeerPropiedad(lineas, clave) == "false") return;
        bool sustituida = false;
        for (int i = 0; i < lineas.Count; i++)
        {
            int separador = lineas[i].IndexOf('=');
            if (separador < 0 || lineas[i].Substring(0, separador).Trim() != clave) continue;
            lineas[i] = clave + "=false";
            sustituida = true;
        }
        if (!sustituida)
        {
            lineas.Add("# Compatibilidad de Meta XR 83 con AGP 9; revisar al actualizar el SDK.");
            lineas.Add(clave + "=false");
        }
        File.WriteAllLines(propiedades, lineas, new UTF8Encoding(false));
        Debug.Log("Compatibilidad Android aplicada: Meta XR 83 comparte namespaces entre sus bibliotecas.");
    }

    private static string LeerPropiedad(List<string> lineas, string clave)
    {
        string resultado = null;
        foreach (string linea in lineas)
        {
            int separador = linea.IndexOf('=');
            if (separador >= 0 && linea.Substring(0, separador).Trim() == clave)
                resultado = linea.Substring(separador + 1).Trim();
        }
        return resultado;
    }
}
