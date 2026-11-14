@echo off
rem Puts the primary monitor back to its normal resolution if Forced Supersampling
rem left it stuck. Rebooting does the same thing, nothing is ever saved.

powershell -NoProfile -ExecutionPolicy Bypass -Command "Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public static class R { [DllImport(\"user32.dll\", CharSet=CharSet.Unicode, EntryPoint=\"ChangeDisplaySettingsExW\")] public static extern int C(string d, IntPtr dm, IntPtr h, uint f, IntPtr l); }'; 'result (0 = ok): ' + [R]::C([NullString]::Value, [IntPtr]::Zero, [IntPtr]::Zero, 0, [IntPtr]::Zero)"

echo.
echo If the screen is still wrong, open Settings ^> System ^> Display and pick the resolution there.
pause
