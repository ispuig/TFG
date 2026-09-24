using System;
using System.Collections;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Meta.XR;
using TFG.Captura;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

/// <summary>Raíz del flujo de captura. Toda API Unity se utiliza desde el hilo principal.</summary>
[DefaultExecutionOrder(-200)]
public sealed class GestorCaptura : MonoBehaviour
{
    [SerializeField] private PassthroughCameraAccess camaraIzquierda;
    [SerializeField] private PassthroughCameraAccess camaraDerecha;
    [SerializeField] private Material stereoCompositeMaterial;
    [SerializeField, Min(1)] private float tiempoEsperaCamaras = 15;
    [SerializeField, Min(1)] private float tiempoEsperaFoto = 5;
    [SerializeField, Range(0, 50)] private float toleranciaStereoMs = 20;
    [Tooltip("Solo en el Editor: patrones L/R; nunca cámaras reales del visor.")]
    [SerializeField] private bool simularEnEditor = true;

    public ControlCaptura Control { get; } = new ControlCaptura();
    public string UltimaCaptura { get; private set; }
    public bool EsSimulacion => proveedor != null && proveedor.EsSimulacion;
    public bool Ocupado => operacion != null;
    public bool PuedeCapturar => !Ocupado && Control.Estado == EstadoCaptura.Listo;
    public FormatoFoto Formato { get; private set; }
    public int CalidadJpeg { get; private set; } = 90;
    public bool H264Disponible { get; private set; }
    public bool HevcDisponible { get; private set; }
    public bool UsarHevc { get; private set; }
    public bool VideoDisponible => UsarHevc ? HevcDisponible : H264Disponible;
    public double DuracionGrabacion { get; private set; }
    public int FramesOmitidos { get; private set; }
    private bool detenerVideo;
    private bool descartarVideo;
    private string avisoInterrupcion;
    private int indicePerfil;
    private readonly bool[] perfilesH264 = new bool[CodificadorVideoAndroid.Perfiles.Length];
    private readonly bool[] perfilesHevc = new bool[CodificadorVideoAndroid.Perfiles.Length];
    public PerfilVideo Perfil => CodificadorVideoAndroid.Perfiles[indicePerfil];
    public Texture VistaPrevia
    {
        get
        {
            try { return proveedor != null && proveedor.IntentarObtener(out var par) ? par.Izquierda : null; }
            catch (Exception) { return null; } // La adquisición del gestor comunica el error con su estado.
        }
    }
    public event Action<string> FotoGuardada;

    private ProveedorCamarasMeta proveedor;
    private CompositorStereo compositor;
    private AlmacenCapturas almacen;
    private CancellationTokenSource operacion;
    private bool suspendido, reiniciarAlTerminar, destruir, pausaDelPermiso;
    private long ultimoTiempoL, ultimoTiempoR;
    private double ultimaImagenNueva;

    private void Awake()
    {
        Formato = PlayerPrefs.GetInt("TFG.FormatoFoto", 0) == 1 ? FormatoFoto.JPEG : FormatoFoto.PNG;
        CalidadJpeg = Mathf.Clamp(PlayerPrefs.GetInt("TFG.CalidadJpeg", 90), 1, 100);
        indicePerfil = Mathf.Clamp(PlayerPrefs.GetInt("TFG.PerfilVideo", 0), 0, perfilesH264.Length - 1);
        for (int i = 0; i < perfilesH264.Length; i++)
        {
            perfilesH264[i] = SystemInfo.supportsAsyncGPUReadback && CodificadorVideoAndroid.Compatible(false, CodificadorVideoAndroid.Perfiles[i]);
            perfilesHevc[i] = SystemInfo.supportsAsyncGPUReadback && CodificadorVideoAndroid.Compatible(true, CodificadorVideoAndroid.Perfiles[i]);
        }
        if (!perfilesH264[indicePerfil] && !perfilesHevc[indicePerfil])
            for (int i = 0; i < perfilesH264.Length; i++)
                if (perfilesH264[i] || perfilesHevc[i]) { indicePerfil = i; break; }
        ActualizarCapacidades();
        UsarHevc = HevcDisponible && (PlayerPrefs.GetInt("TFG.HEVC", 0) == 1 || !H264Disponible);
    }

    private void ActualizarCapacidades()
    {
        H264Disponible = perfilesH264[indicePerfil];
        HevcDisponible = perfilesHevc[indicePerfil];
        if (UsarHevc && !HevcDisponible) UsarHevc = false;
        if (!UsarHevc && !H264Disponible && HevcDisponible) UsarHevc = true;
    }

    public void AlternarPerfil()
    {
        if (Ocupado) return;
        for (int salto = 1; salto <= perfilesH264.Length; salto++)
        {
            int candidato = (indicePerfil + salto) % perfilesH264.Length;
            if (!perfilesH264[candidato] && !perfilesHevc[candidato]) continue;
            indicePerfil = candidato;
            ActualizarCapacidades();
            PlayerPrefs.SetInt("TFG.PerfilVideo", indicePerfil);
            PlayerPrefs.SetInt("TFG.HEVC", UsarHevc ? 1 : 0);
            PlayerPrefs.Save();
            break;
        }
    }

    public void AlternarCodec()
    {
        if (Ocupado || !H264Disponible || !HevcDisponible) return;
        UsarHevc = !UsarHevc;
        PlayerPrefs.SetInt("TFG.HEVC", UsarHevc ? 1 : 0);
        PlayerPrefs.Save();
    }

    public void IniciarVideo()
    {
        if (!PuedeCapturar || !VideoDisponible || suspendido || !isActiveAndEnabled) return;
        detenerVideo = false;
        descartarVideo = false;
        DuracionGrabacion = 0;
        FramesOmitidos = 0;
        Control.Cambiar(EstadoCaptura.Grabando, "Preparando grabación…");
        IniciarOperacion(GrabarVideoAsync);
    }

    public void DetenerVideo()
    {
        if (Control.Estado == EstadoCaptura.Grabando) detenerVideo = true;
    }

    public void DescartarVideo()
    {
        if (Control.Estado != EstadoCaptura.Grabando) return;
        descartarVideo = true;
        operacion?.Cancel();
    }

    private async Task GrabarVideoAsync(CancellationToken cancelacion)
    {
        var codificador = new CodificadorVideoAndroid();
        using var sesion = new SesionVideo(Path.Combine(Application.persistentDataPath, "stereo_captures"));
        bool hevc = UsarHevc;
        PerfilVideo perfil = Perfil;
        int ancho = perfil.AnchoOjo, alto = perfil.AltoOjo;
        double intervalo = 1.0 / perfil.Fps;
        byte[] miniatura = null;
        int frames = 0;
        long anteriorL = ultimoTiempoL, anteriorR = ultimoTiempoR;
        double inicio = 0, siguiente = 0, ultimoPar = Time.realtimeSinceStartupAsDouble;
        string fecha = DateTime.UtcNow.ToString("O");
        try
        {
            await codificador.AbrirAsync(sesion.RutaVideo, hevc, perfil);
            while (!detenerVideo)
            {
                await EsperarFinalDeFrameAsync(cancelacion);
                cancelacion.ThrowIfCancellationRequested();
                double ahora = Time.realtimeSinceStartupAsDouble;
                if (frames > 0)
                {
                    DuracionGrabacion = ahora - inicio;
                    Control.Cambiar(EstadoCaptura.Grabando, $"Grabando · {DuracionGrabacion:0.0} s · {FramesOmitidos} imágenes omitidas");
                    if (ahora < siguiente) continue;
                }
                if (!proveedor.IntentarObtener(out var par) || !ValidacionParStereo.EsValido(
                    par.TiempoIzquierda, par.TiempoDerecha, anteriorL, anteriorR, toleranciaStereoMs))
                {
                    if (ahora - ultimoPar > tiempoEsperaFoto)
                        throw new TimeoutException("Las cámaras dejaron de entregar imágenes sincronizadas. El vídeo incompleto se ha descartado.");
                    continue;
                }
                anteriorL = par.TiempoIzquierda; anteriorR = par.TiempoDerecha; ultimoPar = ahora;
                if (frames == 0) { inicio = ahora; siguiente = ahora; }
                int omitidos = frames == 0 ? 0 : Math.Max(0, (int)Math.Floor((ahora - siguiente) / intervalo));
                FramesOmitidos += omitidos;
                siguiente += (omitidos + 1) * intervalo;
                long tiempoUs = (long)((ahora - inicio) * 1000000);
                byte[] rgba = await compositor.CapturarRgbaAsync(par, ancho, alto, cancelacion);
                if (miniatura == null) miniatura = CompositorStereo.MiniaturaRgba(rgba, ancho, alto);
                cancelacion.ThrowIfCancellationRequested();
                await codificador.EscribirAsync(rgba, tiempoUs);
                frames++;
            }
            cancelacion.ThrowIfCancellationRequested();
            if (frames == 0) throw new InvalidOperationException("Grabación detenida antes de recibir la primera imagen.");
            DuracionGrabacion = Math.Max(intervalo, Time.realtimeSinceStartupAsDouble - inicio);
            Control.Cambiar(EstadoCaptura.Finalizando, "Finalizando vídeo. Espera antes de salir…");
            await codificador.FinalizarAsync((long)(DuracionGrabacion * 1000000));
        }
        finally { await codificador.CerrarAsync(); }

        var datos = new DatosCaptura
        {
            tipo = "video", formato = "MP4", codec = hevc ? "HEVC" : "H264", fechaUtc = fecha,
            anchoOjo = ancho, altoOjo = alto, duracionSegundos = DuracionGrabacion,
            fpsObjetivo = perfil.Fps, framesGuardados = frames, framesOmitidos = FramesOmitidos,
            tiempoIzquierdaTicks = anteriorL, tiempoDerechaTicks = anteriorR
        };
        cancelacion.ThrowIfCancellationRequested();
        Control.Cambiar(EstadoCaptura.Guardando, "Guardando vídeo…");
        UltimaCaptura = await sesion.PublicarAsync(JsonUtility.ToJson(datos, true), miniatura, cancelacion);
        ultimoTiempoL = anteriorL; ultimoTiempoR = anteriorR;
        ultimaImagenNueva = Time.realtimeSinceStartupAsDouble;
        if (!destruir && !suspendido && isActiveAndEnabled)
            Control.Cambiar(EstadoCaptura.Listo, $"Vídeo guardado · {DuracionGrabacion:0.0} s · {frames} imágenes");
    }

    public void ConfigurarFoto(FormatoFoto formato, int calidad)
    {
        if (Ocupado || !Enum.IsDefined(typeof(FormatoFoto), formato) || calidad < 1 || calidad > 100) return;
        Formato = formato;
        CalidadJpeg = calidad;
        PlayerPrefs.SetInt("TFG.FormatoFoto", (int)Formato);
        PlayerPrefs.SetInt("TFG.CalidadJpeg", CalidadJpeg);
        PlayerPrefs.Save();
    }

    private void OnEnable()
    {
        destruir = false;
        if (Ocupado) reiniciarAlTerminar = true;
        else Inicializar();
    }

    public void Inicializar()
    {
        if (Ocupado || suspendido || !isActiveAndEnabled) return;
        IniciarOperacion(InicializarAsync);
    }

    public void CapturarFoto()
    {
        if (Ocupado || suspendido || !isActiveAndEnabled || !Control.IntentarCapturar()) return;
        IniciarOperacion(CapturarFotoAsync);
    }

    // Adaptador para UnityEvent; todas las excepciones de las tareas quedan observadas aquí.
    private async void IniciarOperacion(Func<CancellationToken, Task> trabajo)
    {
        var actual = new CancellationTokenSource();
        operacion = actual;
        try { await trabajo(actual.Token); }
        catch (OperationCanceledException)
        {
            if (!destruir && !suspendido && isActiveAndEnabled)
            {
                ultimaImagenNueva = Time.realtimeSinceStartupAsDouble;
                Control.Cambiar(descartarVideo ? EstadoCaptura.Listo : EstadoCaptura.Error,
                    descartarVideo ? "Vídeo descartado. Puedes iniciar otra captura." : "Captura cancelada. Pulsa Reintentar.");
            }
        }
        catch (Exception error)
        {
            if (!destruir && !suspendido && isActiveAndEnabled)
            {
                Control.Cambiar(EstadoCaptura.Error, error.Message + " Pulsa Reintentar.");
                Debug.LogException(error, this);
            }
        }
        finally
        {
            operacion = null;
            actual.Dispose();
            descartarVideo = false;
            if (destruir || !isActiveAndEnabled || suspendido) Liberar();
            else if (reiniciarAlTerminar && !suspendido)
            {
                reiniciarAlTerminar = false;
                Inicializar();
            }
        }
    }

    private async Task InicializarAsync(CancellationToken cancelacion)
    {
        Control.Cambiar(EstadoCaptura.Inicializando, "Preparando cámaras…");
        Liberar();
        if (float.IsNaN(tiempoEsperaCamaras) || float.IsInfinity(tiempoEsperaCamaras) || tiempoEsperaCamaras <= 0 ||
            float.IsNaN(tiempoEsperaFoto) || float.IsInfinity(tiempoEsperaFoto) || tiempoEsperaFoto <= 0 ||
            float.IsNaN(toleranciaStereoMs) || float.IsInfinity(toleranciaStereoMs) || toleranciaStereoMs < 0 || toleranciaStereoMs > 50)
            throw new InvalidOperationException("Revisa los tiempos de espera y la tolerancia estéreo en GestorCaptura.");
        proveedor = new ProveedorCamarasMeta(camaraIzquierda, camaraDerecha, simularEnEditor);
        compositor = new CompositorStereo(stereoCompositeMaterial);
        almacen = new AlmacenCapturas(Path.Combine(Application.persistentDataPath, "stereo_captures"));

        if (!EsSimulacion)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!PassthroughCameraAccess.IsSupported)
                throw new NotSupportedException("Este dispositivo o versión de Horizon OS no permite acceder a las cámaras.");
            if (!Permission.HasUserAuthorizedPermission("horizonos.permission.HEADSET_CAMERA"))
            {
                Control.Cambiar(EstadoCaptura.SinPermiso, "Autoriza el acceso a las cámaras en el diálogo del sistema.");
                bool permitido = await SolicitarPermisoAsync(cancelacion);
                if (!permitido)
                {
                    Control.Cambiar(EstadoCaptura.SinPermiso, "Permiso denegado. Pulsa Reintentar; si el sistema no lo solicita, actívalo en Ajustes.");
                    return;
                }
            }
#else
            throw new NotSupportedException("Las cámaras físicas requieren una Quest compatible. Activa Simular en Editor para probar el recorrido.");
#endif
        }

        cancelacion.ThrowIfCancellationRequested();
        Control.Cambiar(EstadoCaptura.Inicializando, "Esperando imagen de ambas cámaras…");
        proveedor.Activar();
        double limite = Time.realtimeSinceStartupAsDouble + tiempoEsperaCamaras;
        while (true)
        {
            cancelacion.ThrowIfCancellationRequested();
            if (proveedor.IntentarObtener(out var par) && ValidacionParStereo.EsValido(
                par.TiempoIzquierda, par.TiempoDerecha, 0, 0, toleranciaStereoMs))
            {
                ultimoTiempoL = par.TiempoIzquierda;
                ultimoTiempoR = par.TiempoDerecha;
                ultimaImagenNueva = Time.realtimeSinceStartupAsDouble;
                Control.Cambiar(EstadoCaptura.Listo, Prefijo + $"Cámaras listas · {par.AnchoOjo} × {par.AltoOjo} por ojo" + avisoInterrupcion);
                avisoInterrupcion = null;
                return;
            }
            if (Time.realtimeSinceStartupAsDouble > limite)
                throw new TimeoutException("No llegaron imágenes sincronizadas de ambas cámaras a tiempo.");
            await Task.Yield();
        }
    }

    private string Prefijo => EsSimulacion ? "SIMULACIÓN · " : "";

    private async Task CapturarFotoAsync(CancellationToken cancelacion)
    {
        long anteriorL = ultimoTiempoL, anteriorR = ultimoTiempoR;
        FormatoFoto formatoSesion = Formato;
        int calidadSesion = CalidadJpeg;
        double limite = Time.realtimeSinceStartupAsDouble + tiempoEsperaFoto;
        ParStereo par;
        while (true)
        {
            // MRUK actualiza en el hilo de render. Esperamos a que termine el frame antes de componer.
            await EsperarFinalDeFrameAsync(cancelacion);
            cancelacion.ThrowIfCancellationRequested();
            if (proveedor.IntentarObtener(out par) && ValidacionParStereo.EsValido(
                par.TiempoIzquierda, par.TiempoDerecha, anteriorL, anteriorR, toleranciaStereoMs)) break;
            if (Time.realtimeSinceStartupAsDouble > limite)
                throw new TimeoutException("No se pudo obtener una imagen nueva y sincronizada de los dos ojos.");
        }

        FotoProcesada foto = await compositor.CapturarAsync(par, formatoSesion, calidadSesion, cancelacion);
        cancelacion.ThrowIfCancellationRequested();
        Control.Cambiar(EstadoCaptura.Guardando, Prefijo + "Guardando fotografía…");
        // Serialización en el hilo principal; el almacén solo recibe bytes y texto.
        string json = JsonUtility.ToJson(new DatosCaptura
        {
            fechaUtc = DateTime.UtcNow.ToString("O"), anchoOjo = par.AnchoOjo, altoOjo = par.AltoOjo,
            tiempoIzquierdaTicks = par.TiempoIzquierda, tiempoDerechaTicks = par.TiempoDerecha,
            simulacion = EsSimulacion, formato = formatoSesion.ToString(), calidadJpeg = calidadSesion
        }, true);
        string ruta = await almacen.GuardarFotoAsync(foto.Imagen, foto.Miniatura, json, formatoSesion, cancelacion);
        UltimaCaptura = ruta;
        ultimoTiempoL = par.TiempoIzquierda;
        ultimoTiempoR = par.TiempoDerecha;
        ultimaImagenNueva = Time.realtimeSinceStartupAsDouble;
        if (!destruir && !suspendido && isActiveAndEnabled)
        {
            Control.Cambiar(EstadoCaptura.Listo, Prefijo + "Foto guardada correctamente · " + formatoSesion + " SBS");
            FotoGuardada?.Invoke(ruta);
            Debug.Log("Captura guardada: " + ruta, this);
        }
    }

    private Task EsperarFinalDeFrameAsync(CancellationToken cancelacion)
    {
        var completado = new TaskCompletionSource<bool>();
        var registro = cancelacion.Register(() => completado.TrySetCanceled());
        StartCoroutine(Esperar());
        return completado.Task;

        IEnumerator Esperar()
        {
            try
            {
                yield return new WaitForEndOfFrame();
                if (cancelacion.IsCancellationRequested) completado.TrySetCanceled();
                else completado.TrySetResult(true);
            }
            finally { registro.Dispose(); }
        }
    }

    private void Update()
    {
        if (Ocupado || Control.Estado != EstadoCaptura.Listo || proveedor == null) return;
        try
        {
            if (proveedor.IntentarObtener(out var par) && ValidacionParStereo.EsValido(
                par.TiempoIzquierda, par.TiempoDerecha, ultimoTiempoL, ultimoTiempoR, toleranciaStereoMs))
            {
                ultimoTiempoL = par.TiempoIzquierda;
                ultimoTiempoR = par.TiempoDerecha;
                ultimaImagenNueva = Time.realtimeSinceStartupAsDouble;
            }
            else if (Time.realtimeSinceStartupAsDouble - ultimaImagenNueva > tiempoEsperaFoto)
                Control.Cambiar(EstadoCaptura.Error, "Las cámaras han dejado de entregar imágenes. Pulsa Reintentar.");
        }
        catch (Exception error) { Control.Cambiar(EstadoCaptura.Error, error.Message + " Pulsa Reintentar."); }
    }

    private void OnApplicationPause(bool pausa)
    {
        // Unity también puede emitir false al arrancar: no duplicar la inicialización ni el permiso.
        if (!pausa && !suspendido && !pausaDelPermiso) return;
        // El diálogo de permisos puede pausar la app. Conservar el callback sin duplicar la petición.
        if (pausa && Control.Estado == EstadoCaptura.SinPermiso && Ocupado)
        {
            pausaDelPermiso = true;
            return;
        }
        if (!pausa && pausaDelPermiso)
        {
            pausaDelPermiso = false;
            return;
        }
        suspendido = pausa;
        if (pausa)
        {
            if (Control.Estado == EstadoCaptura.Grabando || Control.Estado == EstadoCaptura.Finalizando)
                avisoInterrupcion = " · Se descartó el vídeo interrumpido al suspender.";
            operacion?.Cancel();
            Control.Cambiar(EstadoCaptura.Suspendido, "Captura suspendida.");
            if (!Ocupado) Liberar();
        }
        else if (isActiveAndEnabled)
        {
            if (Ocupado) reiniciarAlTerminar = true;
            else Inicializar();
        }
    }

    private void OnDisable()
    {
        reiniciarAlTerminar = false;
        operacion?.Cancel();
        Control.Cambiar(EstadoCaptura.Suspendido, "Captura desactivada.");
        if (!Ocupado) Liberar();
    }

    private void OnDestroy()
    {
        destruir = true;
        operacion?.Cancel();
        if (!Ocupado) Liberar();
    }

    private void Liberar()
    {
        compositor?.Dispose();
        proveedor?.Dispose();
        compositor = null;
        proveedor = null;
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    private static async Task<bool> SolicitarPermisoAsync(CancellationToken cancelacion)
    {
        var resultado = new TaskCompletionSource<bool>();
        var callbacks = new PermissionCallbacks();
        callbacks.PermissionGranted += _ => resultado.TrySetResult(true);
        callbacks.PermissionDenied += _ => resultado.TrySetResult(false);
        using (cancelacion.Register(() => resultado.TrySetCanceled()))
        {
            Permission.RequestUserPermission("horizonos.permission.HEADSET_CAMERA", callbacks);
            return await resultado.Task;
        }
    }
#endif

}
