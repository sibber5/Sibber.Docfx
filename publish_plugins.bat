@echo off
setlocal enabledelayedexpansion

rmdir "./ExtractSearchIndexEx/bin/Release/net8.0/publish" /s /q

dotnet clean ./ExtractSearchIndexEx/Sibber.Docfx.ExtractSearchIndexEx.csproj -c Release
dotnet publish ./ExtractSearchIndexEx/Sibber.Docfx.ExtractSearchIndexEx.csproj -c Release

xcopy "./ExtractSearchIndexEx/bin/Release/net8.0/publish" "./docfx/templates/custom_template/plugins" /v /s /e /h /y

if "%1" NEQ "--no-pause" pause
