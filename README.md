# LocalMeetingNotes

Local, offline Windows app that records microphone + system audio, transcribes with Whisper (CPU), and saves a Markdown meeting note.

## Requirements

- Windows 11
- .NET 10 SDK (for development)
- Microsoft Visual C++ Redistributable (VS 2022 x64) for Whisper.net native runtime
- Local Whisper model as `models/ggml-base.bin` (multilingual base)
- Headphones recommended for best You / Remote separation

## Provision the Whisper model

Compressed parts ship in `models/`. Extract them manually so the result is:

```text
models/ggml-base.bin
```

The app never downloads models. It only reads ready `.bin` files from the configured models folder.

## Build and run

```bash
dotnet build LocalMeetingNotes.slnx
dotnet run --project src/LocalMeetingNotes.App
dotnet test LocalMeetingNotes.slnx
```

## Publish

```powershell
./publish.ps1
```

Output: `publish/win-x64/` (self-contained, not single-file, trimming off).

## Offline acceptance

With network disabled, installed binaries, local model present, and a writable meetings folder, the app must: start → record → stop → transcribe → write Markdown.

## Docs

- Spec: `docs/superpowers/specs/2026-08-07-local-meeting-notes-design.md`
- Plan: `docs/superpowers/plans/2026-08-07-local-meeting-notes.md`
