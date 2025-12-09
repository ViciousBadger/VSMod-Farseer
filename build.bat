@echo off
setlocal enabledelayedexpansion

echo Building Farseer...

REM Set VINTAGE_STORY environment variable if not set
if not defined VINTAGE_STORY (
    set "VINTAGE_STORY=%APPDATA%\Vintagestory"
)

REM Build the mod
dotnet build Farseer.sln -c Release > build_output.txt 2>&1
set BUILD_EXIT=%ERRORLEVEL%

REM Show only warnings, errors, and summary
findstr /C:"warning" /C:"error" /C:"Error" /C:"Warning" /C:"succeeded" /C:"failed" build_output.txt
del build_output.txt

if %BUILD_EXIT% EQU 0 (
    echo Build successful!
    
    REM Find the built mod folder
    if exist "Farseer\bin\Release\Mods\mod" (
        REM Remove old versions from Mods folder
        del "%APPDATA%\VintagestoryData\Mods\farseer*.zip" 2>nul
        
        REM Create zip using PowerShell, reading version from modinfo.json
        powershell -Command "$modinfo = Get-Content 'Farseer\modinfo.json' | ConvertFrom-Json; $version = $modinfo.version; $zipName = 'farseer_' + $version + '.zip'; Compress-Archive -Path 'Farseer\bin\Release\Mods\mod\*' -DestinationPath $zipName -Force; Write-Host ('Created: ' + $zipName); Copy-Item $zipName '%APPDATA%\VintagestoryData\Mods\' -Force; Write-Host ('Copied to: %APPDATA%\VintagestoryData\Mods\' + $zipName)"
        
        if !ERRORLEVEL! EQU 0 (
            REM Remove log files after successful copy
            del "%APPDATA%\VintagestoryData\Logs\*.log" 2>nul
            if exist "%APPDATA%\VintagestoryData\Logs\Archive" rmdir /s /q "%APPDATA%\VintagestoryData\Logs\Archive" 2>nul
            echo Log files cleaned.
        )
    )
) else (
    echo Build failed!
    exit /b 1
)

endlocal
