@echo off
title Stress VR - HR Monitor
setlocal

:: Portable launcher — every path is derived from this script's location.
:: %~dp0 = directory of this .bat (repo root), trailing backslash included.
set "ROOT=%~dp0"
set "ADB=%ROOT%platform-tools\adb.exe"
set "BRIDGE=%ROOT%hr_dashboard_v2.py"
set "LOG=%ROOT%bridge.log"

echo ================================================
echo  Stress VR - Heart Rate Monitor (portable)
echo  Root: %ROOT%
echo ================================================
echo.
echo  Provjeri:
echo  [1] Band 9 na ruci i spojen na telefon (BT)
echo  [2] Mi Fitness radi u pozadini na telefonu
echo  [3] Telefon i racunar na istoj WiFi mrezi
echo.

if not exist "%BRIDGE%" (
    echo  GRESKA: nije nadjen %BRIDGE%
    pause & exit /b 1
)

:: Kill old bridge instances (best effort)
taskkill /F /IM python.exe /FI "WINDOWTITLE eq Stress VR*" >nul 2>&1

:: ADB connect (bridge also scans/falls back on its own)
if exist "%ADB%" (
    "%ADB%" connect 192.168.1.222:5555 >nul 2>&1
) else (
    echo  NAPOMENA: %ADB% ne postoji - bridge ce pokusati adb sa PATH-a
)

echo  Pokrecem bridge... (log: %LOG%)
start "Stress VR HR Bridge" /B python -u "%BRIDGE%" > "%LOG%" 2>&1

timeout /t 4 /nobreak >nul
start "" "http://127.0.0.1:8888"

echo.
echo  Dashboard: http://127.0.0.1:8888
echo  Bridge nastavlja raditi u pozadini. Pritisni taster za zatvaranje prozora.
pause >nul
