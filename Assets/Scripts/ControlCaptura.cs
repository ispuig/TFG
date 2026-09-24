using System;

namespace TFG.Captura
{
    public enum EstadoCaptura { Inicializando, SinPermiso, Listo, CapturandoFoto, Guardando, Suspendido, Error, Grabando, Finalizando }

    /// <summary>Puerta única para los comandos, también cuando no proceden de un botón.</summary>
    public sealed class ControlCaptura
    {
        public EstadoCaptura Estado { get; private set; } = EstadoCaptura.Inicializando;
        public string Mensaje { get; private set; } = "Preparando cámaras…";
        public event Action Cambiado;

        public bool IntentarCapturar()
        {
            if (Estado != EstadoCaptura.Listo) return false;
            Cambiar(EstadoCaptura.CapturandoFoto, "Esperando un par de imágenes nuevas…");
            return true;
        }

        internal void Cambiar(EstadoCaptura estado, string mensaje)
        {
            Estado = estado;
            Mensaje = mensaje;
            Cambiado?.Invoke();
        }
    }

    public static class ValidacionParStereo
    {
        public static bool EsValido(long izquierda, long derecha, long anteriorIzquierda,
            long anteriorDerecha, double toleranciaMilisegundos)
        {
            if (double.IsNaN(toleranciaMilisegundos) || double.IsInfinity(toleranciaMilisegundos) ||
                toleranciaMilisegundos < 0 || izquierda <= 0 || derecha <= 0 ||
                izquierda <= anteriorIzquierda || derecha <= anteriorDerecha) return false;
            // Restar antes de convertir: los ticks de DateTime son demasiado grandes para conservar
            // diferencias pequeñas si primero se convierten por separado a double.
            long diferencia = izquierda >= derecha ? izquierda - derecha : derecha - izquierda;
            return diferencia <= toleranciaMilisegundos * TimeSpan.TicksPerMillisecond;
        }
    }
}
