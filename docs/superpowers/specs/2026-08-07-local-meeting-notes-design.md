# LocalMeetingNotes — Design Spec

> **Status:** Approved for planning (brainstorming complete).  
> **Date:** 2026-08-07  
> **Baseline:** `docs/LocalMeetingNotes_Cursor_Spec.md`  
> **Product one-liner:** Record mic + system audio locally → transcribe with local Whisper CPU → save one Markdown note → copy into an approved AI tool.

---

## 1. Goal and non-goals

### Goal

A Windows-only desktop app that:

1. Records microphone and system/loopback audio as separate tracks.
2. Transcribes them offline with Whisper (CPU).
3. Merges segments as **You** / **Remote**.
4. Writes a single reusable `.md` note.
5. Never requires network, login, cloud, or a database at runtime.

### Non-goals (MVP)

No auto-summary, no OpenAI/Copilot/Teams APIs, no bot, no calendar, no diarization, no model download, no Hugging Face, no telemetry, no auto-update, no stealth recording, no FFmpeg, no SQLite.

Phase 2+ items in the baseline spec remain out of scope.

---

## 2. Decisions locked in brainstorming

| Topic | Decision |
| --- | --- |
| TFM | **.NET 10** (`net10.0-windows` for App; `net10.0` for Core) |
| UI | WPF + MVVM; `CommunityToolkit.Mvvm` allowed if it clearly simplifies VMs |
| Audio library | **NAudio 2.3.0** stable; capture behind Core interfaces |
| Whisper packages | **Whisper.net 1.9.1** + **Whisper.net.Runtime 1.9.1** (CPU); no AllRuntimes |
| DI | `Microsoft.Extensions.DependencyInjection` in App only |
| Single instance | Named mutex; second instance shows message and exits (MVP) |
| Capture APIs | Start with `WasapiCapture` + `WasapiLoopbackCapture`; migrate to `WasapiRecorder` only if a spike proves material benefit |
| Loopback silence | **Strategy A first:** play inaudible silence on the same render endpoint while recording; **Strategy B** (monotonic gap fill with zero samples) as documented fallback |
| Solution shape | `App` + `Core` + `Core.Tests` + `IntegrationTests` only — no Infrastructure/Domain/Application split |
| Core purity | Core must not reference WPF, NAudio, or Whisper.net |
| Implementation order | Domain-first with early audio/Whisper spikes after interfaces exist; UI after real pipeline pieces |
| Models | Local `*.bin` only; default expected `ggml-base.bin` (multilingual); discover dynamically; no downloader |
| Persistence | Filesystem source of truth; `%LOCALAPPDATA%\LocalMeetingNotes\settings.json` for config only |

---

## 3. Architecture

### Projects

```text
LocalMeetingNotes.sln
├─ src/
│  ├─ LocalMeetingNotes.App/      # WPF, tray, NAudio, Whisper.net, watchers, DI composition
│  └─ LocalMeetingNotes.Core/     # models, interfaces, pure services, state machine, merge, queue logic
├─ tests/
│  ├─ LocalMeetingNotes.Core.Tests/
│  └─ LocalMeetingNotes.IntegrationTests/
├─ models/                        # ggml-*.bin (manual provisioning)
└─ docs/superpowers/{specs,plans}/
```

### Dependency rule

- `App` → `Core`
- `Core` has **zero** references to WPF / WinForms / NAudio / Whisper.net
- Concrete adapters live in App
- ViewModels call Core orchestration services through interfaces; they never call Whisper.net or NAudio directly

### Composition

Use `Microsoft.Extensions.DependencyInjection` in the App composition root for clear lifetimes (singleton settings store, queue worker, device/model services; transient dialogs/windows). No other DI container.

### High-level flow

```text
UI / Tray
   → ViewModels
      → Recording coordinator (Core orchestration + App audio adapter)
         → .processing/<sessionId>/{session.json, mic.wav, system.wav}
      → Transcription queue (single consumer)
         → ITranscriptionEngine (Whisper.net adapter)
         → checkpoints (mic/system.transcript.json)
         → TranscriptMerger
         → IMeetingNoteWriter (atomic .md)
         → optional audio cleanup
```

---

## 4. Domain model

### MeetingMetadata

- `Title` (required)
- `Participants` (optional list)
- `Context` (optional)
- `References` (optional list of free-form strings)

### MeetingSession

- `SessionId`, metadata, `StartedAt`, `StoppedAt`
- `Status` (explicit enum)
- Mic/system device id + friendly name
- Paths to `mic.wav` / `system.wav`
- Capture start offsets (ms) relative to shared monotonic session start
- `ModelPath` / `ModelFileName` snapshot for the job
- `Language` (`auto` | `en` | `es`)
- `Error`, `RetryCount`
- Root meetings folder snapshot for the session (so settings changes mid-flight do not move an active session)

### SessionStatus

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

### Global app status (separate from session)

`Ready` | `Recording` | `Transcribing` | `RecordingAndTranscribing` | `Error`

Tray icon color derives from recording active + worker active + actionable failures.

### Transcript types

- `TranscriptSegment(Start, End, Text)` per track
- Merged segment adds `Speaker` = `You` | `Remote`
- Track emptiness decided by simple RMS/peak analyzer in Core (no external VAD)

---

## 5. Filesystem layout

Default meetings root: `%USERPROFILE%\Documents\LocalMeetingNotes`

```text
LocalMeetingNotes/
├─ yyyy-MM-dd_HHmm - <SanitizedTitle>.md
├─ .processing/
│  └─ <sessionId>/
│     ├─ session.json
│     ├─ mic.wav
│     ├─ system.wav
│     ├─ mic.transcript.json      # checkpoint
│     └─ system.transcript.json  # checkpoint
└─ .logs/
   └─ app-yyyy-MM-dd.log
```

Completed meetings are ideally **one `.md` only**. Processing folder exists only while recording / waiting / transcribing / failed / interrupted.

Settings path: `%LOCALAPPDATA%\LocalMeetingNotes\settings.json`

---

## 6. Settings

### Shape (human-editable JSON)

- `meetingsFolder`, `modelsFolder`, `selectedModel` (filename relative to models folder)
- `language`, `transcriptionThreads` (default 4)
- `microphone` / `systemOutput`: `{ mode, deviceId }`
  - mic modes: `defaultCommunications` | `device`
  - output modes: `default` | `device`
- `deleteAudioAfterSuccess` (default true)
- `startMinimized`, `closeToTray` (default true)

No meeting history in settings. Unknown fields tolerated. Corrupted JSON at startup → backup copy + defaults/last-known-good + visible warning (no crash).

### Runtime reload

- `FileSystemWatcher` + debounce (≈300–1000 ms) on `settings.json`
- Invalid interim JSON keeps last-known-good
- Changes never mutate an **active** recording or **in-flight** transcription (devices/model/language/threads/meetings folder apply to the next job/recording)
- UI refreshes after successful reload

### Models

- Default `modelsFolder`: `${AppBaseDirectory}\models` (overridable in settings)
- Discover `modelsFolder\*.bin`
- Refresh on Settings open, Refresh button; App may host a debounced `FileSystemWatcher` with stability check (do not treat a still-growing file as ready)
- **Core validation:** exists, size > 0, cheap sanity checks that need no native Whisper
- **App load probe** (via interface): attempt `WhisperFactory.FromPath` when the user selects a model or before transcription; surface ModelInvalid / NativeRuntimeLoadFailure without deleting audio
- Missing/invalid model **does not** block recording → `WaitingForModel` after Stop
- Do not auto-start heavy transcription when a `.bin` appears; user must click Transcribe/Retry

---

## 7. Audio capture

### Requirements

- Two independent streams: mic → `mic.wav`, system loopback → `system.wav`
- Do **not** mix before Whisper
- Target format: PCM WAV, 16 kHz, mono, 16-bit, streaming (avoid storing hours of native float then converting at end)
- Shared monotonic `Stopwatch`; persist per-capture start offsets
- Device selection persisted by ID; defaults resolved at each new recording start; no silent mid-recording device switch
- If one source fails at start → explicit Continue (single source) / Cancel dialog; never silent degradation

### Loopback silence (critical)

1. **Primary (A):** while recording, play disposable inaudible silence (sample value 0) on the same render endpoint so loopback packets keep flowing.
2. **Fallback (B):** if A fails on real devices (Bluetooth, permissions, lifecycle), detect gaps with monotonic clock and insert equivalent zero samples. Never derive timeline solely from captured byte count.

### Mandatory silence-gap test

10 s system audio → 30 s silence → 10 s system audio → second block near **00:40**, not 00:10.

### Meters

Simple peak/RMS bars for Mic and System at ~5–10 Hz. Warn if a source stays silent for several seconds; do not auto-stop.

---

## 8. Transcription pipeline

### Queue

- Single-consumer worker
- At most one session transcribing at a time
- Within a session: mic then system sequentially (no parallel Whisper)
- New recordings allowed while queue is busy
- Stop recording does **not** wait for Whisper

### Engine abstraction (Core)

```csharp
Task<TrackTranscript> TranscribeAsync(
    string wavPath,
    TranscriptionOptions options,
    IProgress<TranscriptionProgress>? progress,
    CancellationToken cancellationToken);
```

App adapter: **Whisper.net 1.9.1** + **Whisper.net.Runtime 1.9.1** (CPU only). No `AllRuntimes`, no CUDA, no network downloaders. Load via `WhisperFactory.FromPath(modelPath)`. Lazy-load model; snapshot model path/filename per job.

Prereq documentation: VC++ Redistributable (VS 2022 x64) as required by Whisper.net; friendly error on native load failure; do not auto-install.

### Checkpoints

After each track finishes, atomically write `*.transcript.json`. Retry reuses valid checkpoints. Changing model or language invalidates matching checkpoints.

### Merge

- Mic → You, System → Remote
- Normalize timestamps with capture offsets
- Sort by start; stable order on ties
- Keep overlaps (both speakers)
- Group consecutive same-speaker segments if gap ≤ ~2 s and no intervening other speaker

### Markdown

- Filename: `yyyy-MM-dd_HHmm - <SanitizedTitle>.md` (sanitize Windows-invalid chars; never overwrite — suffix or short session id)
- UTF-8; metadata header + Context + Transcript with `[HH:mm:ss] **You|Remote:** …`
- Atomic write: `.md.tmp` → flush → replace → verify → then cleanup
- Delete audio only if setting ON and success fully confirmed

### Empty tracks

Core `AudioActivityAnalyzer` (RMS/peak): near-empty track skips Whisper and returns empty transcript.

---

## 9. Recovery, exit, single instance

### Startup recovery

Scan `.processing/*/session.json`:

| Found status | Action |
| --- | --- |
| Queued / WaitingForModel / Failed | Show and allow enqueue/retry |
| Transcribing* | Mark Interrupted; resume from checkpoint or start |
| Recording / Stopping | Preserve WAVs; validate; offer “Try to transcribe recovered audio” |

### Exit

- Recording active → dialog Stop and Save / Cancel (never silent discard)
- Transcribing → cancel safely; preserve audio/checkpoints; Interrupted; recover next launch

### Single instance

Named mutex. **MVP behavior:** if a second instance starts, show a short message that the app is already running and exit. Activating/focusing the first window from a second process is Phase 2 if needed.

### X button

Hide to tray when `closeToTray` is true; do not kill the app.

---

## 10. UI (MVP)

### Main window

- Primary Start / Stop
- Status line + persistent no-model warning when applicable
- Mic/System meters while recording + timer `HH:mm:ss`
- Recent/Queue list from filesystem scan (`*.md` + `.processing`), not a DB
- Row actions: open note, open folder, copy full note, retry, settings (for WaitingForModel)

### Start dialog

Title (required, prefilled `Meeting yyyy-MM-dd HH-mm`), Participants, Context, References. Enter starts; Escape cancels.

### Settings

Meetings folder, tray behaviors, delete-audio flag, mic/output device dropdowns, models folder, model dropdown + Refresh + validation status, language, threads if exposed, Open `settings.json` / config folder. **No Download button.**

### Tray

`NotifyIcon` via WinForms interop. Double-click restore. Context: Start/Stop/Open/Open Meetings Folder/Settings/Exit. Color + tooltip from global status.

Accessibility: keyboard paths, text+icon (not color alone), tooltips.

Document: headphones recommended for best You/Remote separation (no AEC in MVP).

---

## 11. Offline / privacy

Runtime must work with network adapter disabled given installed binaries + local model + writable folder.

No runtime code paths for model download, HF, external APIs, updater, telemetry, or log upload.

Recording starts only on explicit user action; visible red UI + tray while recording.

---

## 12. Errors

Actionable messages; preserve audio on all failure paths. Categories include ModelMissing, ModelInvalid, NativeRuntimeLoadFailure, device/recording failures, AudioFileInvalid, TranscriptionFailure, OutputFolderPermissionDenied, MarkdownWriteFailure, DiskSpaceLow, Unknown.

Disk space check before Start (strong warn / block below a configurable threshold, initial ~500 MB).

---

## 13. Logging

Daily local files under meetings `.logs/`. Log lifecycle events, devices, model load, job states, durations, errors, cleanup. Do **not** log full transcripts by default.

---

## 14. Testing strategy

### Core.Tests (required early)

FilenameSanitizer, MarkdownWriter, TranscriptMerger, ModelValidator, SessionStateMachine, Queue, Recovery, AudioActivityAnalyzer — cases listed in baseline §48.

### IntegrationTests

Create project from day one; add tests only when real integrations exist (filesystem atomicity, settings reload, Whisper with local model behind env flag, etc.). CI must not require hardware or model download.

### Manual matrix

Baseline §50 (mic-only, system-only, both, silence gap, 30+ min, no model, Whisper failure, exit during transcription, tray, devices, settings reload, multiple models, offline).

### Definition of done

Feature is done only with appropriate tests, error handling, cancellation, dispose, logging, and responsive UI — not merely “compiles”.

---

## 15. Implementation sequence

1. Solution skeleton (App, Core, both test projects)
2. Domain models + session state machine (TDD)
3. Filesystem session repository
4. Settings persistence + runtime reload abstractions/impl
5. Model catalog/discovery/validation
6. Device enumeration interfaces + App adapter
7. Mic capture spike
8. Loopback spike + silence strategy A (B if needed)
9. Recording coordinator + WAV validation
10. Whisper local spike (CPU runtime)
11. Transcription service + checkpoints
12. Merge + Markdown atomic writer
13. Queue + recovery
14. Main UI, Start dialog, meters
15. Tray, errors/retry/WaitingForModel, copy/open
16. Logging, publish script, offline acceptance

Do not polish UI before audio + Whisper are proven.

Publish: `dotnet publish -c Release -r win-x64 --self-contained true` (trimming off; single-file off until native Whisper layout is validated). `publish.ps1`: clean → restore → test → publish (fail if tests fail).

---

## 16. Risks and mitigations

| Risk | Mitigation |
| --- | --- |
| Loopback timeline compression | Strategy A + mandatory gap test; B fallback |
| Native Whisper / VC++ load failures | Friendly errors; document prereq; keep recording working without model |
| Corporate blocked downloads | No runtime network; manual model provisioning via split archives in repo |
| Headset connect/disconnect | Persist device IDs; warn if missing; no silent switch mid-recording |
| Settings/model file mid-copy | Debounced watchers + file stability checks |
| Data loss | Never delete audio until Markdown verified; checkpoints; recovery on startup |
| NAudio 3 API churn | Stay on 2.3.0 behind interfaces until spike justifies migration |

---

## 17. Acceptance criteria (MVP)

Must satisfy baseline §51, including: offline runtime, dual-track capture, Stop does not wait for Whisper, single transcription worker, You/Remote merge, filesystem history, tray + close-to-tray, recovery, model discovery/persistence, selectable devices, runtime settings reload safety, WaitingForModel when no `.bin`, no HF/telemetry.

---

## 18. References

- Baseline product/tech spec: `docs/LocalMeetingNotes_Cursor_Spec.md`
- NAudio: https://github.com/naudio/NAudio
- Whisper.net: https://github.com/sandrohanea/whisper.net
- whisper.cpp models: https://github.com/ggml-org/whisper.cpp/tree/master/models
