@echo off
title Compilando SafeDisplay...
echo.
echo =======================================================
echo               COMPILANDO SAFEDISPLAY C#
echo =======================================================
echo.

set CSC_PATH=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if exist "%CSC_PATH%" goto compile

set CSC_PATH=C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe
if exist "%CSC_PATH%" goto compile

echo [ERROR] No se pudo encontrar csc.exe.
echo Asegurate de tener .NET Framework instalado.
pause
exit /b 1

:compile
echo Usando el compilador: %CSC_PATH%
echo Compilando SafeDisplay.cs...

"%CSC_PATH%" /target:winexe /win32icon:icon.ico /out:SafeDisplay_v2.exe SafeDisplay.cs /reference:System.dll,System.Drawing.dll,System.Windows.Forms.dll

if %ERRORLEVEL% equ 0 goto success

echo.
echo =======================================================
echo   [ERROR] Ocurrio un error al compilar.
echo =======================================================
echo.
pause
exit /b %ERRORLEVEL%

:success
echo.
echo =======================================================
echo   [OK] Compilacion exitosa!
echo   Se ha generado: SafeDisplay_v2.exe
echo =======================================================
echo.

