using System;

namespace TFG.Captura
{
    public enum FormatoFoto { PNG, JPEG }

    [Serializable]
    public sealed class DatosCaptura
    {
        public int version = 1;
        public string tipo = "foto", formato = "PNG", disposicion = "SBS", ordenOjos = "LR";
        public string fechaUtc;
        public int anchoOjo, altoOjo, calidadJpeg;
        public long tiempoIzquierdaTicks, tiempoDerechaTicks;
        public bool simulacion;
        public string codec;
        public double duracionSegundos;
        public int fpsObjetivo, framesGuardados, framesOmitidos;
        [NonSerialized] public string ruta;
        [NonSerialized] public string miniatura;
    }

    public sealed class FotoProcesada
    {
        public readonly byte[] Imagen, Miniatura;
        public FotoProcesada(byte[] imagen, byte[] miniatura) { Imagen = imagen; Miniatura = miniatura; }
    }
}
