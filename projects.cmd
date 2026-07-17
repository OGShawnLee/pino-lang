@echo off
setlocal enabledelayedexpansion

set use_c=0
if "%~1"=="--c" (
    set use_c=1
    shift
) else if "%~1"=="-c" (
    set use_c=1
    shift
)

:: Check if arguments were passed. If so, forward them directly to the compiler.
if not "%~1"=="" (
    dotnet run --project "%~dp0pino-csharp\pino-csharp.csproj" -- %*
    exit /b %errorlevel%
)

:: Interactive Mode
echo ===================================================
echo 🌲 Pino Interactive Project Launcher
echo ===================================================
echo.

set count=0
for /d %%i in ("%~dp0projects\*") do (
    set /a count+=1
    set "proj[!count!]=%%~nxi"
    echo   [!count!] %%~nxi
)

if %count%==0 (
    echo No projects found in projects/ directory.
    echo Defaulting to running main.pino in current directory...
    if !use_c!==1 (
        dotnet run --project "%~dp0pino-csharp\pino-csharp.csproj" -- run main.pino --c
    ) else (
        dotnet run --project "%~dp0pino-csharp\pino-csharp.csproj" -- run main.pino
    )
    exit /b %errorlevel%
)

echo.
set /p choice="> Select a project number: "

:: Validate choice
if not defined proj[%choice%] (
    echo [Error] Invalid choice selection.
    exit /b 1
)

set "selected_project=!proj[%choice%]!"
echo.
echo Selected: !selected_project!
echo.
echo   [1] Run once
echo   [2] Watch for changes (auto-reload)
echo.
set /p mode="> Choose action: "

if "%mode%"=="1" (
    echo Running projects\!selected_project!\main.pino...
    if !use_c!==1 (
        dotnet run --project "%~dp0pino-csharp\pino-csharp.csproj" -- run "projects\!selected_project!\main.pino" --c
    ) else (
        dotnet run --project "%~dp0pino-csharp\pino-csharp.csproj" -- run "projects\!selected_project!\main.pino"
    )
) else if "%mode%"=="2" (
    echo Watching projects\!selected_project!\main.pino for changes...
    if !use_c!==1 (
        dotnet run --project "%~dp0pino-csharp\pino-csharp.csproj" -- watch "projects\!selected_project!\main.pino" --c
    ) else (
        dotnet run --project "%~dp0pino-csharp\pino-csharp.csproj" -- watch "projects\!selected_project!\main.pino"
    )
) else (
    echo [Error] Invalid action selected.
)
