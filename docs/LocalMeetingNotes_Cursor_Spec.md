# LocalMeetingNotes — Especificación funcional y técnica completa para Cursor

> **Estado:** Diseño aprobado como baseline.
> **Objetivo:** entregar este archivo directamente a Cursor para que realice el brainstorming final, escriba el plan de implementación y desarrolle la aplicación siguiendo Superpowers.
> **Plataforma objetivo:** Windows 11, notebook corporativa sin GPU dedicada, 16 GB RAM.
> **Stack preferido:** .NET 8 + WPF, NAudio, Whisper.net CPU.
> **Principio rector:** aplicación local, simple, robusta, sin cloud, sin login, sin base de datos y sin dependencias de servicios externos durante runtime.

---

# 0. INSTRUCCIONES OBLIGATORIAS PARA CURSOR / SUPERPOWERS

Antes de escribir código:

1. Invocar y seguir **`superpowers:using-superpowers`**.
2. Invocar y seguir **`superpowers:brainstorming`**.
3. Tratar este documento como **baseline de producto ya discutido con el usuario**, no como una sugerencia vaga.
4. Durante brainstorming:
   - revisar este documento completo;
   - detectar contradicciones, riesgos técnicos o decisiones que necesiten ajuste;
   - no volver a abrir decisiones triviales que ya están definidas;
   - priorizar simplicidad, estabilidad y uso offline;
   - proponer cambios solo si mejoran de forma material confiabilidad, rendimiento o mantenibilidad.
5. **NO escribir implementación todavía** mientras el flujo de brainstorming no haya sido aprobado por el usuario.
6. Una vez aprobado el diseño final:
   - guardar la spec de Superpowers en `docs/superpowers/specs/`;
   - hacer self-review de la spec;
   - pedir aprobación de la spec escrita;
   - luego invocar **`superpowers:writing-plans`**.
7. Para implementación:
   - utilizar TDD donde tenga sentido;
   - usar `superpowers:test-driven-development`;
   - usar `superpowers:verification-before-completion`;
   - ejecutar tests y build reales antes de afirmar que algo funciona.
8. No implementar features futuras solo “porque son fáciles”.
9. No agregar cloud, telemetry, analytics, updater, APIs externas, Hugging Face downloader, OpenAI APIs, Teams APIs ni login.
10. No intentar evadir políticas corporativas o controles de seguridad.

## Regla de prioridad

Si alguna decisión de este documento entra en conflicto con una limitación técnica real descubierta durante implementación:

1. detener implementación de esa parte;
2. documentar el conflicto;
3. proponer la alternativa más simple;
4. pedir aprobación antes de cambiar arquitectura.

---

# 1. NOMBRE DE TRABAJO

Nombre temporal de solución/proyecto:

`LocalMeetingNotes`

El nombre debe ser fácil de cambiar más adelante.

No invertir tiempo de MVP en branding.

---

# 2. PROBLEMA A RESOLVER

El usuario necesita capturar reuniones realizadas principalmente en Microsoft Teams en una notebook corporativa Windows donde:

- no tiene habilitada la grabación/transcripción nativa de Teams;
- servicios como Hugging Face pueden estar interceptados/bloqueados por infraestructura corporativa;
- la notebook no tiene GPU NVIDIA;
- tiene 16 GB de RAM;
- necesita obtener la transcripción completa de sus reuniones;
- luego quiere copiar manualmente esa transcripción a una IA corporativamente permitida, por ejemplo Copilot;
- no necesita inicialmente resumen automático;
- no necesita bot dentro de Teams;
- no necesita integración con calendario;
- no necesita nube;
- no necesita colaboración.

La aplicación debe hacer una sola cosa muy bien:

> **Grabar localmente micrófono + audio que sale por Windows, transcribirlo localmente con Whisper y guardar una nota Markdown reutilizable.**

---

# 3. OBJETIVOS DEL PRODUCTO

## 3.1 Objetivo principal

Permitir que el usuario:

1. abra la app;
2. presione Start;
3. ingrese metadata mínima de la reunión;
4. grabe simultáneamente:
   - su micrófono;
   - el audio del sistema/output donde escucha Teams;
5. presione Stop;
6. deje que la transcripción ocurra en background;
7. reciba como resultado un archivo `.md`;
8. abra, copie o ubique rápidamente esa nota;
9. pueda pegar luego el contenido manualmente en Copilot u otra IA aprobada.

## 3.2 Objetivos secundarios

- minimizar a system tray;
- mostrar claramente si está grabando;
- no perder audio si Whisper falla;
- reintentar transcripciones;
- sobrevivir reinicios/crashes razonablemente;
- permitir nuevas grabaciones mientras hay transcripciones pendientes;
- no requerir DB;
- permitir inspeccionar todo desde carpetas normales;
- funcionar offline en runtime.

---

# 4. NO-OBJETIVOS DEL MVP

NO implementar en MVP:

- resumen automático;
- integración con OpenAI;
- integración con Copilot;
- integración con Teams API;
- bot de Teams;
- captura por aplicación específica;
- login;
- cuentas;
- sincronización;
- cloud storage;
- base de datos;
- embeddings;
- semantic search;
- notebook/RAG;
- diarización real de múltiples participantes;
- pyannote;
- Hugging Face API;
- descarga automática de modelos;
- auto-update;
- telemetry;
- analytics;
- calendario;
- detección automática de meetings;
- edición avanzada de audio;
- waveform visual sofisticado;
- speaker recognition;
- identificación de John/Maria/etc.;
- auto-resumen;
- extracción automática de action items;
- grabación oculta;
- modo stealth.

---

# 5. PRINCIPIOS DE DISEÑO

La aplicación debe priorizar, en este orden:

1. **No perder una reunión.**
2. **Runtime totalmente local.**
3. **Simplicidad.**
4. **Recuperación ante errores.**
5. **Bajo consumo de recursos.**
6. **Transparencia del estado.**
7. **Datos portables y legibles.**
8. **Código mantenible.**

Un error de transcripción nunca debe borrar audio.

La imposibilidad de cargar Whisper nunca debe impedir grabar, siempre que la captura de audio funcione.

---

# 6. STACK TECNOLÓGICO PROPUESTO

## 6.1 Framework

Baseline:

- .NET 8
- `net8.0-windows`
- WPF

Motivo:

- aplicación exclusivamente Windows;
- integración simple con APIs de audio Windows;
- buena compatibilidad con corporate Windows;
- menor complejidad que una UI cross-platform innecesaria.

Si el entorno real ya estandariza una versión LTS de .NET más nueva, Cursor puede proponerla durante brainstorming, pero no cambiar por moda.

## 6.2 UI

WPF + MVVM.

Se puede utilizar `CommunityToolkit.Mvvm` solo si simplifica significativamente ViewModels/commands.

No agregar frameworks UI pesados.

## 6.3 System tray

Preferir `System.Windows.Forms.NotifyIcon` desde WPF usando interoperabilidad WinForms.

Evitar una dependencia NuGet adicional solo para tray si no es necesaria.

## 6.4 Audio

NAudio.

La implementación debe verificar cuál es la versión estable actual al momento de desarrollar.

**No usar APIs que solo existan en una rama unreleased.**

Baseline conocida y estable:

- captura mic: `WasapiCapture`;
- captura system output: `WasapiLoopbackCapture`;
- enumeración: `MMDeviceEnumerator`.

Si una versión estable moderna de NAudio ya incluye una API superior (`WasapiRecorder`) y está publicada oficialmente, Cursor puede proponer usarla, pero la arquitectura debe mantenerse detrás de interfaces propias.

## 6.5 Transcripción

Whisper.net sobre whisper.cpp.

Usar únicamente runtime CPU.

Preferencia conceptual:

- `Whisper.net`
- runtime CPU específico (`Whisper.net.Runtime` o equivalente estable vigente)

NO usar:

- `Whisper.net.AllRuntimes`;
- CUDA;
- Hugging Face downloader;
- `WhisperGgmlDownloader`;
- APIs de red.

La app debe cargar directamente un archivo local con equivalente a:

`WhisperFactory.FromPath(modelPath)`

## 6.6 JSON

`System.Text.Json`.

## 6.7 Logging

Logging local.

No enviar logs a ningún servicio.

---

# 7. REQUERIMIENTO CRÍTICO: OFFLINE / NO NETWORK

En runtime, la aplicación no debe necesitar Internet.

## 7.1 Prohibiciones

No debe existir código de runtime que:

- descargue el modelo;
- se conecte a Hugging Face;
- llame APIs externas;
- compruebe actualizaciones;
- envíe telemetry;
- envíe logs;
- suba audio;
- suba transcripciones;
- haga login.

## 7.2 Comportamiento offline

Con:

- aplicación instalada/publicada;
- modelo local presente;
- dependencias nativas presentes;

debe poder:

1. iniciar;
2. grabar;
3. detener;
4. transcribir;
5. crear Markdown;

con adaptador de red deshabilitado.

Debe existir al menos un test/manual acceptance test específico para esto.

---

# 8. MODELOS WHISPER LOCALES

## 8.1 Modelo inicial incluido por provisioning manual

Modelo inicial esperado:

`ggml-base.bin`

Usar modelo **multilingual `base`**, NO `base.en`, porque las reuniones pueden contener español, inglés y cambios entre ambos idiomas.

La carpeta de modelos por defecto es:

`models\`

Ejemplo:

```text
LocalMeetingNotes/
├─ src/
├─ tests/
├─ models/
│  ├─ ggml-base.bin
│  └─ otros-modelos-opcionales.bin
└─ LocalMeetingNotes.sln
```

El repositorio puede contener el modelo inicial comprimido y dividido en varios archivos pequeños únicamente como mecanismo de transporte/provisioning. El README explicará cómo reconstruir/descomprimir manualmente esos archivos para que el resultado final quede como:

`models\ggml-base.bin`

La aplicación NO debe:
- conocer el formato de esos archivos comprimidos;
- unir chunks;
- descomprimir modelos;
- descargar modelos;
- depender de Git LFS;
- depender de Hugging Face.

La aplicación solo trabaja con archivos `.bin` ya presentes en la carpeta de modelos.

## 8.2 Descubrimiento de modelos

La app debe descubrir dinámicamente modelos disponibles mediante:

`<modelsFolder>\*.bin`

El `modelsFolder` por defecto debe ser:

`${AppBaseDirectory}\models`

pero debe poder configurarse desde Settings y persistirse en `settings.json`.

En Settings, mostrar un selector/dropdown con todos los `.bin` encontrados en esa carpeta.

Cada item debería mostrar al menos:
- filename;
- tamaño aproximado;
- estado válido/no válido si puede comprobarse sin cargar todo el modelo.

Ejemplo:

```text
Whisper model:
[ ggml-base.bin              148 MB ]
  ggml-small.bin             ...
  ggml-medium.bin            ...
```

No hardcodear exclusivamente `ggml-base.bin`.

El modelo seleccionado debe persistirse preferentemente como filename relativo a `modelsFolder`, por ejemplo:

`"selectedModel": "ggml-base.bin"`

Esto permite mover toda la carpeta de la aplicación sin romper un path absoluto.

## 8.3 Modelo seleccionado y snapshot por trabajo

Cuando comienza una transcripción, resolver el modelo seleccionado a un path absoluto y tomar un snapshot para ese job.

Si el usuario:
- cambia Settings;
- edita `settings.json`;
- agrega otro `.bin`;

mientras una transcripción ya está en ejecución, NO cambiar el modelo de esa transcripción a mitad del trabajo.

El cambio aplica al siguiente trabajo que todavía no haya comenzado.

La session/checkpoint debe guardar qué modelo real fue utilizado para poder invalidar checkpoints si luego se solicita Retry con otro modelo.

## 8.4 Validación del modelo

Al validar un `.bin`:

- verificar `File.Exists`;
- verificar tamaño > 0;
- opcionalmente aplicar sanity-check razonable;
- intentar cargarlo con Whisper.net cuando corresponda;
- capturar claramente errores de formato/runtime.

No asumir que todo `.bin` encontrado es un modelo Whisper válido.

Si falla:

> The selected Whisper model could not be loaded. Choose another `.bin` file from Settings. Your recorded audio has been preserved.

## 8.5 Comportamiento si NO existe ningún modelo

Este caso debe estar explícitamente soportado.

La app DEBE permitir grabar aunque no exista ningún `.bin`.

Debe mostrar un warning persistente y claro, por ejemplo en la pantalla principal:

```text
⚠ No Whisper model available.
Recording is enabled, but meetings will remain as audio until a model is added and selected.
```

También mostrar una indicación visible en el diálogo de Start, sin bloquearlo.

Al presionar Stop sin modelo:

1. cerrar y preservar `mic.wav` y `system.wav`;
2. guardar `session.json`;
3. estado -> `WaitingForModel`;
4. NO borrar audio;
5. NO marcar como Failed;
6. mostrar row accionable:

```text
⚠ Payment API — Audio saved, waiting for Whisper model
[Settings] [Transcribe]
```

`Transcribe` queda disabled mientras no haya un modelo válido.

Cuando el usuario:
- descomprime/agrega un `.bin` en la carpeta;
- refresca la lista o el watcher lo detecta;
- selecciona un modelo válido;

el row debe habilitar `Transcribe`/`Retry`.

No arrancar automáticamente una transcripción pesada solo porque apareció un archivo nuevo; requerir acción explícita del usuario para sesiones `WaitingForModel`.

## 8.6 Refresh de modelos

La lista debe poder actualizarse mediante:
- botón `Refresh`;
- al abrir Settings;
- opcionalmente `FileSystemWatcher` sobre `modelsFolder`.

El watcher debe usar debounce y manejar:
- copy incompleto;
- rename;
- delete;
- replacement.

Nunca intentar cargar un `.bin` mientras todavía se está copiando/descomprimiendo si el archivo no está estable.

---

# 9. ARQUITECTURA DE DATOS: SIN BASE DE DATOS

No usar SQLite ni otra DB.

`settings.json` será el único archivo persistente de configuración de la app, pero NO se usará como datastore de meetings.

Fuente de verdad:

- archivos `.md` finalizados;
- carpetas temporales de sesiones en `.processing`.

## 9.1 Carpeta raíz

Default sugerido:

`%USERPROFILE%\Documents\LocalMeetingNotes`

Configurable.

Estructura:

```text
LocalMeetingNotes/
├─ 2026-08-07_0930 - Daily Standup.md
├─ 2026-08-07_1100 - Payment API.md
├─ 2026-08-07_1430 - Sprint Planning.md
│
├─ .processing/
│  ├─ 7c7f.../
│  │  ├─ session.json
│  │  ├─ mic.wav
│  │  └─ system.wav
│  └─ ...
│
└─ .logs/
   └─ app-2026-08-07.log
```

## 9.2 No sidecar permanente requerido

Una meeting completada idealmente queda representada por UN solo `.md`.

`session.json` existe solo mientras se graba, espera, transcribe, falla o fue interrumpida.

Cuando finaliza correctamente:

1. generar `.md` atómicamente;
2. verificar que existe y se puede leer;
3. solo entonces borrar audio;
4. borrar carpeta `.processing/<id>`.

---

# 10. SESSION.JSON

Modelo conceptual:

```json
{
  "sessionId": "guid",
  "title": "Payment API",
  "participants": ["John", "Maria"],
  "context": "Review new payment endpoint.",
  "references": ["https://..."],
  "startedAt": "2026-08-07T11:00:00-03:00",
  "stoppedAt": null,
  "status": "Recording",
  "micDeviceId": "...",
  "micDeviceName": "...",
  "systemDeviceId": "...",
  "systemDeviceName": "...",
  "micCaptureStartedOffsetMs": 0,
  "systemCaptureStartedOffsetMs": 42,
  "modelPath": "C:\\...\\models\\ggml-base.bin",
  "modelFileName": "ggml-base.bin",
  "language": "auto",
  "error": null,
  "retryCount": 0
}
```

Persistir cada transición importante de estado, no cada segmento Whisper.

---

# 11. ESTADOS DE UNA SESSION

Definir enum explícito:

```text
Draft
Recording
Stopping
Queued
WaitingForModel
TranscribingMic
TranscribingSystem
Merging
WritingNote
Completed
Failed
Interrupted
```

`Completed`:
- existe `.md`;
- audio temporal eliminado si setting ON;
- processing folder eliminado.

`Failed`:
- audio conservado;
- session.json conservado;
- error visible;
- Retry disponible.

`Interrupted`:
- app cerró/reinició durante una etapa no completada;
- startup debe recuperar.

---

# 12. INICIO DE GRABACIÓN — UX

Botón principal:

`START RECORDING`

Al presionar, abrir modal pequeño.

### Title
- obligatorio;
- prefill `Meeting yyyy-MM-dd HH-mm`;
- focus inicial;
- Enter puede iniciar.

### Participants
- opcional;
- input simple, coma o una línea por persona.

### Context / Notes
- opcional;
- multiline.

### Reference
- opcional;
- URL, ticket ID, Jira, Confluence, etc.;
- puede aceptar múltiples líneas sin validación estricta.

Botones:
- `Start Recording`
- `Cancel`

Flujo rápido: title ya prellenado -> Enter -> recording.

---

# 13. VALIDACIONES ANTES DE START

Comprobar:

- carpeta de destino escribible;
- mic disponible;
- system output disponible;
- espacio de disco razonable;
- modelo Whisper si está disponible.

## Modelo faltante

NO bloquea recording.

Mensaje:

> Whisper model is not available. Recording will be saved and queued for transcription when a model is configured.

## Un input falla

Ejemplo:

> Microphone capture could not start. Continue with system audio only?

Opciones:
- Continue
- Cancel

Nunca empezar silenciosamente con una fuente faltante.

---

# 14. CAPTURA DE AUDIO — REQUERIMIENTO CENTRAL

Grabar DOS streams independientes:

1. microphone;
2. system/output loopback.

NO mezclar antes de Whisper.

Objetivo:

- mic -> `You`;
- system -> `Remote`.

Esto elimina diarización del MVP.

## Micrófono

Default recomendado: default communications capture device, con posibilidad de selección explícita.

## System audio

Capturar render endpoint seleccionado vía WASAPI loopback.

Esto significa todo el audio reproducido por ese output device.

No intentar capturar exclusivamente Teams.

Debe funcionar también con Zoom, Chrome, Meet, Webex, etc.

## Device selection

Settings:
- Microphone device.
- System output device.

Guardar device ID y friendly name.

No cambiar automáticamente de endpoint a mitad de recording.

Si Windows cambia default device mientras graba:
- advertir;
- seguir con endpoint original.

---

# 15. FORMATO DE AUDIO

Objetivo temporal:

- PCM WAV;
- 16 kHz;
- mono;
- 16 bit;

para cada track siempre que sea técnicamente estable.

Resultado:

```text
mic.wav
system.wav
```

No usar FFmpeg.

Conversión/resampling:
- con NAudio;
- o solicitando formato adecuado a WASAPI si está soportado correctamente.

Cursor debe probar qué es más estable:
A. capture directo 16k mono PCM16;
B. capture nativo + resampling/downmix streaming antes de escribir.

Evitar guardar horas de raw 48k stereo float y convertir al final.

---

# 16. SINCRONIZACIÓN MIC + SYSTEM

Ambos archivos representan la MISMA línea temporal.

Usar un monotonic `Stopwatch` común desde inicio.

Guardar offset real de inicio de cada capture.

Ejemplo:

```text
sessionStart: 0 ms
mic capture start: +10 ms
system capture start: +38 ms
```

Whisper devuelve timestamps relativos al archivo.

Normalizar:

`absoluteSegmentStart = captureStartOffset + whisperSegmentStart`

y usar eso para merge.

---

# 17. SILENCIO DE WASAPI LOOPBACK — CRÍTICO

Con loopback, cuando no se reproduce audio, `DataAvailable` puede no dispararse.

Eso puede comprimir la duración de `system.wav` y romper timestamps.

La implementación DEBE preservar timeline real.

## Estrategia preferida A

Mientras se graba:
- reproducir stream de silencio real al mismo render endpoint;
- sample value 0;
- inaudible;
- mantener paquetes loopback fluyendo.

Debe ser mínimo y disposable.

## Alternativa B

Detectar gaps con monotonic clock e insertar samples 0 equivalentes.

No derivar timeline solo desde cantidad de bytes capturados.

## Test obligatorio

1. 10 s con system audio.
2. 30 s de silencio.
3. 10 s system audio.
4. Stop.
5. El segundo bloque debe quedar alrededor de 00:40, no 00:10.

---

# 18. AUDIO METERS

Durante recording:

```text
Mic     █████░░░
System  ████░░░░
```

No waveform.

Actualizar UI ~5-10 Hz.

Usar peak/RMS simple.

Objetivo: detectar instantáneamente que mic y Teams realmente entran.

Si una fuente nunca registra señal durante varios segundos, mostrar warning visual pero no auto-stop.

---

# 19. MAIN WINDOW

Concepto:

```text
┌──────────────────────────────────────────────┐
│ Local Meeting Notes                    [⚙]  │
│                                              │
│          ● START RECORDING                   │
│                                              │
│ Status: Ready                                │
├──────────────────────────────────────────────┤
│ Recent / Queue                               │
│ 🔵 Payment API      Transcribing system 62%  │
│ 🟣 Sprint Planning  Queued                   │
│ ✅ Daily Standup    Completed                │
│ ❗ Architecture    Failed       [Retry]      │
└──────────────────────────────────────────────┘
```

Durante recording:

```text
🔴 RECORDING                      00:43:12

Mic     ██████░░
System  █████░░░

                 STOP
```

Start/Stop siempre muy visibles.

---

# 20. SYSTEM TRAY

## Double click
Abrir/restaurar/enfocar main.

## Right click

```text
Start Recording
Stop Recording
Open
Open Meetings Folder
Settings
Exit
```

## Icon status
- Green: Ready.
- Red: Recording.
- Blue: Transcribing/queue active.
- Orange: Recording + transcription work.
- Amber/Yellow: actionable error.
- Gray: initialization/no devices si hace falta.

Tooltip con estado y meeting.

## X de ventana
Ocultar a tray, no matar app.

---

# 21. STOP RECORDING

Secuencia:

1. state -> `Stopping`;
2. bloquear doble Stop;
3. detener mic;
4. detener loopback;
5. flush/cerrar WAV writers;
6. verificar WAVs;
7. guardar `stoppedAt`;
8. state -> `Queued`;
9. enqueue;
10. UI vuelve a permitir otra recording.

Stop NO espera Whisper.

---

# 22. TRANSCRIPTION QUEUE

Worker single-consumer.

**Máximo una session transcribiéndose a la vez.**

Dentro de session:
- mic y system secuenciales por defecto;
- no ejecutar dos Whisper en paralelo.

Ejemplo:

```text
Session A:
  mic -> Whisper
  system -> Whisper
  merge
  markdown

Session B:
  ...
```

Nueva recording permitida mientras hay queue.

CPU:
- modelo base;
- CPU runtime;
- default conservador de threads, sugerencia inicial 4;
- verificar API exacta vigente de Whisper.net.

No consumir todos los logical processors sin necesidad.

---

# 23. WHISPER ENGINE

Crear abstracción propia:

```csharp
public interface ITranscriptionEngine
{
    Task<TrackTranscript> TranscribeAsync(
        string wavPath,
        TranscriptionOptions options,
        IProgress<TranscriptionProgress>? progress,
        CancellationToken cancellationToken);
}
```

ViewModels no conocen Whisper.net.

## Lazy load
Cargar modelo recién al transcribir por primera vez.

Mantener factory/model mientras sea conveniente.

## Language
- Auto default
- English
- Spanish

Interno:
- `auto`
- `en`
- `es`

## Segments

```csharp
public sealed record TranscriptSegment(
    TimeSpan Start,
    TimeSpan End,
    string Text);
```

Trim, ignorar vacíos, no resumir ni corregir con IA.

---

# 24. TRACK VACÍO / SILENCIO

Antes de Whisper:
- analizar PCM;
- detectar si no hay señal significativa;
- si está prácticamente vacío, no transcribir;
- devolver transcript vacío.

No agregar Silero/pyannote/VAD externo en MVP.

Usar RMS/peak simple y conservador.

---

# 25. PROGRESO

Whisper segments permiten estimar progreso por `segment.End`.

Ejemplo:

`Transcribing microphone — 12:34 / 47:22`

Overall:

`totalWork = micDuration + systemDuration`

`completedWork = processedMic + processedSystem`

No persistir cada segmento a disco.

---

# 26. CHECKPOINTS DE TRANSCRIPCIÓN

Recomendado para evitar repetir CPU tras fallos.

Mientras procesa:

```text
.processing/<session>/
  session.json
  mic.wav
  system.wav
  mic.transcript.json
  system.transcript.json
```

Transcript JSON:

```json
{
  "track": "Mic",
  "audioDurationMs": 123456,
  "modelFileName": "ggml-base.bin",
  "language": "auto",
  "segments": [
    {
      "startMs": 1000,
      "endMs": 4300,
      "text": "..."
    }
  ]
}
```

Flujo:
1. transcribe mic;
2. guardar checkpoint atómico;
3. transcribe system;
4. guardar checkpoint;
5. merge;
6. Markdown.

Retry reutiliza checkpoint válido.

Si cambió modelo o language, invalidar checkpoints correspondientes.

---

# 27. MERGE

Input:
- mic -> You
- system -> Remote

Normalizar timestamps con capture offsets.

Luego:
1. tag speaker;
2. concatenar;
3. ordenar por Start;
4. orden estable en empate;
5. agrupar segmentos consecutivos mismo speaker si gap <= ~2 s y no hubo otro speaker.

Overlap:
- conservar ambos;
- ordenar por Start;
- no diarizar.

---

# 28. RESULTADO MARKDOWN

Filename:

`yyyy-MM-dd_HHmm - <SanitizedTitle>.md`

Ejemplo:

`2026-08-07_1100 - Payment API.md`

Sanitizar caracteres inválidos Windows.

No truncar title dentro del archivo.

No sobrescribir; usar sufijo o session-id corto.

Template:

```markdown
# Payment API

**Date:** 2026-08-07
**Started:** 11:00:14
**Ended:** 11:47:52
**Duration:** 00:47:38
**Participants:** John, Maria
**Reference:** https://...
**Whisper model:** ggml-base.bin
**Language:** auto

## Context

Review the new payment endpoint and clarify error-handling behavior.

---

## Transcript

[00:00:04] **You:** So my question about the endpoint is...

[00:00:09] **Remote:** Currently we're returning...

[00:00:21] **You:** Perfect. And what happens when...
```

UTF-8.

---

# 29. ESCRITURA ATÓMICA

1. generar contenido;
2. escribir `.md.tmp`;
3. flush;
4. cerrar;
5. rename/move a `.md`;
6. verificar;
7. recién entonces cleanup.

Si write falla:
- Failed;
- audio se conserva.

---

# 30. BORRADO DEL AUDIO

Setting:

`Delete audio after successful transcription`

Default ON.

Solo borrar si:
- tracks procesados o vacíos;
- merge correcto;
- Markdown escrito y verificado.

Cualquier error:
- NO borrar.

---

# 31. RECOVERY AL STARTUP

Escanear `.processing/*/session.json`.

Casos:

### Queued / WaitingForModel / Failed
Mostrar/reencolar.

### Transcribing*
Proceso anterior murió.
Marcar Interrupted y reintentar desde checkpoint o inicio.

### Recording / Stopping
Crash durante capture.
Conservar WAV.
Validar y ofrecer:

`Try to transcribe recovered audio`

No prometer recuperación perfecta si WAV quedó corrupto.

---

# 32. EXIT

## Si Recording
Dialog:

```text
A recording is currently active.

[Stop and Save]
[Cancel]
```

No descartar silenciosamente.

## Si Transcribing
Cancelar de forma segura:
- preservar audio/checkpoints;
- marcar Interrupted;
- recuperar próximo startup.

---

# 33. HISTORY / RECENT

No DB.

Scan:
- root `*.md`;
- `.processing`.

Orden desc por fecha.

Completed:
- double click -> abrir `.md`;
- Open;
- Open Folder;
- Copy Full Note.

Failed:
- Retry;
- Open processing folder;
- Show error.

Queued:
- mostrar queue position.

---

# 34. COPY

## Copy Full Note
Leer `.md` entero -> clipboard.

## Copy Transcript
Phase 1 si parser simple; si complica, Phase 2.

## Copy for AI
Phase 2.

No IA integrada.

---

# 35. SETTINGS

Settings es una sección de primera clase del MVP.

`settings.json` es el **único estado de configuración persistente administrado por la app**.

No es una base de datos de meetings:
- meetings completadas siguen siendo `.md`;
- trabajos pendientes siguen siendo `.processing`;
- no usar SQLite.

## General

- Meetings folder.
- Start minimized.
- Close to tray.
- Delete audio after success.

## Audio

Debe permitir elegir explícitamente:

### Microphone / Input

Dropdown:
- `Windows Default / Communications Device`;
- lista de capture devices disponibles.

Persistir device ID, no solo nombre.

### System Audio / Output

Dropdown:
- `Windows Default Output`;
- lista de render/output devices disponibles.

Ejemplos:
- speakers de notebook;
- USB headset;
- Bluetooth headset;
- docking station;
- monitor HDMI.

Persistir device ID.

Esto es importante porque Windows puede cambiar defaults al conectar/desconectar headset.

### Comportamiento de defaults

Si el setting es `default`, resolver el default real al comenzar cada nueva recording.

Si el usuario seleccionó un device específico:
- usar ese device mientras exista;
- si desaparece, mostrar warning;
- no cambiar silenciosamente a otro input/output.

Cambiar Settings mientras una recording ya está activa NO debe cambiar los devices de esa recording.

El nuevo valor aplica a la siguiente recording.

## Whisper

- Models folder.
- Refresh models.
- dropdown con `*.bin` descubiertos;
- selected model persistido;
- validation;
- Auto / English / Spanish;
- threads si se justifica.

Mostrar algo similar a:

```text
Models folder:
C:\...\LocalMeetingNotes\models
[Browse]

Model:
[ggml-base.bin]
[Refresh]

Status:
✓ Local model available
```

Si no hay modelos:

```text
⚠ No .bin models found.
Recordings will be preserved as audio and can be transcribed later.
```

NO Download button.

NO Hugging Face.

## Advanced / Configuration

Agregar botón:

`Open settings.json`

Debe abrir el archivo de configuración con la aplicación default de Windows.

Opcionalmente:

`Open configuration folder`

El JSON debe ser humano-legible e intencionalmente editable.

## Startup

Start with Windows -> Phase 2 si agrega fricción.

---

# 36. STARTUP STATUS

Checks no bloqueantes:

```text
✓ Meetings folder writable
✓ Microphone found
✓ System output found
✓ Whisper runtime available
✓ 1 Whisper model found: ggml-base.bin
```

Si no existe modelo:

```text
⚠ No Whisper model available
Recording is still available.
Audio will be preserved and can be transcribed later.
```

El warning debe permanecer visible en main window hasta resolverlo, no aparecer una sola vez y desaparecer.

No wizard compleja.

---

# 37. ERRORES

Mensajes accionables.

Ejemplo:

```text
Transcription failed.

Model:
C:\...\models\ggml-base.bin

Error:
Unable to load Whisper runtime.

Your audio was NOT deleted.

[Retry]
[Open Audio Folder]
[Open Log]
```

Categorías:
- ModelMissing
- ModelInvalid
- NativeRuntimeLoadFailure
- MicrophoneUnavailable
- SystemAudioUnavailable
- RecordingStartFailure
- RecordingStopFailure
- AudioFileInvalid
- TranscriptionFailure
- OutputFolderPermissionDenied
- MarkdownWriteFailure
- DiskSpaceLow
- Unknown

---

# 38. LOGGING

Archivo local diario.

NO loguear transcript completo por defecto.

Log:
- startup;
- devices;
- start/stop;
- model load;
- job states;
- durations;
- errors;
- cleanup.

---

# 39. SINGLE INSTANCE

Usar named mutex.

Evitar dos instancias capturando/procesando.

Ideal:
- segunda instancia signal primera para mostrar window;
- segunda termina.

Si signaling complica MVP:
- mensaje + exit.

---

# 40. ASYNC / THREADING

UI nunca bloqueada.

Separar:
- UI;
- audio callbacks;
- transcription worker;
- filesystem.

Usar:
- Task;
- CancellationToken;
- `System.Threading.Channels` o mecanismo single-consumer equivalente.

No polling con sleeps innecesarios.

---

# 41. SERVICIOS PROPUESTOS

```csharp
public interface IAudioCaptureService
{
    Task<RecordingSessionHandle> StartAsync(
        RecordingRequest request,
        CancellationToken ct);

    Task<RecordingResult> StopAsync(
        CancellationToken ct);
}
```

```csharp
public interface IAudioDeviceService
{
    IReadOnlyList<AudioDeviceInfo> GetMicrophones();
    IReadOnlyList<AudioDeviceInfo> GetRenderDevices();
    AudioDeviceInfo? GetDefaultMicrophone();
    AudioDeviceInfo? GetDefaultRenderDevice();
}
```

```csharp
public interface ITranscriptionEngine
{
    Task<TrackTranscript> TranscribeAsync(...);
}
```

```csharp
public interface ITranscriptionQueue
{
    void Enqueue(Guid sessionId);
    Task RetryAsync(Guid sessionId);
}
```

```csharp
public interface ISessionRepository
{
    Task<MeetingSession?> LoadAsync(Guid id);
    Task SaveAsync(MeetingSession session);
    Task<IReadOnlyList<MeetingSession>> LoadProcessingAsync();
}
```

```csharp
public interface IMeetingNoteWriter
{
    Task<string> WriteAsync(
        MeetingSession session,
        MergedTranscript transcript,
        CancellationToken ct);
}
```

```csharp
public interface IModelCatalog
{
    IReadOnlyList<WhisperModelInfo> Discover(string modelsFolder);
    ModelValidationResult Validate(string path);
    string? ResolveSelectedModel(AppSettings settings);
}
```

```csharp
public interface IRecoveryService
{
    Task<RecoveryResult> RecoverAsync(CancellationToken ct);
}
```

---

# 42. ENTIDADES

## MeetingMetadata
- Title
- Participants[]
- Context
- References[]

## MeetingSession
- SessionId
- Metadata
- StartedAt
- StoppedAt
- Status
- MicDevice
- SystemDevice
- MicPath
- SystemPath
- CaptureOffsets
- Language
- ModelPath
- Error
- RetryCount

## TranscriptSegment
- Start
- End
- Text
- Speaker (`You`, `Remote`)

---

# 43. ESTRUCTURA DE SOLUCIÓN

```text
src/
  LocalMeetingNotes.App/
    App.xaml
    Views/
    ViewModels/
    Tray/
    Bootstrap/

  LocalMeetingNotes.Core/
    Models/
    Interfaces/
    Services/
    Transcription/
    Audio/
    Files/
    Recovery/

tests/
  LocalMeetingNotes.Core.Tests/
  LocalMeetingNotes.IntegrationTests/

docs/
  superpowers/
    specs/
    plans/

models/
  ggml-base.bin
```

Si múltiples projects no aportan valor, Cursor puede proponer simplificar. YAGNI.

---

# 44. VIEWMODELS

## MainViewModel
- global state;
- Start/Stop;
- recent list;
- Settings;
- meters.

## StartRecordingViewModel
- metadata;
- validation;
- start command.

## SettingsViewModel
- meetings/models paths;
- device selection;
- discovered models;
- selected model;
- model validation;
- live settings reload state.

## SessionRowViewModel
- title;
- status;
- progress;
- error;
- commands.

---

# 45. PERFORMANCE TARGET

Target real:
- Windows 11;
- Intel laptop CPU;
- 16 GB RAM;
- sin NVIDIA GPU.

Objetivos:
- Start responde rápido;
- Stop no espera transcription;
- capture ligera;
- Whisper puede ir más lento que realtime;
- UI permanece responsive;
- una sola transcription simultánea;
- modelo base.

---

# 46. HEADPHONES / DUPLICACIÓN

Con auriculares:
- system -> Remote;
- mic -> You;
- buena separación.

Con speakers:
- mic puede volver a captar Remote acústicamente.

No implementar echo cancellation en MVP.

Documentar:

> Headphones are recommended for best separation between You and Remote.

---

# 47. PRIVACIDAD / TRANSPARENCIA

No grabación oculta.

Recording:
- tray rojo;
- main rojo;
- timer visible.

Solo comienza por acción explícita del usuario.

No detección automática de Teams.

No bypass de políticas corporativas.

---

# 48. UNIT TESTS OBLIGATORIOS

### FilenameSanitizer
- caracteres inválidos;
- vacío;
- largo;
- Unicode;
- duplicate.

### MarkdownWriter
- metadata completa/vacía;
- UTF-8;
- You/Remote;
- timestamps;
- atomic behavior.

### TranscriptMerger
- mic antes;
- remote antes;
- overlap;
- same timestamp;
- offsets;
- grouping;
- empty track.

### ModelValidator
- missing;
- zero;
- plausible/invalid `.bin`;
- selected filename not present;
- model folder changed;
- model removed after selection.

### SessionStateMachine
- transiciones.

### Queue
- single consumer;
- retry;
- failure no bloquea siguiente;
- cancellation.

### Recovery
- Failed;
- Queued;
- interrupted transcription;
- interrupted recording;
- missing audio.

### AudioActivityAnalyzer
- silence;
- low noise;
- synthetic activity.

---

# 49. INTEGRATION TESTS

CI no debe requerir hardware real ni descargar modelos.

Abstraer NAudio.

Test local opcional con modelo real mediante config/env var.

Debe existir prueba manual offline.

---

# 50. MANUAL HARDWARE TEST MATRIX

## 1 — Mic only
Hablar 30 s, system silent, validar You.

## 2 — System only
Mic mute, reproducir audio, validar Remote.

## 3 — Both
Alternar speakers, validar orden.

## 4 — Silence gap
10 s system, 30 s silence, 10 s system. Segundo bloque ~00:40.

## 5 — 30+ min
Estabilidad/memory/Stop.

## 6 — No model available
Mover/remover todos los `.bin` -> warning visible -> grabar -> Stop -> `WaitingForModel` -> audio preservado -> agregar/descomprimir modelo -> Refresh -> seleccionar -> Transcribe -> completed.

## 7 — Whisper failure
Modelo inválido -> audio conservado.

## 8 — Exit during transcription
Reabrir -> recover.

## 9 — Window X
Oculta a tray.

## 10 — Tray
Start/Stop/status.

## 11 — Devices
Headset/speakers. Seleccionar input/output explícitos, reiniciar app y verificar persistencia.

## 12 — Runtime settings reload
Editar `settings.json` con app abierta. Verificar reload, invalid JSON temporal y que recording activa no cambie de devices.

## 13 — Multiple local models
Agregar dos `.bin`, Refresh, verificar dropdown, selección y persistencia.

## 14 — Offline
Network disabled -> full flow funciona.

---

# 51. ACCEPTANCE CRITERIA MVP

MVP aceptado solo si:

1. inicia Windows 11;
2. no login;
3. runtime sin Internet;
4. modelo desde local path;
5. mic capture;
6. system capture;
7. tracks separados;
8. Start/Stop main;
9. Stop no espera Whisper;
10. queue single-worker;
11. CPU Whisper;
12. base model;
13. nueva recording con queue pendiente;
14. failure -> audio no borrado;
15. Retry;
16. success -> Markdown;
17. success -> audio cleanup si ON;
18. metadata en Markdown;
19. You/Remote merge timestamps;
20. history filesystem;
21. double click abre note;
22. Open Folder;
23. tray;
24. tray status color;
25. X -> tray;
26. recovery;
27. models list se descubre desde `modelsFolder\*.bin`;
28. selected model se persiste en `settings.json`;
29. audio input/output seleccionables y persistentes;
30. `settings.json` puede editarse en runtime sin romper operaciones activas;
31. sin modelo se puede grabar y queda `WaitingForModel` con audio preservado;
32. no Hugging Face downloader;
33. no telemetry;
34. offline manual test pasa.

---

# 52. ORDEN RECOMENDADO DE IMPLEMENTACIÓN

Después de Superpowers plan:

1. solution skeleton;
2. domain models + state machine;
3. filesystem repository;
4. Settings persistence + runtime reload;
5. local model catalog/discovery;
6. device enumeration + selection persistence;
7. mic capture spike;
8. loopback spike;
9. timeline/silence solution;
10. recording coordinator;
11. WAV validation;
12. Whisper local spike;
13. transcription service;
14. checkpoints;
15. merge;
16. Markdown;
17. queue;
18. recovery;
19. main UI;
20. Start dialog;
21. meters;
22. tray;
23. errors/retry/WaitingForModel;
24. copy/open;
25. logging;
26. tests;
27. offline acceptance;
28. publish;

No empezar por UI bonita antes de validar audio + Whisper.

---

# 53. PHASE 2 — NO IMPLEMENTAR AHORA

- global hotkey Start/Stop;
- Copy Transcript si no entró MVP;
- Copy for AI;
- inline transcript viewer;
- editar metadata;
- editar transcript;
- search;
- tags;
- Start with Windows;
- pause/limit transcription during recording;
- per-meeting language;
- richer diagnostics;
- audio test screen;
- retention;
- small model;
- optional keep audio.

---

# 54. FUTURO LEJANO

Solo si aparece necesidad real:

- diarization;
- speaker names;
- local LLM;
- Ollama;
- summaries;
- action items;
- embeddings;
- RAG;
- NotebookLM-like DB;
- Copilot automation;
- Teams metadata;
- calendar.

No diseñar infraestructura hoy para esto.

---

# 55. PUBLISH / DISTRIBUCIÓN

Development:

`dotnet run`

Portable Windows build inicial:

```bash
dotnet publish -c Release -r win-x64 --self-contained true
```

Inicialmente:
- trimming OFF;
- single-file OFF hasta validar native Whisper runtime;
- publish folder normal.

Crear `publish.ps1` que:
1. clean;
2. restore;
3. test;
4. publish.

No publicar si tests fallan.

---

# 56. DEPENDENCIAS NATIVAS

Whisper.net CPU puede requerir runtime nativo/Visual C++ Redistributable según versión.

Cursor debe:
- revisar docs exactas de versión;
- validar Windows 11;
- error amigable ante DLL load failure;
- documentar prerequisitos.

No descargar/installar redistributables automáticamente.

---

# 57. SETTINGS.JSON

Este archivo es el **único store persistente de configuración de la aplicación**.

No guardar en él:
- history de meetings;
- transcripts;
- queue completa;
- metadata de meetings.

Eso vive en `.md` y `.processing`.

Guardar config en:

`%LOCALAPPDATA%\LocalMeetingNotes\settings.json`

Ejemplo:

```json
{
  "meetingsFolder": "C:\\Users\\...\\Documents\\LocalMeetingNotes",
  "modelsFolder": "C:\\Tools\\LocalMeetingNotes\\models",
  "selectedModel": "ggml-base.bin",
  "language": "auto",

  "microphone": {
    "mode": "defaultCommunications",
    "deviceId": null
  },

  "systemOutput": {
    "mode": "default",
    "deviceId": null
  },

  "deleteAudioAfterSuccess": true,
  "startMinimized": false,
  "closeToTray": true,
  "transcriptionThreads": 4
}
```

## 57.1 Diseño del archivo

- JSON human-readable;
- indentado;
- nombres claros;
- valores desconocidos deben tratarse con tolerancia razonable;
- agregar version field solo si realmente se necesita migración.

## 57.2 Escritura

Cuando la UI modifica Settings:
- validar;
- escribir a `.tmp`;
- flush;
- atomic replace;
- actualizar estado in-memory.

## 57.3 Edición manual mientras la app está abierta

El archivo debe poder editarse manualmente en runtime.

Implementar `FileSystemWatcher` o mecanismo equivalente sobre `settings.json`.

Requisitos:
- debounce ~300-1000 ms;
- esperar que el editor termine el replace/write;
- parsear a objeto temporal;
- validar;
- aplicar solo si es válido.

Si JSON queda temporalmente inválido mientras el editor escribe:
- no reemplazar settings actuales;
- esperar siguiente evento;
- no crash.

Si permanece inválido:
- mostrar warning;
- continuar con last-known-good settings.

## 57.4 Snapshot y seguridad de cambios

Los settings no deben mutar operaciones activas.

### Recording activa
Cambiar mic/output en JSON:
- no cambia capture actual;
- aplica a próxima recording.

### Transcription activa
Cambiar selected model/language/threads:
- no cambia job actual;
- aplica al siguiente job.

### Meetings folder
Si cambia mientras hay recording:
- session actual conserva su root original;
- nuevas sessions usan nueva carpeta.

## 57.5 Cambios externos reflejados en UI

Si settings se recarga correctamente:
- actualizar controles;
- refrescar devices si corresponde;
- refrescar models folder/list si cambió;
- mostrar indicación no intrusiva opcional:
  `Settings reloaded`.

## 57.6 Corrupción al startup

Si JSON está corrupto:
- conservar copia backup;
- usar defaults o last-known-good si existe;
- warning visible;
- no crash.

---

# 58. DISK SPACE

Antes de Start:
- consultar free space;
- si extremadamente bajo, warning fuerte o bloquear.

Umbral inicial razonable configurable, por ejemplo 500 MB, ajustado al formato real.

---

# 59. TIMER

Recording timer con monotonic Stopwatch.

`HH:mm:ss`.

Guardar timestamps reales con `DateTimeOffset`.

---

# 60. APP GLOBAL STATUS

Separar estado de session de estado global.

Global:

```text
Ready
Recording
Transcribing
RecordingAndTranscribing
Error
```

Tray deriva de:
- active recording;
- active worker;
- failed count.

---

# 61. UX DE QUEUE

```text
🔴 Payment API — Recording 00:21:15
🟣 Architecture — Queued (2)
🔵 Daily — System audio 68%
✅ Planning — Completed
❗ Demo — Transcription failed [Retry]
```

No depender solo de color.

---

# 62. ACCESSIBILITY

- tab order;
- Enter start;
- Escape cancel;
- keyboard buttons;
- texto + icono, no solo color;
- tooltips.

---

# 63. EDGE CASES

Definir comportamiento para:

- title inválido/repetido;
- mic unplug;
- headset unplug;
- output disappears;
- Stop doble;
- Start doble;
- disk full;
- folder borrado;
- model borrado;
- model cambiado durante queue;
- Markdown locked;
- clipboard failure;
- corrupt session.json;
- corrupt checkpoint;
- zero-length WAV;
- system silence;
- mic silence;
- meeting muy corta;
- meeting >1h;
- sleep;
- lock screen;
- cancellation.

No todos requieren solución compleja, pero ninguno debe causar cleanup destructivo.

---

# 64. SLEEP / SUSPEND

Si Windows duerme:
- registrar;
- al resume verificar capture;
- si device murió, warning/error.

No prevenir sleep por fuerza en MVP.

---

# 65. REGLA DE CLEANUP

Nunca borrar destructivamente sin éxito confirmado.

- capture falla -> conservar;
- normalize falla -> conservar;
- Whisper falla -> conservar;
- merge falla -> conservar;
- Markdown falla -> conservar.

Cleanup es el último paso.

---

# 66. DEFINITION OF DONE POR FEATURE

No basta compilar.

Según feature:
- unit test;
- integración/manual;
- error handling;
- cancellation;
- dispose;
- logging;
- UI responsive.

Audio, Whisper, queue y recovery requieren verificación real.

---

# 67. FORMA DE TRABAJO DE CURSOR

Después del brainstorming aprobado:

1. escribir plan detallado;
2. tareas pequeñas;
3. cada tarea con:
   - archivos;
   - test;
   - implementación;
   - comando de verificación;
4. implementar incrementalmente;
5. validar audio temprano;
6. no hacer big-bang UI;
7. commits pequeños;
8. al final:
   - build;
   - tests;
   - publish;
   - manual critical checklist.

---

# 68. DECISIONES YA TOMADAS

No reabrir sin razón técnica:

- Windows only.
- .NET.
- desktop.
- local/offline.
- no DB.
- Markdown.
- mic + system separados.
- You / Remote.
- no diarization.
- no cloud.
- no model download.
- local models descubiertos desde `modelsFolder\*.bin`.
- default esperado `ggml-base.bin`, pero soportar otros `.bin`.
- model selection persistida en `settings.json`.
- audio input/output seleccionables y persistidos por device ID.
- `settings.json` editable en runtime con safe reload.
- sin modelo, recording sigue disponible y queda audio pendiente.
- system tray.
- Start/Stop visibles.
- transcription queue.
- una transcripción a la vez.
- Retry preserva audio.
- delete audio solo success.
- filesystem source of truth.
- no Teams integration.
- no IA dentro de app MVP.

---

# 69. COSAS QUE CURSOR DEBE VALIDAR EN BRAINSTORMING

1. versión/API estable actual de NAudio;
2. estrategia timeline loopback silence;
3. streaming 16k mono sin raw gigantes;
4. versión estable Whisper.net;
5. CPU runtime exacto;
6. cancellation actual;
7. threads conservadores;
8. prerequisitos Windows nativos;
9. WPF + NotifyIcon;
10. atomic Windows file operations;
11. single-instance;
12. publish/native DLL layout;
13. comportamiento real de device IDs de NAudio al conectar/desconectar headsets;
14. FileSystemWatcher/debounce seguro para `settings.json` y `modelsFolder`;
15. estrategia de model discovery sin intentar cargar archivos todavía incompletos.

---

# 70. REFERENCIAS TÉCNICAS

NAudio  
https://github.com/naudio/NAudio

WASAPI loopback  
https://github.com/naudio/NAudio/blob/main/Docs/WasapiLoopbackCapture.md

Whisper.net  
https://github.com/sandrohanea/whisper.net

whisper.cpp  
https://github.com/ggml-org/whisper.cpp

Whisper models  
https://github.com/ggml-org/whisper.cpp/tree/master/models

---

# 71. EJEMPLO END-TO-END

Usuario abre app.

Tray:
`Green — Ready`

Start.

Metadata:

```text
Title: Payment API
Participants: John, Maria
Context: Need to understand error handling.
Reference: PROJ-1234
```

Recording:

```text
🔴 Payment API
00:35:17

Mic     █████░░░
System  ████░░░░
```

Stop.

Queue:

```text
Payment API
Transcribing microphone
08:41 / 35:17
```

Luego system.

Mientras tanto puede grabarse otra meeting.

Success:

```text
✅ Payment API
Completed
```

Filesystem:

`2026-08-07_1233 - Payment API.md`

Audio borrado tras éxito si setting ON.

Double click abre Markdown.

`Copy Full Note`.

Abrir Copilot -> paste -> preguntar.

Ese es el producto.

---

# 72. ESCENARIO DE FALLO

Modelo movido.

UI:

```text
❗ Payment API
Model not found.

Audio has been preserved.

[Locate Model]
[Retry]
[Open Folder]
```

Processing conserva:
- session.json;
- mic.wav;
- system.wav;
- checkpoints disponibles.

Configurar model -> Retry -> success -> Markdown -> cleanup.

---

# 73. ESCENARIO CORPORATIVO OFFLINE

La app:
- no conoce Zscaler;
- no se conecta;
- no usa certificados web;
- no usa Hugging Face;
- no usa Python;
- no usa FFmpeg;
- no usa Docker.

Runtime necesita:
- binarios locales;
- native runtime empaquetado;
- modelo local;
- devices de audio;
- carpeta escribible.

---

# 74. ÚLTIMA INSTRUCCIÓN A CURSOR

No conviertas esto en un “Granola clone”.

El valor es que sea:

- pequeño;
- offline;
- predecible;
- transparente;
- fácil de auditar;
- fácil de reparar;
- sin servicios.

Si una feature no ayuda directamente a:

> **Record -> Transcribe -> Save -> Copy**

probablemente no pertenece al MVP.

**Primero ejecutá Superpowers brainstorming usando esta spec como baseline.**

Presentá el diseño final y tradeoffs al usuario.

**NO escribas código hasta obtener aprobación.**

Luego usá Superpowers `writing-plans` y recién después implementá.
