using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using TFG.Captura;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR;
using UnityEngine.Video;

/// <summary>Captura, ajustes y galería local. La navegación no exige permiso de cámara.</summary>
public sealed class InterfazCaptura : MonoBehaviour
{
    [SerializeField] private GestorCaptura gestor;
    [SerializeField] private Button boton;
    [SerializeField] private TMP_Text textoBoton;
    [SerializeField] private Canvas lienzo;
    [SerializeField] private Material materialVisor;
    private TMP_Text estado, detalle, paginaTexto;
    private RectTransform cursor, contenido;
    private OVRCameraRig rig;
    private RepositorioCapturas repositorio;
    private readonly List<Button> botones = new List<Button>();
    private readonly List<Texture2D> texturas = new List<Texture2D>();
    private readonly List<GameObject> objetos = new List<GameObject>();
    private List<DatosCaptura> capturas = new List<DatosCaptura>();
    private List<DatosCaptura> todasLasCapturas = new List<DatosCaptura>();
    private string filtro = "Todo";
    private DatosCaptura seleccionada;
    private Material materialInstancia;
    private string vista = "Captura", mensaje;
    private int pagina, versionVista;
    private bool cargando, gatilloAnterior, confirmarBorrado;
    private Button encimaAnterior, pulsado;
    private UnityEngine.XR.InputDevice dispositivoAnterior;
    private UnityEngine.InputSystem.UI.InputSystemUIInputModule moduloEntrada;
    private bool moduloEstabaHabilitado;
    private VideoPlayer reproductor;
    private RenderTexture videoTextura;
    private TMP_Text tiempoVideo;
    private RawImage previa;
    private bool confirmarDescarte;
    private bool GrabacionEnCurso => gestor && (gestor.Control.Estado == EstadoCaptura.Grabando || gestor.Control.Estado == EstadoCaptura.Finalizando);

    private void Start()
    {
        if (!gestor || !boton || !textoBoton || !lienzo || !materialVisor)
        {
            Debug.LogError("Faltan referencias de InterfazCaptura en la escena.", this);
            enabled = false;
            return;
        }
        rig = FindFirstObjectByType<OVRCameraRig>();
        if (EventSystem.current)
        {
            moduloEntrada = EventSystem.current.GetComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            moduloEstabaHabilitado = moduloEntrada && moduloEntrada.enabled;
        }
        repositorio = new RepositorioCapturas(Path.Combine(Application.persistentDataPath, "stereo_captures"));
        materialInstancia = new Material(materialVisor);
        var rect = (RectTransform)lienzo.transform;
        rect.sizeDelta = new Vector2(650, 540);
        var panel = (RectTransform)boton.transform.parent;
        panel.sizeDelta = rect.sizeDelta;
        Image fondo = panel.GetComponent<Image>();
        if (fondo) { fondo.enabled = true; fondo.color = new Color(0.035f, 0.055f, 0.075f, 0.97f); fondo.raycastTarget = false; }
        boton.gameObject.SetActive(false); // Plantilla conservada; cada botón nuevo tiene su propio evento.
        textoBoton.raycastTarget = false;
        CrearTexto(panel, "CAPTURA / 3D", new Vector2(0, 224), new Vector2(570, 40), 30);
        estado = CrearTexto(panel, "", new Vector2(0, 172), new Vector2(570, 65), 21);
        CrearBoton(panel, "Captura", new Vector2(-200, 110), new Vector2(180, 48), () => Mostrar("Captura"));
        CrearBoton(panel, "Galería", new Vector2(0, 110), new Vector2(180, 48), MostrarGaleria);
        CrearBoton(panel, "Ajustes", new Vector2(200, 110), new Vector2(180, 48), () => Mostrar("Ajustes"));
        contenido = CrearRect("Contenido", panel, new Vector2(0, -78), new Vector2(590, 325));

        cursor = CrearRect("Puntero", rect, Vector2.zero, new Vector2(10, 10));
        var marca = cursor.gameObject.AddComponent<Image>();
        marca.color = new Color(0.3f, 1f, 0.75f);
        marca.raycastTarget = false;
        cursor.gameObject.SetActive(false);
        var ancla = rig ? rig.centerEyeAnchor : Camera.main ? Camera.main.transform : null;
        if (ancla)
        {
            lienzo.transform.SetPositionAndRotation(ancla.position + ancla.forward * 1.4f, ancla.rotation);
            lienzo.transform.localScale = Vector3.one * 0.002f;
            Camera camaraUI = ancla.GetComponent<Camera>();
            if (camaraUI) lienzo.worldCamera = camaraUI;
            // La escena antigua ocultaba esta capa al render normal y usaba OVROverlayCanvas.
            if (rig)
                foreach (var camara in rig.GetComponentsInChildren<Camera>(true))
                    camara.cullingMask |= 1 << lienzo.gameObject.layer;
            else if (camaraUI) camaraUI.cullingMask |= 1 << lienzo.gameObject.layer;
        }
        Mostrar("Captura");
    }

    public void AccionPrincipal()
    {
        if (gestor && gestor.Control.Estado == EstadoCaptura.Grabando)
        {
            if (confirmarDescarte) gestor.DescartarVideo();
            else confirmarDescarte = true;
            return;
        }
        if (!gestor || gestor.Ocupado) return;
        if (gestor.Control.Estado == EstadoCaptura.Listo) gestor.CapturarFoto();
        else if (gestor.Control.Estado == EstadoCaptura.Error || gestor.Control.Estado == EstadoCaptura.SinPermiso)
            gestor.Inicializar();
    }

    private void LimpiarContenido()
    {
        LiberarVideo();
        versionVista++;
        foreach (Transform hijo in contenido) { hijo.gameObject.SetActive(false); Destroy(hijo.gameObject); }
        botones.RemoveAll(b => !b || b.transform.IsChildOf(contenido));
        objetos.RemoveAll(obj => !obj || obj.transform != contenido && obj.transform.IsChildOf(contenido));
        foreach (var textura in texturas) if (textura) Destroy(textura);
        texturas.Clear();
        detalle = paginaTexto = null;
        confirmarBorrado = false;
        confirmarDescarte = false;
        previa = null;
    }

    private void Mostrar(string nuevaVista)
    {
        if (cargando || GrabacionEnCurso && nuevaVista != "Captura") return;
        LimpiarContenido();
        vista = nuevaVista;
        mensaje = null;
        seleccionada = null;
        if (vista == "Captura")
        {
            previa = CrearRect("Vista previa", contenido, new Vector2(-170, 62), new Vector2(224, 168)).gameObject.AddComponent<RawImage>();
            previa.raycastTarget = false;
            detalle = CrearTexto(contenido, "", new Vector2(125, 70), new Vector2(295, 160), 21);
            CrearBoton(contenido, "Capturar foto", new Vector2(0, -54), new Vector2(420, 58), AccionPrincipal);
            CrearBoton(contenido, "Grabar vídeo", new Vector2(0, -120), new Vector2(420, 58), () =>
            {
                if (gestor.Control.Estado == EstadoCaptura.Grabando) gestor.DetenerVideo();
                else gestor.IniciarVideo();
            });
        }
        else if (vista == "Ajustes")
        {
            CrearBoton(contenido, "Foto: " + gestor.Formato, new Vector2(0, 126), new Vector2(500, 50), () =>
            {
                gestor.ConfigurarFoto(gestor.Formato == FormatoFoto.PNG ? FormatoFoto.JPEG : FormatoFoto.PNG, gestor.CalidadJpeg);
                Mostrar("Ajustes");
            });
            CrearBoton(contenido, "Calidad JPEG: " + gestor.CalidadJpeg, new Vector2(0, 64), new Vector2(500, 50), () =>
            {
                gestor.ConfigurarFoto(gestor.Formato, gestor.CalidadJpeg < 90 ? 90 : gestor.CalidadJpeg < 100 ? 100 : 75);
                Mostrar("Ajustes");
            });
            CrearBoton(contenido, "Vídeo: " + (gestor.VideoDisponible ? "MP4 / " + (gestor.UsarHevc ? "HEVC" : "H.264") : "requiere Quest compatible"),
                new Vector2(0, 2), new Vector2(520, 50), () => { gestor.AlternarCodec(); Mostrar("Ajustes"); });
            CrearBoton(contenido, "Perfil: " + gestor.Perfil, new Vector2(0, -60), new Vector2(550, 50),
                () => { gestor.AlternarPerfil(); Mostrar("Ajustes"); });
            CrearTexto(contenido, "Se ofrecen perfiles admitidos por el dispositivo.\nFPS objetivo; vídeo sin audio. Los ajustes se conservan.",
                new Vector2(0, -130), new Vector2(560, 65), 19);
        }
    }

    private void MostrarGaleria()
    {
        if (cargando || GrabacionEnCurso) return;
        LimpiarContenido();
        vista = "Galería";
        seleccionada = null;
        EjecutarUI(async () =>
        {
            todasLasCapturas = await repositorio.ListarAsync();
            AplicarFiltro();
            if (!this || !isActiveAndEnabled) return;
            pagina = Mathf.Clamp(pagina, 0, Math.Max(0, (capturas.Count - 1) / 3));
            await DibujarPaginaAsync();
        });
    }

    private async Task DibujarPaginaAsync()
    {
        LimpiarContenido();
        mensaje = capturas.Count == 0 ? "Todavía no hay capturas. Puedes hacer la primera en Captura." : "Fotos y vídeos guardados en este dispositivo";
        int version = versionVista;
        CrearBoton(contenido, "Mostrar: " + filtro, new Vector2(0, 147), new Vector2(550, 28), () =>
        {
            filtro = filtro == "Todo" ? "Fotos" : filtro == "Fotos" ? "Vídeos" : "Todo";
            pagina = 0;
            AplicarFiltro();
            EjecutarUI(DibujarPaginaAsync);
        }).GetComponentInChildren<TMP_Text>().fontSize = 19;
        for (int fila = 0; fila < 3; fila++)
        {
            int indice = pagina * 3 + fila;
            if (indice >= capturas.Count) break;
            DatosCaptura dato = capturas[indice];
            string fecha = DateTime.TryParse(dato.fechaUtc, out DateTime instante) ? instante.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss") : "Sin fecha";
            var entrada = CrearBoton(contenido, fecha + "  ·  " + dato.formato +
                (dato.tipo == "video" ? $" · {dato.duracionSegundos:0} s" : "") + (dato.simulacion ? "\nSimulación" : ""),
                new Vector2(0, 85 - fila * 83), new Vector2(550, 70), () => Abrir(dato));
            var texto = entrada.GetComponentInChildren<TMP_Text>();
            texto.margin = new Vector4(115, 0, 5, 0);
            if (File.Exists(dato.miniatura))
            {
                byte[] bytes;
                try { bytes = await Task.Run(() => File.ReadAllBytes(dato.miniatura)); }
                catch (IOException) { continue; }
                catch (UnauthorizedAccessException) { continue; }
                if (!this || !isActiveAndEnabled || version != versionVista) return;
                var miniatura = new Texture2D(2, 2, TextureFormat.RGB24, false);
                if (!miniatura.LoadImage(bytes)) { Destroy(miniatura); continue; }
                texturas.Add(miniatura);
                var imagen = CrearRect("Miniatura", entrada.transform, new Vector2(-210, 0), new Vector2(90, 60)).gameObject.AddComponent<RawImage>();
                imagen.texture = miniatura;
                imagen.raycastTarget = false;
            }
        }
        if (!this || !isActiveAndEnabled || version != versionVista) return;
        CrearBoton(contenido, "Anterior", new Vector2(-200, -145), new Vector2(175, 45), () => CambiarPagina(-1));
        CrearBoton(contenido, "Siguiente", new Vector2(200, -145), new Vector2(175, 45), () => CambiarPagina(1));
        paginaTexto = CrearTexto(contenido, (pagina + 1) + " / " + Math.Max(1, (capturas.Count + 2) / 3),
            new Vector2(0, -145), new Vector2(180, 42), 23);
    }

    private void CambiarPagina(int incremento)
    {
        if (cargando) return;
        pagina = Mathf.Clamp(pagina + incremento, 0, Math.Max(0, (capturas.Count - 1) / 3));
        EjecutarUI(DibujarPaginaAsync);
    }

    private void AplicarFiltro()
    {
        capturas = todasLasCapturas.FindAll(c => filtro == "Todo" || (filtro == "Fotos" ? c.tipo == "foto" : c.tipo == "video"));
    }

    private void Abrir(DatosCaptura captura)
    {
        if (cargando) return;
        LimpiarContenido();
        vista = "Visor";
        seleccionada = captura;
        int version = versionVista;
        EjecutarUI(async () =>
        {
            bool video = captura.tipo == "video";
            Texture textura;
            if (video)
            {
                await PrepararVideoAsync(captura, version);
                if (!this || !isActiveAndEnabled || version != versionVista) return;
                textura = videoTextura;
            }
            else
            {
                byte[] bytes = await Task.Run(() => File.ReadAllBytes(captura.ruta));
                if (!this || !isActiveAndEnabled || version != versionVista) return;
                var foto = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!foto.LoadImage(bytes)) { Destroy(foto); throw new IOException("La imagen no se puede abrir."); }
                if (foto.width != captura.anchoOjo * 2 || foto.height != captura.altoOjo)
                { Destroy(foto); throw new IOException("El tamaño de imagen no coincide con su descripción."); }
                texturas.Add(foto);
                textura = foto;
            }
            float alto = Math.Min(video ? 170 : 235, 520f * captura.altoOjo / captura.anchoOjo);
            float ancho = alto * captura.anchoOjo / captura.altoOjo;
            var imagen = CrearRect("Vista por ojo", contenido, new Vector2(0, video ? 60 : 30), new Vector2(ancho, alto)).gameObject.AddComponent<RawImage>();
            imagen.texture = textura;
            imagen.material = materialInstancia;
            imagen.maskable = false;
            imagen.raycastTarget = false;
            mensaje = XRSettings.isDeviceActive ? "Vista estereoscópica · cada ojo recibe su imagen" : "Vista izquierda en pantalla · la profundidad requiere el visor";
            if (video)
            {
                tiempoVideo = CrearTexto(contenido, "", new Vector2(0, -38), new Vector2(400, 28), 18);
                CrearBoton(contenido, "−5 s", new Vector2(-190, -82), new Vector2(145, 42), () => BuscarVideo(-5));
                CrearBoton(contenido, "Pausa", new Vector2(0, -82), new Vector2(190, 42), () =>
                {
                    if (!reproductor || !reproductor.isPrepared) return;
                    if (reproductor.isPlaying) reproductor.Pause(); else reproductor.Play();
                });
                CrearBoton(contenido, "+5 s", new Vector2(190, -82), new Vector2(145, 42), () => BuscarVideo(5));
            }
            CrearBoton(contenido, "Volver", new Vector2(-210, -140), new Vector2(150, 48), MostrarGaleria);
            if (ExportadorCapturas.Disponible)
                CrearBoton(contenido, "Exportar", new Vector2(0, -140), new Vector2(170, 48), () => EjecutarUI(async () =>
                {
                    string uri = await ExportadorCapturas.ExportarAsync(captura);
                    if (this && isActiveAndEnabled) mensaje = "Copia exportada a " + (video ? "Movies/TFG" : "Pictures/TFG") + ". Selecciona SBS en el reproductor si es necesario.";
                    Debug.Log("Exportación guardada: " + uri);
                }));
            CrearBoton(contenido, "Eliminar", new Vector2(210, -140), new Vector2(150, 48), PedirBorrado);
        });
    }

    private async Task PrepararVideoAsync(DatosCaptura captura, int version)
    {
        reproductor = gameObject.AddComponent<VideoPlayer>();
        reproductor.playOnAwake = false;
        reproductor.audioOutputMode = VideoAudioOutputMode.None;
        reproductor.renderMode = VideoRenderMode.RenderTexture;
        reproductor.source = VideoSource.Url;
        reproductor.url = new Uri(captura.ruta).AbsoluteUri;
        reproductor.isLooping = true;
        videoTextura = new RenderTexture(captura.anchoOjo * 2, captura.altoOjo, 0);
        videoTextura.Create();
        reproductor.targetTexture = videoTextura;
        string errorPreparacion = null;
        reproductor.errorReceived += (_, error) => { errorPreparacion = error; mensaje = "No se puede reproducir el vídeo: " + error; };
        reproductor.Prepare();
        double limite = Time.realtimeSinceStartupAsDouble + 15;
        try
        {
            while (reproductor && !reproductor.isPrepared)
            {
                if (!this || !isActiveAndEnabled || version != versionVista) return;
                if (errorPreparacion != null) throw new IOException(errorPreparacion);
                if (Time.realtimeSinceStartupAsDouble > limite) throw new TimeoutException("El vídeo no se pudo preparar a tiempo.");
                await Task.Yield();
            }
            if (reproductor && version == versionVista) reproductor.Play();
        }
        catch { LiberarVideo(); throw; }
    }

    private void BuscarVideo(double segundos)
    {
        if (reproductor && reproductor.isPrepared && reproductor.canSetTime)
            reproductor.time = Math.Max(0, Math.Min(reproductor.length - 0.1, reproductor.time + segundos));
    }

    private void LiberarVideo()
    {
        if (reproductor) { reproductor.Stop(); reproductor.targetTexture = null; Destroy(reproductor); reproductor = null; }
        if (videoTextura) { videoTextura.Release(); Destroy(videoTextura); videoTextura = null; }
        tiempoVideo = null;
    }

    private void PedirBorrado()
    {
        if (seleccionada == null || cargando) return;
        if (!confirmarBorrado)
        {
            confirmarBorrado = true;
            mensaje = "¿Eliminar esta captura? Pulsa Eliminar otra vez para confirmar o Volver para cancelar.";
            foreach (var b in botones)
                if (b && b.name == "Eliminar") b.GetComponentInChildren<TMP_Text>().text = "Confirmar";
            return;
        }
        DatosCaptura borrar = seleccionada;
        LiberarVideo(); // Soltar el archivo antes de eliminarlo.
        EjecutarUI(async () =>
        {
            await repositorio.EliminarAsync(borrar);
            if (!this || !isActiveAndEnabled) return;
            seleccionada = null;
            todasLasCapturas = await repositorio.ListarAsync();
            AplicarFiltro();
            if (!this || !isActiveAndEnabled) return;
            vista = "Galería";
            pagina = Mathf.Clamp(pagina, 0, Math.Max(0, (capturas.Count - 1) / 3));
            await DibujarPaginaAsync();
        });
    }

    private async void EjecutarUI(Func<Task> accion)
    {
        if (cargando) return;
        cargando = true;
        mensaje = "Cargando…";
        try { await accion(); }
        catch (Exception error)
        {
            if (this && isActiveAndEnabled)
            {
                mensaje = "No se pudo completar la operación: " + error.Message;
                Debug.LogWarning(error.Message, this);
            }
        }
        finally { if (this) cargando = false; }
    }

    private void Update()
    {
        if (!estado || !gestor) return;
        estado.text = vista == "Captura" ? gestor.Control.Mensaje : mensaje ?? "Ajustes de captura";
        foreach (var b in botones) if (b) b.interactable = !cargando;
        if (GrabacionEnCurso)
            foreach (var b in botones) if (b && !b.transform.IsChildOf(contenido)) b.interactable = false;
        if (vista == "Captura")
        {
            bool grabando = gestor.Control.Estado == EstadoCaptura.Grabando;
            if (!grabando) confirmarDescarte = false;
            if (previa)
            {
                previa.texture = gestor.VistaPrevia;
                previa.color = previa.texture ? Color.white : Color.clear;
            }
            if (detalle) detalle.text = confirmarDescarte ? "Pulsa Confirmar descarte para eliminar esta grabación. Puedes detener y guardar para conservarla." :
                "Vista previa: ojo izquierdo\n" + gestor.Formato + " · 3D SBS\n" +
                (gestor.EsSimulacion ? "SIMULACIÓN\nVídeo en Quest" : "MP4 · " + (gestor.UsarHevc ? "HEVC" : "H.264"));
            var principal = botones.Find(b => b && b.transform.IsChildOf(contenido));
            if (principal)
            {
                bool reintentar = gestor.Control.Estado == EstadoCaptura.Error || gestor.Control.Estado == EstadoCaptura.SinPermiso;
                principal.interactable = !cargando && (grabando || !gestor.Ocupado && (gestor.PuedeCapturar || reintentar));
                principal.GetComponentInChildren<TMP_Text>().text = grabando ? (confirmarDescarte ? "Confirmar descarte" : "Descartar vídeo") : reintentar ? "Reintentar" : "Capturar foto";
            }
            var grabar = botones.Find(b => b && b.name == "Grabar vídeo");
            if (grabar)
            {
                grabar.interactable = !cargando && (grabando || gestor.PuedeCapturar && gestor.VideoDisponible);
                grabar.GetComponentInChildren<TMP_Text>().text = grabando ? "Detener y guardar" : gestor.VideoDisponible ? "Grabar vídeo" : "Vídeo requiere Quest compatible";
            }
        }
        if (tiempoVideo && reproductor && reproductor.isPrepared)
        {
            tiempoVideo.text = $"{reproductor.time:0.0} / {reproductor.length:0.0} s";
            var pausa = botones.Find(b => b && b.name == "Pausa");
            if (pausa) pausa.GetComponentInChildren<TMP_Text>().text = reproductor.isPlaying ? "Pausa" : "Reproducir";
        }
        if (vista == "Ajustes" && gestor.Ocupado)
            foreach (var b in botones) if (b && b.transform.IsChildOf(contenido)) b.interactable = false;
        if (vista == "Ajustes")
            foreach (var b in botones)
            {
                if (!b) continue;
                if (b.name.StartsWith("Vídeo:")) b.interactable &= gestor.H264Disponible && gestor.HevcDisponible;
                if (b.name.StartsWith("Perfil:")) b.interactable &= gestor.VideoDisponible;
            }
        ActualizarPuntero();
    }

    private RectTransform CrearRect(string nombre, Transform padre, Vector2 posicion, Vector2 tamano)
    {
        var go = new GameObject(nombre, typeof(RectTransform));
        objetos.Add(go);
        go.layer = lienzo.gameObject.layer;
        var rect = (RectTransform)go.transform;
        rect.SetParent(padre, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = posicion;
        rect.sizeDelta = tamano;
        return rect;
    }

    private TMP_Text CrearTexto(Transform padre, string texto, Vector2 posicion, Vector2 tamano, float fuente)
    {
        var rect = CrearRect("Texto", padre, posicion, tamano);
        var nuevo = rect.gameObject.AddComponent<TextMeshProUGUI>();
        nuevo.font = textoBoton.font;
        nuevo.text = texto;
        nuevo.fontSize = fuente;
        nuevo.color = Color.white;
        nuevo.raycastTarget = false;
        nuevo.alignment = TextAlignmentOptions.Center;
        nuevo.textWrappingMode = TextWrappingModes.Normal;
        return nuevo;
    }

    private Button CrearBoton(Transform padre, string etiqueta, Vector2 posicion, Vector2 tamano, Action accion)
    {
        var nuevo = Instantiate(boton, padre);
        objetos.Add(nuevo.gameObject);
        nuevo.gameObject.SetActive(true);
        nuevo.name = etiqueta;
        var rect = (RectTransform)nuevo.transform;
        rect.anchoredPosition = posicion;
        rect.sizeDelta = tamano;
        nuevo.onClick = new Button.ButtonClickedEvent();
        nuevo.onClick.AddListener(() => accion());
        TMP_Text texto = nuevo.GetComponentInChildren<TMP_Text>();
        texto.text = etiqueta;
        texto.fontSize = 23;
        texto.color = new Color(0.04f, 0.09f, 0.13f);
        texto.raycastTarget = false;
        botones.Add(nuevo);
        return nuevo;
    }

    private void ActualizarPuntero()
    {
        if (!rig || !EventSystem.current) return;
        var dispositivo = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        Transform ancla = rig.rightControllerAnchor;
        if (!dispositivo.isValid || !dispositivo.TryGetFeatureValue(CommonUsages.isTracked, out bool seguido) || !seguido)
        {
            dispositivo = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            ancla = rig.leftControllerAnchor;
        }
        bool valido = dispositivo.isValid && ancla && dispositivo.TryGetFeatureValue(CommonUsages.isTracked, out bool tracking) && tracking;
        // Un único emisor de clics XR. El módulo habitual sigue manejando el ratón cuando no hay controlador.
        if (moduloEntrada) moduloEntrada.enabled = !valido && moduloEstabaHabilitado;
        bool gatillo = valido && dispositivo.TryGetFeatureValue(CommonUsages.triggerButton, out bool valor) && valor;
        if (dispositivo != dispositivoAnterior) { pulsado = null; gatilloAnterior = gatillo; }
        dispositivoAnterior = dispositivo;
        Button encima = null;
        cursor.gameObject.SetActive(false);
        if (valido)
        {
            var plano = new Plane(lienzo.transform.forward, lienzo.transform.position);
            var rayo = new Ray(ancla.position, ancla.forward);
            if (plano.Raycast(rayo, out float distancia) && distancia < 5)
            {
                Vector3 impacto = rayo.GetPoint(distancia);
                var rectLienzo = (RectTransform)lienzo.transform;
                Vector3 local = rectLienzo.InverseTransformPoint(impacto);
                if (rectLienzo.rect.Contains(local))
                {
                    cursor.gameObject.SetActive(true);
                    cursor.localPosition = new Vector3(local.x, local.y, -0.01f);
                    foreach (var candidato in botones)
                    {
                        if (!candidato || !candidato.gameObject.activeInHierarchy) continue;
                        var rect = (RectTransform)candidato.transform;
                        if (rect.rect.Contains(rect.InverseTransformPoint(impacto))) encima = candidato;
                    }
                }
            }
        }
        var evento = new PointerEventData(EventSystem.current);
        if (encima != encimaAnterior)
        {
            if (encimaAnterior) encimaAnterior.OnPointerExit(evento);
            if (encima) encima.OnPointerEnter(evento);
        }
        if (gatillo && !gatilloAnterior)
        {
            pulsado = encima && encima.interactable ? encima : null;
            if (pulsado) pulsado.OnPointerDown(evento);
        }
        if (!gatillo && gatilloAnterior)
        {
            if (pulsado) pulsado.OnPointerUp(evento);
            if (valido && pulsado && pulsado == encima && pulsado.interactable) pulsado.onClick.Invoke();
            pulsado = null;
        }
        gatilloAnterior = gatillo;
        encimaAnterior = encima;
    }

    private void OnDisable()
    {
        LiberarVideo();
        versionVista++;
        if (moduloEntrada) moduloEntrada.enabled = moduloEstabaHabilitado;
        if (cursor) cursor.gameObject.SetActive(false);
        pulsado = null;
    }
    private void OnDestroy()
    {
        LiberarVideo();
        foreach (var textura in texturas) if (textura) Destroy(textura);
        foreach (var objeto in objetos) if (objeto) Destroy(objeto);
        if (materialInstancia) Destroy(materialInstancia);
    }
}
