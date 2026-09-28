# Captura estereoscópica para Meta Quest

Aplicación Unity para capturar el entorno con las cámaras físicas izquierda y derecha, guardar fotos y vídeos SBS (ambas vistas juntas) y consultarlos en una galería con visor por ojo. La raíz es `Assets/Scripts/GestorCaptura.cs`; la escena de entrada es `Assets/Scenes/SampleScene.unity`.

## Probar en el Editor

1. Abrir con Unity **6000.3.11f1** y esperar a que terminen las importaciones.
2. Abrir `SampleScene` y entrar en Play. `Simular en Editor` está activado en el gestor: produce patrones L/R, nunca imágenes físicas.
3. En **Captura**, hacer una foto. En **Ajustes**, alternar PNG/JPEG y calidad. Abrir **Galería** y seleccionar la captura.
4. En pantalla se muestra el ojo izquierdo. La profundidad requiere el visor. La grabación Android y la exportación del sistema aparecen deshabilitadas fuera del dispositivo compatible.

La interfaz se genera al iniciar a partir del Canvas y botón de la escena. Se coloca a 1,4 m frente al usuario. En Quest, apuntar con el controlador y pulsar/soltar el gatillo; se utiliza el derecho si está disponible y, en su defecto, el izquierdo. Sin controlador XR, el módulo habitual conserva la interacción de ratón. Solo hay un emisor de clics XR para evitar capturas dobles.

## Funciones implementadas

- Fotos PNG/JPEG a la resolución efectiva de las cámaras, en disposición SBS izquierda/derecha; calidad JPEG persistente.
- Vista previa del ojo izquierdo para encuadrar, estados y recuperación de errores/permisos.
- Vídeo MP4 sin audio mediante MediaCodec/MediaMuxer. H.264 o HEVC y perfiles 640×480 a 10 FPS, 320×240 a 15 FPS, 640×480 a 15 FPS, por ojo, sujetos a capacidad de codificación/decodificación. FPS objetivo, no garantía de rendimiento.
- Parada con finalización del contenedor; descarte con doble confirmación. Un vídeo interrumpido por suspensión se descarta si aún no se publicó.
- Galería persistente con miniaturas, filtros, páginas, duración y borrado confirmado; visor estéreo y reproducción/pausa/saltos de ±5 segundos.
- Exportación de una copia mediante MediaStore a `Pictures/TFG` o `Movies/TFG`. Eliminar el original local no elimina la copia exportada.

## Cómo está organizado

`GestorCaptura` coordina permisos, estados y una operación de captura a la vez. `ProveedorCamarasMeta` obtiene los dos ojos; `CompositorStereo` congela y compone el par. `AlmacenCapturas` publica fotos y `SesionVideo` publica el vídeo solo después de cerrar el codificador. `CodificadorVideoAndroid` entrega RGBA al plugin Java; `RgbaYuv` convierte el color y respeta los strides que indica Android. `RepositorioCapturas`, `InterfazCaptura` y `ExportadorCapturas` completan la consulta, reproducción y exportación.

La analogía útil es una cocina con una sola bandeja: hasta que el codificador procesa el frame, no entra otro. Si tarda, se omiten intervalos y se conserva el tiempo real, en lugar de acumular imágenes hasta agotar memoria. El archivo permanece en una carpeta temporal hasta estar terminado; solo entonces aparece en la galería.

Las capturas se guardan en `Application.persistentDataPath/stereo_captures/<id>/`. Cada carpeta contiene `foto_sbs.png`, `foto_sbs.jpg` o `video_sbs.mp4`, `miniatura.jpg` y `captura.json`. El catálogo se reconstruye desde esos manifiestos. Los temporales ocultos y las entradas dañadas se ignoran. No se borran automáticamente datos ajenos ni temporales de otra sesión.

## Comprobaciones reproducibles

Desde PowerShell, en la raíz:

```powershell
./Tools/Comprobar-Captura.ps1
./Tools/Comprobar-VideoAndroid.ps1
./Tools/Probar-CompositorUnity.ps1
./Tools/Probar-ProyectoUnity.ps1
```

El proyecto declara 6000.3.11f1. Si esa versión no está instalada, los scripts admiten una ruta explícita: `-UnityEditor` en las pruebas Unity, `-UnityEditorData` en la compilación C# y `-AndroidPlayer` en la prueba Java. Por ejemplo, para la versión 6000.3.23f1 instalada al terminar esta revisión:

```powershell
./Tools/Probar-ProyectoUnity.ps1 -UnityEditor 'C:/Program Files/Unity/Hub/Editor/6000.3.23f1/Editor/Unity.exe'
./Tools/Comprobar-Captura.ps1 -UnityEditorData 'C:/Program Files/Unity/Hub/Editor/6000.3.23f1/Editor/Data'
./Tools/Comprobar-VideoAndroid.ps1 -AndroidPlayer 'C:/Program Files/Unity/Hub/Editor/6000.3.23f1/Editor/Data/PlaybackEngines/AndroidPlayer'
```

Para intentar un APK en la copia temporal, añadir `-CompilarAndroid`; `-Reutilizar` reutiliza la última copia y su caché de importación. Esta comprobación usa API 35 instalada, ARM64 e IL2CPP, y no cambia los ajustes de publicación del proyecto original.

- `Comprobar-Captura`: compila los scripts con las referencias locales de Unity para Editor y ramas Android; ejecuta **32 comprobaciones** de exclusión, timestamps, guardados concurrentes, cancelación y publicación/limpieza de sesiones. Las pruebas de almacenamiento de vídeo usan fixtures; no pretenden decodificar un MP4.
- `Comprobar-VideoAndroid`: compila los plugins Java con el SDK Android instalado y verifica BT.601, orientación, orden horizontal, padding, offsets y planos YUV independientes/intercalados.
- `Probar-CompositorUnity`: ejecuta shaders y readback en la GPU real dentro de un proyecto temporal. Verifica PNG/JPEG, independencia de instantáneas, RGBA para vídeo, miniaturas, selección de ojo, catálogo, corrupción y eliminación.
- `Probar-ProyectoUnity`: importa una copia del proyecto, valida ambos plugins y las referencias de las dos escenas, guarda una foto sintética y genera Captura/Ajustes/Galería/Visor para inspección visual. Las cuatro vistas se han revisado sin recortes ni botones fuera del lienzo. La prueba de distribución usa render 2D; no sustituye la prueba XR/URP. [Resultados y capturas](Documentacion/Validacion/RESULTADOS.md).

Los resultados, imágenes y logs quedan en `.utmp/captura/`. Las pruebas Unity necesitan acceso al servicio local de licencias. Usan copias temporales; no abren ni alteran la escena de trabajo del usuario. El proyecto debe haberse abierto al menos una vez para disponer de las referencias de compilación y paquetes locales.

## Validación en Quest pendiente

No se dispone de una prueba de aceptación completa en Quest ni se ha generado un APK validado en esta entrega. Compilar C# y Java no demuestra que el codificador, JNI, permisos y reproducción funcionen juntos en el dispositivo.

El último intento con Unity 6000.3.23f1 completó la compilación nativa ARM64/IL2CPP, pero el empaquetado Android falló porque varias bibliotecas de Meta XR 83 comparten namespace y AGP 9 exige que sean únicos. `Assets/Editor/CompatibilidadAndroidMeta.cs` añade una excepción de compatibilidad al proyecto Gradle generado únicamente con Meta XR 83 y AGP 9 o superior. No modifica los ajustes de publicación ni los paquetes originales. Esta corrección todavía no se ha recompilado: se detuvieron las pruebas por petición del usuario. Al actualizar Meta XR debe revisarse si sigue siendo necesaria.

1. Instalar el módulo Android de Unity con SDK/NDK/OpenJDK. Elegir Android, ARM64 e IL2CPP; incluir `SampleScene`. Revisar el nivel de SDK requerido por la configuración actual: si no está instalado, instalarlo o seleccionar conscientemente otro compatible. No se han modificado los ajustes de publicación del usuario.
2. Compilar e instalar una versión de desarrollo en Quest 3/3S. Conceder acceso a las cámaras y comprobar orientación con letras distintas a izquierda y derecha. Repetir con permiso denegado y verificar que la galería local sigue accesible.
3. Grabar movimiento y un reloj durante 10 y 60 segundos en cada perfil ofrecido. Detener, reproducir, pausar, avanzar/retroceder y comparar la duración con `captura.json`. Registrar `framesGuardados`, `framesOmitidos`, memoria y temperatura.
4. Repetir tres sesiones; probar parada inmediata, descarte, suspensión y reanudación. Cerrar y abrir diez veces el visor y comprobar que los recursos se estabilizan.
5. Reiniciar la app; consultar, exportar y eliminar fotos/vídeos. Probar la copia en la galería nativa, seleccionando SBS manualmente si el reproductor lo requiere.

La compatibilidad automática con reproductores externos, la comodidad estereoscópica, el rendimiento sostenido y HEVC quedan pendientes de estas pruebas. No se inserta metadato espacial propietario ni se promete formato multivista Apple. La conversión de vídeo usa CPU y readback GPU: sus perfiles deben ajustarse con medidas reales. La grabación exige readback asíncrono disponible.

La configuración actual tiene desactivada la minificación Java. Si se activa en el futuro, conservar `com.tfg.capture.**` en las reglas de ProGuard/R8, porque C# accede por nombre mediante JNI.

El diagnóstico original, los objetivos del Word y la matriz T01–T15 están en [PLAN_IMPLEMENTACION.md](PLAN_IMPLEMENTACION.md).
