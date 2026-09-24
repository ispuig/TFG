using UnityEngine;

namespace TFG.Captura
{
    /// <summary>Vistas prestadas de la fuente. Copiar su contenido antes de conservar una captura.</summary>
    public readonly struct ParStereo
    {
        public readonly Texture Izquierda, Derecha;
        public readonly long TiempoIzquierda, TiempoDerecha;
        public int AnchoOjo => Izquierda.width;
        public int AltoOjo => Izquierda.height;

        public ParStereo(Texture izquierda, Texture derecha, long tiempoIzquierda, long tiempoDerecha)
        {
            Izquierda = izquierda;
            Derecha = derecha;
            TiempoIzquierda = tiempoIzquierda;
            TiempoDerecha = tiempoDerecha;
        }
    }
}
