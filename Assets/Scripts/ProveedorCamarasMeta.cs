using System;
using Meta.XR;
using UnityEngine;

namespace TFG.Captura
{
    /// <summary>Adapta MRUK 83. Las texturas físicas pertenecen al SDK.</summary>
    public sealed class ProveedorCamarasMeta : IDisposable
    {
        private readonly PassthroughCameraAccess izquierda, derecha;
        public bool EsSimulacion { get; }
#if UNITY_EDITOR
        private Texture2D pruebaIzquierda, pruebaDerecha;
#endif
        public ProveedorCamarasMeta(PassthroughCameraAccess izquierda, PassthroughCameraAccess derecha, bool simularEnEditor)
        {
            this.izquierda = izquierda;
            this.derecha = derecha;
#if UNITY_EDITOR
            EsSimulacion = simularEnEditor;
            if (EsSimulacion)
            {
                if (izquierda) izquierda.enabled = false;
                if (derecha) derecha.enabled = false;
                pruebaIzquierda = CrearPatron(true);
                pruebaDerecha = CrearPatron(false);
                return;
            }
#endif
            if (!izquierda || !derecha || izquierda == derecha)
                throw new InvalidOperationException("Asigna dos accesos de cámara distintos en GestorCaptura.");
            if (izquierda.CameraPosition != PassthroughCameraAccess.CameraPositionType.Left ||
                derecha.CameraPosition != PassthroughCameraAccess.CameraPositionType.Right)
                throw new InvalidOperationException("Las referencias de cámara izquierda y derecha están intercambiadas.");
            if (!izquierda.gameObject.activeInHierarchy || !derecha.gameObject.activeInHierarchy)
                throw new InvalidOperationException("Los objetos de ambas cámaras deben estar activos.");
            izquierda.enabled = false;
            derecha.enabled = false;
        }

        public void Activar()
        {
            if (EsSimulacion) return;
            izquierda.enabled = true;
            derecha.enabled = true;
        }

        public bool IntentarObtener(out ParStereo par)
        {
            par = default;
#if UNITY_EDITOR
            if (EsSimulacion)
            {
                long instante = (long)(Time.realtimeSinceStartupAsDouble * TimeSpan.TicksPerSecond) + 1;
                par = new ParStereo(pruebaIzquierda, pruebaDerecha, instante, instante);
                return true;
            }
#endif
            if (!izquierda || !derecha || !izquierda.IsPlaying || !derecha.IsPlaying) return false;
            Texture texturaL = izquierda.GetTexture();
            Texture texturaR = derecha.GetTexture();
            if (!texturaL || !texturaR || texturaL.width <= 0 || texturaL.height <= 0) return false;
            if (texturaL.width != texturaR.width || texturaL.height != texturaR.height)
                throw new InvalidOperationException("Las cámaras entregan resoluciones distintas. Configura el mismo tamaño para ambos ojos.");
            par = new ParStereo(texturaL, texturaR, izquierda.Timestamp.Ticks, derecha.Timestamp.Ticks);
            return par.TiempoIzquierda > 0 && par.TiempoDerecha > 0;
        }

        public void Dispose()
        {
            if (izquierda) izquierda.enabled = false;
            if (derecha) derecha.enabled = false;
#if UNITY_EDITOR
            if (pruebaIzquierda) UnityEngine.Object.Destroy(pruebaIzquierda);
            if (pruebaDerecha) UnityEngine.Object.Destroy(pruebaDerecha);
#endif
        }

#if UNITY_EDITOR
        private static Texture2D CrearPatron(bool izquierdo)
        {
            const int ancho = 320, alto = 240;
            var textura = new Texture2D(ancho, alto, TextureFormat.RGBA32, false);
            textura.name = izquierdo ? "Prueba ojo L" : "Prueba ojo R";
            var pixeles = new Color32[ancho * alto];
            Color32 fondo = izquierdo ? new Color32(220, 40, 40, 255) : new Color32(30, 90, 220, 255);
            for (int y = 0; y < alto; y++)
            for (int x = 0; x < ancho; x++)
            {
                bool marcaSuperior = y > alto - 24;
                bool letra = x >= 100 && x < 120 && y >= 60 && y < 180;
                if (izquierdo) letra |= y >= 60 && y < 80 && x >= 100 && x < 210;
                else letra |= (y >= 160 && y < 180 || y >= 110 && y < 130) && x >= 100 && x < 200 ||
                              x >= 180 && x < 200 && y >= 110 && y < 180 ||
                              x >= 120 && x < 200 && Math.Abs(y - (230 - x)) < 12;
                pixeles[y * ancho + x] = marcaSuperior ? new Color32(255, 220, 0, 255) :
                    letra ? new Color32(255, 255, 255, 255) : fondo;
            }
            textura.SetPixels32(pixeles);
            textura.Apply(false, true);
            return textura;
        }
#endif
    }
}
