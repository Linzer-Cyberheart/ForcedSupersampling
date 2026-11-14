@echo off
setlocal

rem Builds bin\ForcedSupersampling.exe with the C# compiler that ships with Windows.
rem No Visual Studio or SDK needed.

set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" (
    echo Could not find csc.exe at %CSC%
    exit /b 1
)

if not exist bin mkdir bin

"%CSC%" /nologo /target:winexe /optimize+ /codepage:65001 ^
    /out:bin\ForcedSupersampling.exe ^
    /win32icon:assets\fu_on.ico ^
    /reference:System.Windows.Forms.dll /reference:System.Drawing.dll ^
    /resource:assets\fu_on.ico,ForcedUpscaling.fu_on.ico ^
    /resource:assets\fu_off.ico,ForcedUpscaling.fu_off.ico ^
    /resource:assets\iLinzer_Sit.gif,ForcedUpscaling.iLinzer_Sit.gif ^
    /resource:assets\iLinzer_Cavando.gif,ForcedUpscaling.iLinzer_Cavando.gif ^
    src\*.cs

if errorlevel 1 exit /b 1
echo Built bin\ForcedSupersampling.exe
