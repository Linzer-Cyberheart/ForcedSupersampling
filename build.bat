@echo off
setlocal

rem Builds bin\ForcedSupersampling.exe. No Visual Studio project needed.
rem
rem It uses the Roslyn C# compiler that comes with Visual Studio or the Build Tools
rem (found through vswhere), with a deterministic build, a strong name and an explicit
rem x64 target. That combination matters: the same source built with the old csc.exe
rem that ships with Windows gets flagged by a few antivirus heuristics, and this one
rem does not. If Roslyn is not installed, the old compiler is used and a warning is shown.

set VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe
set CSC=
if exist "%VSWHERE%" (
    for /f "usebackq delims=" %%i in (`"%VSWHERE%" -latest -products * -requires Microsoft.Component.MSBuild -find MSBuild\**\Roslyn\csc.exe`) do set CSC=%%i
)
set EXTRA=-deterministic -keyfile:ForcedSupersampling.snk -platform:x64
if "%CSC%"=="" (
    echo WARNING: Roslyn csc.exe not found. Falling back to the compiler that ships with Windows.
    echo          The result works, but some antivirus heuristics flag that build. Install
    echo          "Build Tools for Visual Studio" to get the clean build.
    set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
    set EXTRA=-keyfile:ForcedSupersampling.snk -platform:x64
)
if not exist "%CSC%" (
    echo Could not find a C# compiler.
    exit /b 1
)
echo Compiler: %CSC%

if not exist bin mkdir bin

"%CSC%" -nologo -target:winexe -optimize+ -codepage:65001 %EXTRA% ^
    -out:bin\ForcedSupersampling.exe ^
    -win32icon:assets\fu_on.ico ^
    -reference:System.Windows.Forms.dll -reference:System.Drawing.dll ^
    -resource:assets\fu_on.ico,ForcedUpscaling.fu_on.ico ^
    -resource:assets\fu_off.ico,ForcedUpscaling.fu_off.ico ^
    -resource:assets\iLinzer_Sit.gif,ForcedUpscaling.iLinzer_Sit.gif ^
    -resource:assets\iLinzer_Cavando.gif,ForcedUpscaling.iLinzer_Cavando.gif ^
    src\*.cs

if errorlevel 1 exit /b 1
echo Built bin\ForcedSupersampling.exe
