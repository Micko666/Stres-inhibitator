@echo off
title SmartLine VR - HR Monitor

echo ================================================
echo  SmartLine VR - Heart Rate Monitor
echo ================================================
echo.
echo  Provjeri:
echo  [1] Band 9 na ruci i spojen na telefon BT
echo  [2] Mi Fitness radi u pozadini na telefonu
echo  [3] Telefon na WiFi 192.168.1.x
echo.
echo  Pokretanje...
echo.

:: Ubij stare instance
taskkill /F /IM python3.12.exe /T >nul 2>&1
timeout /t 1 /nobreak >nul

:: ADB connect
"C:\Users\Korisnik\Desktop\Diplomski\platform-tools\adb.exe" connect 192.168.1.222:5555 >nul 2>&1

:: Pokreni bridge u pozadini
start "" /B python -u "C:\Users\Korisnik\Desktop\Diplomski\hr_dashboard_v2.py" > "C:\Users\Korisnik\Desktop\Diplomski\bridge.log" 2>&1

:: Cekaj da Flask startuje
echo  Cekam da se pokrene server...
timeout /t 4 /nobreak >nul

:: Otvori dashboard u Chrome
echo  Otvaram dashboard...
start chrome "http://127.0.0.1:8888"

echo.
echo  Dashboard: http://127.0.0.1:8888
echo  Log:       C:\Users\Korisnik\Desktop\Diplomski\bridge.log
echo.
echo  Pritisni bilo koji taster za zatvaranje ovog prozora.
echo  (Bridge nastavlja raditi u pozadini)
pause >nul
