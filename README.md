# LocalMeetingNotes

Local, offline Windows app that records microphone + system audio, transcribes with Whisper (CPU), and saves a Markdown meeting note.

## Requirements

- Windows 11
- .NET 10 SDK
- Whisper model as a local `.bin` (see below)
- Microsoft Visual C++ Redistributable (VS 2022 x64) for Whisper.net native runtime

## Provision the Whisper model

Compressed parts ship in `models/`. Extract them manually so the result is:

```text
models/ggml-base.bin
```

The app never downloads models and ignores the archive format — it only reads ready `.bin` files.

## Build and run

```bash
dotnet build LocalMeetingNotes.slnx
dotnet run --project src/LocalMeetingNotes.App
dotnet test LocalMeetingNotes.slnx
```

## Status

MVP under active development on `feature/local-meeting-notes-mvp`. See `docs/superpowers/specs/` and `docs/superpowers/plans/`.
