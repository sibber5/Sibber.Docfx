@REM SPDX-License-Identifier: MIT
@REM Copyright (c) 2025 sibber (GitHub: sibber5)

@echo off
setlocal enabledelayedexpansion

rmdir "./ExtractSearchIndexEx/bin/Release/net8.0/publish" /s /q

dotnet clean ./ExtractSearchIndexEx/Sibber.Docfx.ExtractSearchIndexEx.csproj -c Release
dotnet publish ./ExtractSearchIndexEx/Sibber.Docfx.ExtractSearchIndexEx.csproj -c Release

xcopy "./ExtractSearchIndexEx/bin/Release/net8.0/publish" "./docs/templates/customizations/plugins" /v /s /e /h /y

if "%1" NEQ "--no-pause" pause
