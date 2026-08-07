#!/usr/bin/env pwsh
$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

Write-Host "==> Clean"
dotnet clean LocalMeetingNotes.sln -c Release

Write-Host "==> Restore"
dotnet restore LocalMeetingNotes.sln

Write-Host "==> Test"
dotnet test LocalMeetingNotes.sln -c Release --no-restore
if ($LASTEXITCODE -ne 0) {
    throw "Tests failed; publish aborted."
}

Write-Host "==> Publish win-x64 self-contained"
dotnet publish src/LocalMeetingNotes.App/LocalMeetingNotes.App.csproj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:PublishTrimmed=false `
    -o publish/win-x64

Write-Host "Published to publish/win-x64"
