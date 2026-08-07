# LocalMeetingNotes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a Windows WPF app that records mic + system audio locally, transcribes with Whisper CPU, and writes a single Markdown meeting note — fully offline, no DB, no cloud.

**Architecture:** `LocalMeetingNotes.Core` holds pure domain, interfaces, state machine, merge, queue/recovery, and filesystem logic. `LocalMeetingNotes.App` holds WPF/MVVM and Windows adapters (NAudio, Whisper.net, tray, watchers, DI). Filesystem is source of truth (`.md` + `.processing`).

**Tech Stack:** .NET 10, WPF, CommunityToolkit.Mvvm, NAudio 2.3.0, Whisper.net 1.9.1 + Whisper.net.Runtime 1.9.1 (CPU), System.Text.Json, Microsoft.Extensions.DependencyInjection, xUnit + FluentAssertions.

## Global Constraints

- Target: App `net10.0-windows`, Core `net10.0`
- Core MUST NOT reference WPF, WinForms, NAudio, or Whisper.net
- No Hugging Face, model download, telemetry, cloud, login, SQLite, FFmpeg
- Never delete audio until Markdown is written and verified
- Missing Whisper model must not block recording (`WaitingForModel`)
- Single transcription worker; Stop must not wait for Whisper
- Loopback silence: Strategy A (play silence) first; Strategy B (gap-fill) only if A fails on real devices
- NAudio 2.3.0 with `WasapiCapture` / `WasapiLoopbackCapture` behind interfaces
- Settings at `%LOCALAPPDATA%\LocalMeetingNotes\settings.json`; meetings default `%USERPROFILE%\Documents\LocalMeetingNotes`
- Commits: small, frequent; do not push unless asked
- Work on branch `feature/local-meeting-notes-mvp` (not bare main commits for implementation)

## File Structure (locked)

```text
LocalMeetingNotes.sln
src/LocalMeetingNotes.Core/
  Models/          MeetingMetadata, MeetingSession, SessionStatus, AppSettings, Transcript*, AudioDeviceInfo, WhisperModelInfo, ErrorCategory
  Interfaces/      IAudioCaptureService, IAudioDeviceService, ITranscriptionEngine, ITranscriptionQueue, ISessionRepository, IMeetingNoteWriter, IModelCatalog, IModelLoadProbe, ISettingsStore, IRecoveryService, IClock, ILogger
  Services/        SessionStateMachine, TranscriptMerger, FilenameSanitizer, MarkdownNoteBuilder, AudioActivityAnalyzer, TranscriptionQueue, RecoveryService, ModelCatalog (pure parts)
  Files/           SessionPaths, AtomicFile, MeetingHistoryScanner
  Settings/        AppSettings, SettingsDefaults, SettingsValidator
src/LocalMeetingNotes.App/
  Views/, ViewModels/, Tray/, Bootstrap/
  Audio/           NAudioDeviceService, NAudioCaptureService, SilencePlayer, ResamplingWavWriter
  Transcription/   WhisperTranscriptionEngine
  Settings/        JsonSettingsStore, SettingsFileWatcher
  Models/          (UI-only if needed)
tests/LocalMeetingNotes.Core.Tests/
tests/LocalMeetingNotes.IntegrationTests/
models/            *.rar kept; *.bin gitignored
publish.ps1
README.md
```

---

### Task 1: Solution skeleton

**Files:**
- Create: `LocalMeetingNotes.sln`
- Create: `src/LocalMeetingNotes.Core/LocalMeetingNotes.Core.csproj`
- Create: `src/LocalMeetingNotes.App/LocalMeetingNotes.App.csproj`
- Create: `tests/LocalMeetingNotes.Core.Tests/LocalMeetingNotes.Core.Tests.csproj`
- Create: `tests/LocalMeetingNotes.IntegrationTests/LocalMeetingNotes.IntegrationTests.csproj`
- Create: `src/LocalMeetingNotes.App/App.xaml`, `App.xaml.cs`, `MainWindow.xaml`, `MainWindow.xaml.cs`
- Modify: `README.md`

**Interfaces:**
- Produces: buildable solution; App references Core; test projects reference Core (IntegrationTests may reference App later)

- [ ] **Step 1: Create feature branch**

```bash
git checkout -b feature/local-meeting-notes-mvp
```

- [ ] **Step 2: Create projects**

```bash
dotnet new sln -n LocalMeetingNotes
dotnet new classlib -n LocalMeetingNotes.Core -o src/LocalMeetingNotes.Core -f net10.0
dotnet new wpf -n LocalMeetingNotes.App -o src/LocalMeetingNotes.App -f net10.0-windows
dotnet new xunit -n LocalMeetingNotes.Core.Tests -o tests/LocalMeetingNotes.Core.Tests -f net10.0
dotnet new xunit -n LocalMeetingNotes.IntegrationTests -o tests/LocalMeetingNotes.IntegrationTests -f net10.0
dotnet sln LocalMeetingNotes.sln add src/LocalMeetingNotes.Core src/LocalMeetingNotes.App tests/LocalMeetingNotes.Core.Tests tests/LocalMeetingNotes.IntegrationTests
dotnet add src/LocalMeetingNotes.App reference src/LocalMeetingNotes.Core
dotnet add tests/LocalMeetingNotes.Core.Tests reference src/LocalMeetingNotes.Core
dotnet add tests/LocalMeetingNotes.IntegrationTests reference src/LocalMeetingNotes.Core
dotnet add tests/LocalMeetingNotes.Core.Tests package FluentAssertions
dotnet add tests/LocalMeetingNotes.IntegrationTests package FluentAssertions
```

Set App csproj: `UseWPF`, `UseWindowsForms` (for NotifyIcon), packages later. Delete default `Class1.cs`. Add placeholder IntegrationTests test that Assert.True(true) can be replaced — or a single skipped stub documenting intent.

- [ ] **Step 3: Verify build**

```bash
dotnet build LocalMeetingNotes.sln
dotnet test LocalMeetingNotes.sln --no-build
```

Expected: BUILD SUCCESS; tests pass (default/empty).

- [ ] **Step 4: Commit**

```bash
git add LocalMeetingNotes.sln src tests README.md
git commit -m "chore: scaffold LocalMeetingNotes solution (.NET 10 App+Core+tests)"
```

---

### Task 2: Domain models + SessionStateMachine (TDD)

**Files:**
- Create: `src/LocalMeetingNotes.Core/Models/SessionStatus.cs`
- Create: `src/LocalMeetingNotes.Core/Models/GlobalAppStatus.cs`
- Create: `src/LocalMeetingNotes.Core/Models/MeetingMetadata.cs`
- Create: `src/LocalMeetingNotes.Core/Models/MeetingSession.cs`
- Create: `src/LocalMeetingNotes.Core/Models/Speaker.cs`
- Create: `src/LocalMeetingNotes.Core/Models/TranscriptSegment.cs`
- Create: `src/LocalMeetingNotes.Core/Models/MergedTranscriptSegment.cs`
- Create: `src/LocalMeetingNotes.Core/Models/ErrorCategory.cs`
- Create: `src/LocalMeetingNotes.Core/Services/SessionStateMachine.cs`
- Test: `tests/LocalMeetingNotes.Core.Tests/Services/SessionStateMachineTests.cs`

**Interfaces:**
- Produces: `SessionStateMachine.CanTransition(from, to)`, `Transition(session, to)` throwing on illegal moves; session model with all fields from design §4

- [ ] **Step 1: Write failing state machine tests**

Cover legal paths: `Draft→Recording→Stopping→Queued→TranscribingMic→TranscribingSystem→Merging→WritingNote→Completed`, `Queued→WaitingForModel`, `*→Failed`, `Transcribing*→Interrupted`, reject `Completed→Recording`.

- [ ] **Step 2: Run tests — expect FAIL**

```bash
dotnet test tests/LocalMeetingNotes.Core.Tests --filter SessionStateMachine
```

- [ ] **Step 3: Implement models + state machine**

Explicit allowed transition map. Persist status via session object mutation only through machine.

- [ ] **Step 4: Run tests — expect PASS**

- [ ] **Step 5: Commit**

```bash
git commit -m "feat: add domain models and session state machine"
```

---

### Task 3: FilenameSanitizer + MarkdownNoteBuilder (TDD)

**Files:**
- Create: `src/LocalMeetingNotes.Core/Services/FilenameSanitizer.cs`
- Create: `src/LocalMeetingNotes.Core/Services/MarkdownNoteBuilder.cs`
- Create: `src/LocalMeetingNotes.Core/Files/AtomicFile.cs`
- Create: `src/LocalMeetingNotes.Core/Services/MeetingNoteWriter.cs` implementing `IMeetingNoteWriter`
- Create: `src/LocalMeetingNotes.Core/Interfaces/IMeetingNoteWriter.cs`
- Test: `tests/LocalMeetingNotes.Core.Tests/Services/FilenameSanitizerTests.cs`
- Test: `tests/LocalMeetingNotes.Core.Tests/Services/MarkdownNoteWriterTests.cs`

**Interfaces:**
- Produces:
  - `FilenameSanitizer.Sanitize(string title) -> string`
  - `FilenameSanitizer.BuildNoteFileName(DateTimeOffset startedAt, string title, string? disambiguator = null) -> string` → `yyyy-MM-dd_HHmm - <title>.md`
  - `IMeetingNoteWriter.WriteAsync(MeetingSession, IReadOnlyList<MergedTranscriptSegment>, CancellationToken) -> Task<string>` (full path)
  - Atomic: write `.md.tmp`, flush, `File.Move(..., overwrite: false)`, verify readback

- [ ] **Step 1: Failing tests** — invalid chars, empty title fallback, Unicode, duplicate disambiguator, UTF-8 content, You/Remote lines, timestamps, atomic write (temp dir)

- [ ] **Step 2: Implement until green**

- [ ] **Step 3: Commit** — `feat: add markdown note writer and filename sanitizer`

---

### Task 4: TranscriptMerger + AudioActivityAnalyzer (TDD)

**Files:**
- Create: `src/LocalMeetingNotes.Core/Services/TranscriptMerger.cs`
- Create: `src/LocalMeetingNotes.Core/Services/AudioActivityAnalyzer.cs`
- Test: `tests/LocalMeetingNotes.Core.Tests/Services/TranscriptMergerTests.cs`
- Test: `tests/LocalMeetingNotes.Core.Tests/Services/AudioActivityAnalyzerTests.cs`

**Interfaces:**
- Produces:
  - `TranscriptMerger.Merge(micSegments, systemSegments, micOffset, systemOffset, TimeSpan groupGap = 2s) -> IReadOnlyList<MergedTranscriptSegment>`
  - `AudioActivityAnalyzer.HasSignificantActivity(ReadOnlySpan<byte> pcm16Mono, WaveFormatInfo format) -> bool` (or path-based overload reading WAV header+samples in Core without NAudio — implement minimal WAV PCM reader in Core)

- [ ] **Step 1: Merger tests** — mic before remote, remote before, overlap keeps both, same timestamp stable order, offsets applied, grouping ≤2s, empty tracks

- [ ] **Step 2: Analyzer tests** — all-zero silence false; synthetic loud PCM true; low noise below threshold false

- [ ] **Step 3: Implement + green + commit** — `feat: add transcript merger and audio activity analyzer`

---

### Task 5: Session repository + paths (TDD)

**Files:**
- Create: `src/LocalMeetingNotes.Core/Files/SessionPaths.cs`
- Create: `src/LocalMeetingNotes.Core/Interfaces/ISessionRepository.cs`
- Create: `src/LocalMeetingNotes.Core/Files/FileSessionRepository.cs`
- Create: `src/LocalMeetingNotes.Core/Models/TrackTranscriptCheckpoint.cs`
- Test: `tests/LocalMeetingNotes.Core.Tests/Files/FileSessionRepositoryTests.cs`

**Interfaces:**
- Produces:
```csharp
public interface ISessionRepository
{
    Task SaveAsync(MeetingSession session, CancellationToken ct = default);
    Task<MeetingSession?> LoadAsync(string meetingsRoot, Guid sessionId, CancellationToken ct = default);
    Task<IReadOnlyList<MeetingSession>> LoadProcessingAsync(string meetingsRoot, CancellationToken ct = default);
    Task SaveCheckpointAsync(string meetingsRoot, Guid sessionId, TrackTranscriptCheckpoint checkpoint, CancellationToken ct = default);
    Task<TrackTranscriptCheckpoint?> LoadCheckpointAsync(string meetingsRoot, Guid sessionId, string track, CancellationToken ct = default);
    Task DeleteProcessingFolderAsync(string meetingsRoot, Guid sessionId, CancellationToken ct = default);
}
```
- `session.json` via System.Text.Json, indented, camelCase
- Paths: `{root}/.processing/{id}/session.json|mic.wav|system.wav|mic.transcript.json|system.transcript.json`

- [ ] **Step 1: Tests with temp directories** — roundtrip save/load, list processing, checkpoint roundtrip, delete folder

- [ ] **Step 2: Implement + green + commit** — `feat: add filesystem session repository`

---

### Task 6: Settings store abstractions + Core validation (TDD)

**Files:**
- Create: `src/LocalMeetingNotes.Core/Settings/AppSettings.cs`
- Create: `src/LocalMeetingNotes.Core/Settings/DeviceSelection.cs`
- Create: `src/LocalMeetingNotes.Core/Settings/SettingsDefaults.cs`
- Create: `src/LocalMeetingNotes.Core/Settings/SettingsValidator.cs`
- Create: `src/LocalMeetingNotes.Core/Interfaces/ISettingsStore.cs`
- Create: `src/LocalMeetingNotes.App/Settings/JsonSettingsStore.cs`
- Create: `src/LocalMeetingNotes.App/Settings/SettingsFileWatcher.cs`
- Test: `tests/LocalMeetingNotes.Core.Tests/Settings/SettingsValidatorTests.cs`
- Test: `tests/LocalMeetingNotes.IntegrationTests/Settings/JsonSettingsStoreTests.cs`

**Interfaces:**
```csharp
public interface ISettingsStore
{
    AppSettings Current { get; }
    event EventHandler<AppSettings>? SettingsReloaded;
    Task<AppSettings> LoadAsync(CancellationToken ct = default);
    Task SaveAsync(AppSettings settings, CancellationToken ct = default);
}
```
- Defaults: meetings folder Documents\LocalMeetingNotes; modelsFolder = AppBaseDirectory\models; selectedModel ggml-base.bin; language auto; threads 4; deleteAudioAfterSuccess true; closeToTray true; mic defaultCommunications; output default
- Atomic save `.tmp` then replace; corrupt startup → backup + defaults; watcher debounce 500ms; invalid JSON keeps last-known-good

- [ ] **Step 1: Validator unit tests + store integration tests in temp LocalAppData-like folder**

- [ ] **Step 2: Implement Core models + App JsonSettingsStore/Watcher**

- [ ] **Step 3: Green + commit** — `feat: add settings model and JSON settings store`

---

### Task 7: Model catalog (TDD)

**Files:**
- Create: `src/LocalMeetingNotes.Core/Models/WhisperModelInfo.cs`
- Create: `src/LocalMeetingNotes.Core/Interfaces/IModelCatalog.cs`
- Create: `src/LocalMeetingNotes.Core/Interfaces/IModelLoadProbe.cs`
- Create: `src/LocalMeetingNotes.Core/Services/ModelCatalog.cs`
- Test: `tests/LocalMeetingNotes.Core.Tests/Services/ModelCatalogTests.cs`

**Interfaces:**
```csharp
public interface IModelCatalog
{
    IReadOnlyList<WhisperModelInfo> Discover(string modelsFolder);
    ModelValidationResult ValidateFile(string path); // exists, size>0, cheap checks
    string? ResolveSelectedModelPath(AppSettings settings);
}

public interface IModelLoadProbe
{
    Task<ModelValidationResult> ProbeLoadAsync(string absolutePath, CancellationToken ct = default);
}
```
- Discover `*.bin`; skip unstable sizes if caller passes stability filter; missing selection → null path

- [ ] **Step 1: Tests** — missing folder, empty, zero-byte invalid, resolve relative selectedModel, folder changed

- [ ] **Step 2: Implement + commit** — `feat: add whisper model catalog discovery`

---

### Task 8: Audio device + capture interfaces + NAudio adapters (spike)

**Files:**
- Create: `src/LocalMeetingNotes.Core/Models/AudioDeviceInfo.cs`
- Create: `src/LocalMeetingNotes.Core/Interfaces/IAudioDeviceService.cs`
- Create: `src/LocalMeetingNotes.Core/Interfaces/IAudioCaptureService.cs`
- Create: `src/LocalMeetingNotes.Core/Models/RecordingRequest.cs`
- Create: `src/LocalMeetingNotes.Core/Models/RecordingResult.cs`
- Create: `src/LocalMeetingNotes.App/Audio/NAudioDeviceService.cs`
- Create: `src/LocalMeetingNotes.App/Audio/NAudioCaptureService.cs`
- Create: `src/LocalMeetingNotes.App/Audio/SilenceLoopbackKeeper.cs`
- Create: `src/LocalMeetingNotes.App/LocalMeetingNotes.App.csproj` — add NAudio 2.3.0
- Test: `tests/LocalMeetingNotes.Core.Tests` — fake capture tests for coordinator later; this task adds a manual spike harness or console note in README

**Interfaces:**
```csharp
public interface IAudioDeviceService
{
    IReadOnlyList<AudioDeviceInfo> GetMicrophones();
    IReadOnlyList<AudioDeviceInfo> GetRenderDevices();
    AudioDeviceInfo? GetDefaultCommunicationsMicrophone();
    AudioDeviceInfo? GetDefaultRenderDevice();
}

public interface IAudioCaptureService
{
    bool IsRecording { get; }
    event EventHandler<AudioMeterEventArgs>? MetersUpdated;
    Task StartAsync(RecordingRequest request, CancellationToken ct = default);
    Task<RecordingResult> StopAsync(CancellationToken ct = default);
}
```
- Capture: WasapiCapture + WasapiLoopbackCapture → stream to 16k mono PCM16 WAVs
- Shared Stopwatch; record mic/system start offsets
- Strategy A: SilenceLoopbackKeeper plays zeros on same render device while recording
- If one device fails at start, throw typed result so UI can Continue/Cancel (partial start only after explicit continue — implement via request flags `AllowMicOnly` / `AllowSystemOnly`)

- [ ] **Step 1: Add packages + implement device enumeration**

- [ ] **Step 2: Implement dual capture + silence keeper + WAV writers**

- [ ] **Step 3: Manual verify mic.wav/system.wav lengths on short recording (document in commit message); add IntegrationTests stub skipped `[Fact(Skip="hardware")]` for silence-gap when env set**

- [ ] **Step 4: Commit** — `feat: add NAudio dual-track capture with loopback silence keeper`

---

### Task 9: Recording coordinator (Core) + WAV validation

**Files:**
- Create: `src/LocalMeetingNotes.Core/Services/RecordingCoordinator.cs`
- Create: `src/LocalMeetingNotes.Core/Services/WavFileValidator.cs`
- Test: `tests/LocalMeetingNotes.Core.Tests/Services/RecordingCoordinatorTests.cs` (fake `IAudioCaptureService`, `ISessionRepository`, `ISettingsStore`)

**Interfaces:**
- `StartAsync(MeetingMetadata, CancellationToken)` creates session folder, Draft→Recording, starts capture
- `StopAsync` → Stopping → close WAVs → validate → Queued or WaitingForModel (if no resolved model) → save session
- Never deletes audio on failure

- [ ] **Step 1: Failing tests with fakes**

- [ ] **Step 2: Implement + green + commit** — `feat: add recording coordinator`

---

### Task 10: Whisper transcription engine adapter

**Files:**
- Create: `src/LocalMeetingNotes.Core/Interfaces/ITranscriptionEngine.cs`
- Create: `src/LocalMeetingNotes.Core/Models/TranscriptionOptions.cs`
- Create: `src/LocalMeetingNotes.Core/Models/TrackTranscript.cs`
- Create: `src/LocalMeetingNotes.App/Transcription/WhisperTranscriptionEngine.cs`
- Create: `src/LocalMeetingNotes.App/Transcription/WhisperModelLoadProbe.cs`
- Modify: App csproj — Whisper.net 1.9.1, Whisper.net.Runtime 1.9.1
- Test: Integration test skipped unless `LOCALMEETINGNOTES_WHISPER_MODEL` env points to `.bin`

**Interfaces:**
```csharp
Task<TrackTranscript> TranscribeAsync(
    string wavPath,
    TranscriptionOptions options,
    IProgress<TranscriptionProgress>? progress,
    CancellationToken cancellationToken);
```
- Lazy WhisperFactory; threads from options; language auto/en/es; trim empty segments
- Map native load failures to ErrorCategory.NativeRuntimeLoadFailure

- [ ] **Step 1: Implement engine + probe**

- [ ] **Step 2: Optional local run against models/ggml-base.bin if present**

- [ ] **Step 3: Commit** — `feat: add Whisper.net CPU transcription engine`

---

### Task 11: Transcription queue + checkpoints + pipeline

**Files:**
- Create: `src/LocalMeetingNotes.Core/Interfaces/ITranscriptionQueue.cs`
- Create: `src/LocalMeetingNotes.Core/Services/TranscriptionQueue.cs`
- Create: `src/LocalMeetingNotes.Core/Services/TranscriptionPipeline.cs`
- Test: `tests/LocalMeetingNotes.Core.Tests/Services/TranscriptionQueueTests.cs`
- Test: `tests/LocalMeetingNotes.Core.Tests/Services/TranscriptionPipelineTests.cs`

**Interfaces:**
```csharp
public interface ITranscriptionQueue
{
    void Enqueue(Guid sessionId);
    Task RetryAsync(Guid sessionId, CancellationToken ct = default);
    event EventHandler<SessionProgressEventArgs>? ProgressChanged;
}
```
- Single consumer via `Channel<Guid>` / one long-running Task
- Pipeline: load session → skip empty tracks → reuse checkpoint if model+language match → else transcribe mic then system → save checkpoints → merge → write markdown → delete processing if setting → Completed; on error Failed preserve audio
- WaitingForModel: do not dequeue for whisper until model available + user Retry/Transcribe

- [ ] **Step 1: Queue tests** — single consumer, failure does not block next, cancellation, retry

- [ ] **Step 2: Pipeline tests with fake engine**

- [ ] **Step 3: Green + commit** — `feat: add transcription queue and pipeline`

---

### Task 12: Recovery service (TDD)

**Files:**
- Create: `src/LocalMeetingNotes.Core/Interfaces/IRecoveryService.cs`
- Create: `src/LocalMeetingNotes.Core/Services/RecoveryService.cs`
- Test: `tests/LocalMeetingNotes.Core.Tests/Services/RecoveryServiceTests.cs`

**Interfaces:**
- On startup: scan processing; Queued/WaitingForModel/Failed → surface; Transcribing* → Interrupted + requeue; Recording/Stopping → preserve, mark Interrupted, offer recover flag on session

- [ ] **Step 1: Tests for each status case + missing audio**

- [ ] **Step 2: Implement + commit** — `feat: add startup session recovery`

---

### Task 13: App bootstrap, MainWindow MVVM, Start dialog

**Files:**
- Create: `src/LocalMeetingNotes.App/Bootstrap/AppServices.cs`
- Create: `src/LocalMeetingNotes.App/ViewModels/MainViewModel.cs`
- Create: `src/LocalMeetingNotes.App/ViewModels/StartRecordingViewModel.cs`
- Create: `src/LocalMeetingNotes.App/ViewModels/SessionRowViewModel.cs`
- Create: `src/LocalMeetingNotes.App/Views/StartRecordingWindow.xaml(+cs)`
- Modify: `MainWindow.xaml(+cs)`, `App.xaml.cs`
- Package: CommunityToolkit.Mvvm

**Behavior:**
- DI composition root
- Start opens dialog (title prefilled); Start/Stop; meters; recent list from scanner; no-model warning banner; timer; status text
- Close → hide to tray if setting

- [ ] **Step 1: Wire DI + MainViewModel commands**

- [ ] **Step 2: XAML layouts matching design (functional, not fancy)**

- [ ] **Step 3: Manual smoke + commit** — `feat: add main window and start recording dialog`

---

### Task 14: Settings UI + history actions

**Files:**
- Create: `src/LocalMeetingNotes.App/ViewModels/SettingsViewModel.cs`
- Create: `src/LocalMeetingNotes.App/Views/SettingsWindow.xaml(+cs)`
- Create: `src/LocalMeetingNotes.Core/Files/MeetingHistoryScanner.cs`
- Create: `src/LocalMeetingNotes.App/Services/ClipboardService.cs` (or thin helper)
- Test: `tests/LocalMeetingNotes.Core.Tests/Files/MeetingHistoryScannerTests.cs`

**Behavior:**
- Settings: folders, devices, models refresh/select, language, threads, delete-audio, tray flags, Open settings.json
- History: open md, open folder, copy full note, retry, settings for WaitingForModel
- Double-click completed → open `.md`

- [ ] **Step 1: Scanner tests + Settings VM + window**

- [ ] **Step 2: Commit** — `feat: add settings UI and meeting history actions`

---

### Task 15: System tray + single instance + logging

**Files:**
- Create: `src/LocalMeetingNotes.App/Tray/TrayIconService.cs`
- Create: `src/LocalMeetingNotes.App/Bootstrap/SingleInstance.cs`
- Create: `src/LocalMeetingNotes.Core/Interfaces/IAppLogger.cs`
- Create: `src/LocalMeetingNotes.App/Logging/FileAppLogger.cs`
- Modify: `App.xaml.cs` for mutex + tray lifecycle

**Behavior:**
- NotifyIcon colors/tooltips from GlobalAppStatus
- Context menu Start/Stop/Open/Open Meetings Folder/Settings/Exit
- Exit while recording → confirm Stop and Save
- Exit while transcribing → cancel safe / Interrupted
- Daily logs under `{meetingsRoot}/.logs/app-yyyy-MM-dd.log`; never log full transcript by default

- [ ] **Step 1: Implement tray + mutex + logger**

- [ ] **Step 2: Commit** — `feat: add tray, single-instance, and file logging`

---

### Task 16: Error UX, disk space check, publish script, README

**Files:**
- Create: `src/LocalMeetingNotes.Core/Services/DiskSpaceChecker.cs`
- Create: `publish.ps1`
- Modify: `README.md` — build, run, extract model from rar parts, VC++ redist, headphones note, offline acceptance
- Test: disk space checker unit tests

**Behavior:**
- Before Start: writable folder + disk threshold (~500 MB)
- Actionable error dialogs with Retry / Open Audio Folder / Open Log
- `publish.ps1`: clean, restore, test, publish win-x64 self-contained (no trim, no single-file)

- [ ] **Step 1: Implement checks + scripts + docs**

- [ ] **Step 2: Full `dotnet test` + `dotnet build -c Release`**

- [ ] **Step 3: Commit** — `feat: add publish script, disk checks, and README ops guide`

---

## Self-review checklist (plan author)

- [x] Spec §1–18 mapped to tasks 1–16
- [x] Core purity preserved (adapters only in App tasks 6,8,10,13–15)
- [x] WaitingForModel, silence A→B, settings reload, recovery, queue single-consumer covered
- [x] Phase 2 items excluded
- [x] No TBD placeholders in task contracts

## Execution handoff

User requested full development immediately → execute with **subagent-driven-development** on branch `feature/local-meeting-notes-mvp`, continuous until blocked or complete.
