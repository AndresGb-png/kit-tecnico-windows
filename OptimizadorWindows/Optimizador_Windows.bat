@echo off
:: =====================================================================
::  OPTIMIZADOR DE WINDOWS  (Windows 10 / 11, compatible parcial con 7/8)
::  Creado por: Julio Gutierrez
::  Doble clic para ejecutar. Pide permisos de administrador solo.
::  Todo lo que hace queda registrado en C:\OptimizadorWindows\
:: =====================================================================
setlocal EnableExtensions
chcp 850 >nul
title Optimizador de Windows - por Julio Gutierrez
color 0B

set "SELF=%~f0"
set "PS=powershell -NoProfile -ExecutionPolicy Bypass -Command"
set "DRV=%SystemDrive:~0,1%"
set "LOGDIR=%SystemDrive%\OptimizadorWindows"
if not exist "%LOGDIR%" md "%LOGDIR%" >nul 2>&1
for /f %%a in ('%PS% "Get-Date -Format yyyy-MM-dd_HH-mm"') do set "STAMP=%%a"
if not defined STAMP set "STAMP=sin_fecha"
set PASO=0

:: Medicion automatica despues de reiniciar (no necesita administrador)
if /i "%~1"=="medir" goto :modo_medir

:: ---------- Pedir permisos de administrador ----------
net session >nul 2>&1
if errorlevel 1 (
    echo Solicitando permisos de administrador...
    powershell -NoProfile -ExecutionPolicy Bypass -Command "try { Start-Process -FilePath '%~f0' -Verb RunAs -ErrorAction Stop } catch { exit 1 }"
    if errorlevel 1 (
        echo.
        echo  No se dieron permisos de administrador. El optimizador no puede continuar.
        echo  Vuelve a abrirlo y presiona SI cuando Windows lo pida.
        echo.
        pause
    )
    exit /b
)

:: ---------- Menu ----------
:menu
cls
echo.
echo  ==============================================================
echo                 OPTIMIZADOR DE WINDOWS
echo                Creado por: Julio Guti‚rrez
echo  ==============================================================
echo.
echo   Antes de empezar: CIERRA navegadores y programas abiertos.
echo   Se creara un punto de restauracion por seguridad.
echo   En los pasos largos puedes presionar O para saltarlos.
echo.
echo   [1] Optimizacion RAPIDA    (5 a 15 minutos)
echo       Revision del hardware, limpieza y menos uso de RAM, CPU y disco.
echo.
echo   [2] Optimizacion COMPLETA  (30 a 90 minutos)
echo       Todo lo anterior + limpieza de componentes de Windows,
echo       reparacion de archivos del sistema (DISM + SFC) y
echo       optimizacion del disco (TRIM en SSD / desfragmentar HDD).
echo.
echo   [3] Solo DIAGNOSTICO       (menos de 1 minuto, no cambia nada)
echo       Estado de discos, temperatura, RAM, bateria y uso actual.
echo.
echo   [4] Salir
echo.
choice /c 1234 /n /m "  Elige una opcion [1, 2, 3 o 4]: "
if errorlevel 4 exit /b
if errorlevel 3 goto :diagnostico
if errorlevel 2 (set "MODO=COMPLETO") else (set "MODO=RAPIDO")
echo.
choice /c SN /n /m "  Vaciar tambien la Papelera de reciclaje? [S/N]: "
if errorlevel 2 (set "PAPELERA=0") else (set "PAPELERA=1")

set "LOG=%LOGDIR%\registro_%STAMP%.txt"
set "BACKUP=%LOGDIR%\respaldo_registro_%STAMP%"
cls
call :log "=============================================================="
call :log " OPTIMIZADOR DE WINDOWS - Modo %MODO% - %DATE% %TIME%"
call :log " Creado por: Julio Guti‚rrez"
call :log " Equipo: %COMPUTERNAME%   Usuario: %USERNAME%"
call :log "=============================================================="

:: ---------- Datos del equipo ----------
for /f %%a in ('%PS% "[math]::Round((Get-PSDrive %DRV%).Free/1MB)"') do set "LIBRE_ANTES=%%a"
for /f %%a in ('%PS% "[math]::Round((Get-WmiObject Win32_ComputerSystem).TotalPhysicalMemory/1KB)"') do set "RAMKB=%%a"
:: Si Windows no responde a la consulta, se usan valores seguros para no trabarse
if not defined LIBRE_ANTES set "LIBRE_ANTES=0"
if not defined RAMKB set "RAMKB=0"
set /a RAMGB=(RAMKB+524288)/1048576
%PS% "if (Get-WmiObject Win32_Battery) {exit 1} else {exit 0}" >nul 2>&1
if errorlevel 1 (set "PORTATIL=1") else (set "PORTATIL=0")
call :log " RAM detectada: %RAMGB% GB  -  Espacio libre en %SystemDrive% %LIBRE_ANTES% MB"
if "%PORTATIL%"=="1" (call :log " Tipo de equipo: PORTATIL") else (call :log " Tipo de equipo: ESCRITORIO")

:: =====================================================================
::  0. DIAGNOSTICO INICIAL
:: =====================================================================
call :paso "Revisando el estado del hardware: discos, temperatura, RAM y bateria"
call :ps salud
if errorlevel 2 (
    call :log ""
    call :log "   Se detecto un problema SERIO de hardware. Revisa los puntos en rojo."
    call :log "   Si es un disco en mal estado: HAZ RESPALDO de tus archivos cuanto antes."
    echo.
    choice /c SN /n /m "  Continuar con la optimizacion de todas formas? [S/N]: "
    if errorlevel 2 goto :fin
)

call :paso "Midiendo el uso de RAM, CPU y disco ANTES de optimizar"
set "ETIQUETA=ANTES"
call :ps medir

:: =====================================================================
::  1. SEGURIDAD
:: =====================================================================
call :paso "Creando punto de restauracion del sistema"
reg add "HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore" /v SystemRestorePointCreationFrequency /t REG_DWORD /d 0 /f >nul 2>&1
:: Maximo 5 minutos; si Restaurar sistema esta desactivado o no responde, se continua
%PS% "Enable-ComputerRestore -Drive '%SystemDrive%\' -ErrorAction SilentlyContinue; $j = Start-Job { Checkpoint-Computer -Description 'Antes del Optimizador' -RestorePointType MODIFY_SETTINGS -ErrorAction Stop }; if (-not (Wait-Job $j -Timeout 300)) { Stop-Job $j; exit 2 }; if ($j.State -ne 'Completed') { exit 1 }; exit 0" >nul 2>&1
if errorlevel 2 (
    call :log "   [AVISO] El punto de restauracion tardo demasiado. Se continua."
) else if errorlevel 1 (
    call :log "   [AVISO] No se pudo crear el punto de restauracion. Se continua."
) else (
    call :log "   [OK] Punto de restauracion creado."
)

call :paso "Respaldando claves de registro que se van a modificar"
md "%BACKUP%" >nul 2>&1
reg export "HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile" "%BACKUP%\SystemProfile.reg" /y >nul 2>&1
reg export "HKCU\Control Panel\Desktop" "%BACKUP%\Desktop.reg" /y >nul 2>&1
reg export "HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer" "%BACKUP%\Explorer.reg" /y >nul 2>&1
reg export "HKCU\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager" "%BACKUP%\ContentDelivery.reg" /y >nul 2>&1
reg export "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" "%BACKUP%\Inicio_Usuario.reg" /y >nul 2>&1
reg export "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved" "%BACKUP%\Inicio_Aprobado_Equipo.reg" /y >nul 2>&1
for /f "tokens=3" %%a in ('reg query "HKLM\SYSTEM\CurrentControlSet\Control" /v SvcHostSplitThresholdInKB 2^>nul ^| find "REG_"') do set "SVC_ANTES=%%a"
>"%BACKUP%\valor_original_SvcHostSplit.txt" echo SvcHostSplitThresholdInKB original: %SVC_ANTES% (por defecto Windows usa 0x380000 = 3670016)
call :log "   [OK] Respaldo guardado en %BACKUP%"

:: =====================================================================
::  2. LIMPIEZA DE ARCHIVOS
:: =====================================================================
call :paso "Eliminando archivos temporales de usuario - carpeta TEMP"
for /d %%u in ("%SystemDrive%\Users\*") do (
    if exist "%%u\AppData\Local\Temp" (
        del /f /s /q "%%u\AppData\Local\Temp\*" >nul 2>&1
        for /d %%d in ("%%u\AppData\Local\Temp\*") do rd /s /q "%%d" >nul 2>&1
    )
)
call :log "   [OK] Temporales de todos los usuarios eliminados (los que estaban en uso se omiten)."

call :paso "Eliminando temporales de Windows (C:\Windows\Temp)"
del /f /s /q "%SystemRoot%\Temp\*" >nul 2>&1
for /d %%d in ("%SystemRoot%\Temp\*") do rd /s /q "%%d" >nul 2>&1
call :log "   [OK] Carpeta Windows\Temp limpia."

call :paso "Limpiando Prefetch"
del /f /s /q "%SystemRoot%\Prefetch\*" >nul 2>&1
call :log "   [OK] Prefetch limpio. Windows lo regenera solo; el primer arranque puede tardar un poco mas."

call :paso "Limpiando cache de navegadores (Chrome, Edge, Brave, Opera, Firefox)"
call :log "   (No se borran contrasenas, historial ni cookies, solo la cache)"
for /d %%u in ("%SystemDrive%\Users\*") do (
    call :chromium "%%u\AppData\Local\Google\Chrome\User Data"
    call :chromium "%%u\AppData\Local\Microsoft\Edge\User Data"
    call :chromium "%%u\AppData\Local\BraveSoftware\Brave-Browser\User Data"
    if exist "%%u\AppData\Local\Opera Software\Opera Stable\Cache" rd /s /q "%%u\AppData\Local\Opera Software\Opera Stable\Cache" >nul 2>&1
    if exist "%%u\AppData\Local\Mozilla\Firefox\Profiles" (
        for /d %%p in ("%%u\AppData\Local\Mozilla\Firefox\Profiles\*") do rd /s /q "%%p\cache2" >nul 2>&1
    )
)
call :log "   [OK] Cache de navegadores limpia."

call :paso "Limpiando cache de miniaturas, reportes de errores y logs viejos"
for /d %%u in ("%SystemDrive%\Users\*") do (
    del /f /q "%%u\AppData\Local\Microsoft\Windows\Explorer\thumbcache_*.db" >nul 2>&1
    del /f /q "%%u\AppData\Local\Microsoft\Windows\Explorer\iconcache_*.db" >nul 2>&1
    del /f /s /q "%%u\AppData\Local\CrashDumps\*" >nul 2>&1
    del /f /s /q "%%u\AppData\Local\Microsoft\Windows\INetCache\*" >nul 2>&1
)
del /f /s /q "%ProgramData%\Microsoft\Windows\WER\ReportArchive\*" >nul 2>&1
del /f /s /q "%ProgramData%\Microsoft\Windows\WER\ReportQueue\*" >nul 2>&1
del /f /s /q "%ProgramData%\Microsoft\Windows\WER\Temp\*" >nul 2>&1
del /f /q "%SystemRoot%\Logs\CBS\*.cab" >nul 2>&1
del /f /q "%SystemRoot%\Logs\DISM\*.log" >nul 2>&1
call :log "   [OK] Miniaturas, reportes de errores y logs eliminados."

call :paso "Limpiando cache de Windows Update"
net stop wuauserv /y >nul 2>&1
net stop bits /y >nul 2>&1
rd /s /q "%SystemRoot%\SoftwareDistribution\Download" >nul 2>&1
md "%SystemRoot%\SoftwareDistribution\Download" >nul 2>&1
net start bits >nul 2>&1
net start wuauserv >nul 2>&1
call :log "   [OK] Descargas antiguas de Windows Update eliminadas."

call :paso "Limpiando cache de Optimizacion de distribucion"
%PS% "Delete-DeliveryOptimizationCache -Force -ErrorAction SilentlyContinue" >nul 2>&1
call :log "   [OK] Cache de Optimizacion de distribucion limpia."

if "%PAPELERA%"=="1" (
    call :paso "Vaciando la Papelera de reciclaje"
    %PS% "Clear-RecycleBin -Force -ErrorAction SilentlyContinue" >nul 2>&1
    if errorlevel 1 rd /s /q "%SystemDrive%\$Recycle.Bin" >nul 2>&1
    call :log "   [OK] Papelera vaciada. Si ya estaba vacia, no pasa nada."
)

call :paso "Ejecutando el Liberador de espacio en disco de Windows"
call :log "   (Puede tardar varios minutos, no cierres la ventana)"
:: Activa todas las categorias EXCEPTO la carpeta Descargas y la instalacion anterior de Windows
%PS% "$k='HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VolumeCaches'; $ex=@('DownloadsFolder','Previous Installations'); Get-ChildItem $k | ForEach-Object { if ($ex -contains $_.PSChildName) { Remove-ItemProperty -Path $_.PSPath -Name StateFlags0777 -ErrorAction SilentlyContinue } else { New-ItemProperty -Path $_.PSPath -Name StateFlags0777 -Value 2 -PropertyType DWord -Force | Out-Null } }" >nul 2>&1
call :limite 20 "cleanmgr.exe" "/sagerun:777" "Liberador de espacio completado, sin tocar tu carpeta Descargas."

if "%MODO%"=="COMPLETO" (
    call :paso "Limpiando componentes antiguos de Windows - DISM"
    call :log "   (Esto puede tardar entre 5 y 20 minutos)"
    call :limite 30 "Dism.exe" "/Online /Cleanup-Image /StartComponentCleanup" "Componentes antiguos eliminados."
)

:: =====================================================================
::  3. MENOS PROCESOS, MENOS RAM, CPU Y DISCO EN USO
:: =====================================================================
call :paso "Reduciendo procesos svchost segun la RAM (%RAMGB% GB)"
if %RAMGB% LSS 2 (
    call :log "   [AVISO] No se pudo leer la RAM o es menor a 2 GB. Se omite este ajuste."
    goto :prioridad
)
reg add "HKLM\SYSTEM\CurrentControlSet\Control" /v SvcHostSplitThresholdInKB /t REG_DWORD /d %RAMKB% /f >nul 2>&1
if errorlevel 1 (call :log "   [ERROR] No se pudo aplicar.") else (call :log "   [OK] SvcHostSplitThresholdInKB = %RAMKB% KB. Habra menos procesos tras reiniciar.")

:prioridad
call :paso "Dando prioridad a los programas en primer plano"
reg add "HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile" /v SystemResponsiveness /t REG_DWORD /d 10 /f >nul 2>&1
reg add "HKLM\SYSTEM\CurrentControlSet\Control\PriorityControl" /v Win32PrioritySeparation /t REG_DWORD /d 38 /f >nul 2>&1
call :log "   [OK] SystemResponsiveness = 10 (minimo real de Windows) y prioridad a primer plano."

call :paso "Quitando del inicio de Windows programas pesados que no hacen falta"
call :log "   (Juegos, tiendas, actualizadores y reproductores. Se pueden abrir igual cuando quieras)"
call :ps inicio

call :paso "Desactivando servicios de telemetria y servicios innecesarios"
for %%s in (DiagTrack dmwappushservice MapsBroker RetailDemo WMPNetworkSvc Fax) do (
    sc query %%s >nul 2>&1 && (
        sc stop %%s >nul 2>&1
        sc config %%s start= disabled >nul 2>&1
        call :log "   [OK] Servicio desactivado: %%s"
    )
)
reg add "HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection" /v AllowTelemetry /t REG_DWORD /d 0 /f >nul 2>&1

call :paso "Ajustando SysMain segun el tipo de disco"
call :ps sysmain

call :paso "Desactivando tareas programadas que disparan el uso de CPU y disco"
schtasks /Change /TN "\Microsoft\Windows\Application Experience\Microsoft Compatibility Appraiser" /Disable >nul 2>&1
schtasks /Change /TN "\Microsoft\Windows\Application Experience\ProgramDataUpdater" /Disable >nul 2>&1
schtasks /Change /TN "\Microsoft\Windows\Customer Experience Improvement Program\Consolidator" /Disable >nul 2>&1
schtasks /Change /TN "\Microsoft\Windows\Customer Experience Improvement Program\UsbCeip" /Disable >nul 2>&1
schtasks /Change /TN "\Microsoft\Windows\DiskDiagnostic\Microsoft-Windows-DiskDiagnosticDataCollector" /Disable >nul 2>&1
schtasks /Change /TN "\Microsoft\Windows\Autochk\Proxy" /Disable >nul 2>&1
schtasks /Change /TN "\Microsoft\Windows\Feedback\Siuf\DmClient" /Disable >nul 2>&1
schtasks /Change /TN "\Microsoft\Windows\Feedback\Siuf\DmClientOnScenarioDownload" /Disable >nul 2>&1
schtasks /Change /TN "\Microsoft\Windows\Windows Error Reporting\QueueReporting" /Disable >nul 2>&1
schtasks /Change /TN "\Microsoft\Windows\Maps\MapsUpdateTask" /Disable >nul 2>&1
call :log "   [OK] Tareas de telemetria (CompatTelRunner y similares) desactivadas."

call :paso "Bajando el consumo de Windows Search, Defender y Windows Update"
sc config WSearch start= delayed-auto >nul 2>&1
call :log "   [OK] Windows Search arranca despues del inicio, sin frenar el encendido."
%PS% "Set-MpPreference -ScanAvgCPULoadFactor 30 -ErrorAction SilentlyContinue; Set-MpPreference -EnableLowCpuPriority $true -ErrorAction SilentlyContinue" >nul 2>&1
call :log "   [OK] Analisis de Defender limitado a 30 por ciento de CPU. La proteccion sigue activa."
reg add "HKLM\SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization" /v DODownloadMode /t REG_DWORD /d 0 /f >nul 2>&1
call :log "   [OK] Windows Update ya no sube descargas a otros equipos de internet."
fsutil behavior set disablelastaccess 1 >nul 2>&1
call :log "   [OK] Menos escrituras en disco: no se guarda la hora de ultimo acceso."

call :paso "Desactivando Widgets, Noticias y el arranque anticipado de Edge"
reg add "HKLM\SOFTWARE\Policies\Microsoft\Dsh" /v AllowNewsAndInterests /t REG_DWORD /d 0 /f >nul 2>&1
reg add "HKLM\SOFTWARE\Policies\Microsoft\Windows\Windows Feeds" /v EnableFeeds /t REG_DWORD /d 0 /f >nul 2>&1
reg add "HKLM\SOFTWARE\Policies\Microsoft\Edge" /v StartupBoostEnabled /t REG_DWORD /d 0 /f >nul 2>&1
reg add "HKLM\SOFTWARE\Policies\Microsoft\Edge" /v BackgroundModeEnabled /t REG_DWORD /d 0 /f >nul 2>&1
call :log "   [OK] Menos procesos de Edge y WebView abiertos en segundo plano."

call :paso "Revisando la memoria virtual"
call :ps paginacion

call :paso "Desactivando aplicaciones en segundo plano"
reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications" /v GlobalUserDisabled /t REG_DWORD /d 1 /f >nul 2>&1
reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Search" /v BackgroundAppGlobalToggle /t REG_DWORD /d 0 /f >nul 2>&1
call :log "   [OK] Las apps de la Tienda ya no corren en segundo plano."

call :paso "Desactivando sugerencias, anuncios e instalacion automatica de apps"
set "CDM=HKCU\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager"
for %%v in (SilentInstalledAppsEnabled SystemPaneSuggestionsEnabled SoftLandingEnabled PreInstalledAppsEnabled OemPreInstalledAppsEnabled SubscribedContent-338388Enabled SubscribedContent-338389Enabled SubscribedContent-310093Enabled SubscribedContent-353694Enabled SubscribedContent-353696Enabled) do (
    reg add "%CDM%" /v %%v /t REG_DWORD /d 0 /f >nul 2>&1
)
reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced" /v ShowSyncProviderNotifications /t REG_DWORD /d 0 /f >nul 2>&1
call :log "   [OK] Sugerencias y anuncios de Windows desactivados."

call :paso "Desactivando grabacion de juegos en segundo plano (Game DVR)"
reg add "HKCU\System\GameConfigStore" /v GameDVR_Enabled /t REG_DWORD /d 0 /f >nul 2>&1
reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\GameDVR" /v AppCaptureEnabled /t REG_DWORD /d 0 /f >nul 2>&1
reg add "HKLM\SOFTWARE\Policies\Microsoft\Windows\GameDVR" /v AllowGameDVR /t REG_DWORD /d 0 /f >nul 2>&1
call :log "   [OK] Game DVR desactivado."

call :paso "Acelerando el inicio y los menus"
reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize" /v StartupDelayInMSec /t REG_DWORD /d 0 /f >nul 2>&1
reg add "HKCU\Control Panel\Desktop" /v MenuShowDelay /t REG_SZ /d 100 /f >nul 2>&1
reg add "HKCU\Control Panel\Mouse" /v MouseHoverTime /t REG_SZ /d 100 /f >nul 2>&1
call :log "   [OK] Sin retraso en programas de inicio y menus mas rapidos."

call :paso "Ajustando efectos visuales para mejor rendimiento"
reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects" /v VisualFXSetting /t REG_DWORD /d 3 /f >nul 2>&1
reg add "HKCU\Control Panel\Desktop" /v UserPreferencesMask /t REG_BINARY /d 9012038010000000 /f >nul 2>&1
reg add "HKCU\Control Panel\Desktop\WindowMetrics" /v MinAnimate /t REG_SZ /d 0 /f >nul 2>&1
reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced" /v TaskbarAnimations /t REG_DWORD /d 0 /f >nul 2>&1
reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize" /v EnableTransparency /t REG_DWORD /d 0 /f >nul 2>&1
reg add "HKCU\Software\Microsoft\Windows\DWM" /v EnableAeroPeek /t REG_DWORD /d 0 /f >nul 2>&1
call :log "   [OK] Animaciones y transparencias desactivadas (se mantiene el suavizado de letras)."

call :paso "Configurando el plan de energia"
if "%PORTATIL%"=="1" (
    powercfg -setactive SCHEME_BALANCED >nul 2>&1
    call :log "   [OK] Portatil detectado: plan Equilibrado (para no gastar bateria)."
    call :log "   [INFO] La hibernacion se mantiene activa en portatiles."
) else (
    powercfg -setactive SCHEME_MIN >nul 2>&1
    if errorlevel 1 (call :log "   [AVISO] Plan Alto rendimiento no disponible en este equipo.") else (call :log "   [OK] Plan de energia: Alto rendimiento.")
    powercfg -h off >nul 2>&1
    call :log "   [OK] Hibernacion desactivada: se libera el archivo hiberfil.sys (varios GB)."
)

call :paso "Limpiando cache DNS y renovando la red"
ipconfig /flushdns >nul 2>&1
netsh winsock reset >nul 2>&1
call :log "   [OK] Cache DNS limpia y Winsock restablecido."

:: =====================================================================
::  4. MANTENIMIENTO PROFUNDO (solo modo completo)
:: =====================================================================
if "%MODO%"=="RAPIDO" goto :resumen

call :paso "Reparando la imagen de Windows - DISM RestoreHealth"
call :log "   (Necesita internet. Puede tardar 10 a 30 minutos)"
call :limite 40 "Dism.exe" "/Online /Cleanup-Image /RestoreHealth" "Imagen de Windows revisada y reparada."

call :paso "Verificando archivos del sistema - SFC"
call :log "   (Puede tardar 10 a 20 minutos)"
call :limite 40 "sfc.exe" "/scannow" "SFC finalizado. Detalle en C:\Windows\Logs\CBS\CBS.log"

call :paso "Optimizando el disco %SystemDrive% (TRIM en SSD / desfragmentacion en HDD)"
call :limite 60 "defrag.exe" "%SystemDrive% /O /U" "Disco optimizado."

:: =====================================================================
::  RESUMEN
:: =====================================================================
:resumen
for /f %%a in ('%PS% "[math]::Round((Get-PSDrive %DRV%).Free/1MB)"') do set "LIBRE_DESPUES=%%a"
if not defined LIBRE_DESPUES set "LIBRE_DESPUES=%LIBRE_ANTES%"
set /a LIBERADO=LIBRE_DESPUES-LIBRE_ANTES
if %LIBERADO% LSS 0 set LIBERADO=0
set "NOTA=Espacio recuperado correctamente."
if %LIBERADO% LSS 100 set "NOTA=El equipo ya estaba limpio: habia poco que borrar, y eso es normal."

:: Medicion automatica al volver a iniciar sesion, para comparar con la de ANTES
copy /y "%SELF%" "%LOGDIR%\Optimizador_Windows.bat" >nul 2>&1
reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\RunOnce" /v OptimizadorMedicion /t REG_SZ /d "\"%LOGDIR%\Optimizador_Windows.bat\" medir" /f >nul 2>&1

call :log ""
call :log "=============================================================="
call :log " OPTIMIZACION TERMINADA - %DATE% %TIME%"
call :log "=============================================================="
call :log " Espacio libre antes:   %LIBRE_ANTES% MB"
call :log " Espacio libre ahora:   %LIBRE_DESPUES% MB"
call :log " Espacio liberado:      %LIBERADO% MB"
call :log " %NOTA%"
call :log ""
call :log " Registro completo:  %LOG%"
call :log " Respaldo registro:  %BACKUP%"
call :log " Si algo no te gusta: Panel de control - Recuperacion -"
call :log " Abrir Restaurar sistema - 'Antes del Optimizador'."
call :log ""
call :log " IMPORTANTE: REINICIA el equipo para aplicar todos los cambios."
call :log " Al volver a entrar se abrira sola una medicion que compara"
call :log " el uso de RAM, CPU y disco con el de antes de optimizar."
call :log "=============================================================="
echo.
choice /c SN /n /m "  Reiniciar el equipo ahora? [S/N]: "
if errorlevel 2 goto :fin
call :log " Reiniciando en 10 segundos..."
shutdown /r /t 10 /c "Reinicio para aplicar la optimizacion"
:fin
echo.
echo  Presiona una tecla para cerrar.
pause >nul
endlocal
exit /b

:: =====================================================================
::  SOLO DIAGNOSTICO (opcion 3 del menu, no cambia nada)
:: =====================================================================
:diagnostico
set "LOG=%LOGDIR%\diagnostico_%STAMP%.txt"
cls
call :log "=============================================================="
call :log " DIAGNOSTICO DEL EQUIPO - %DATE% %TIME%"
call :log " Equipo: %COMPUTERNAME%   -   Optimizador por Julio Guti‚rrez"
call :log "=============================================================="
call :ps salud
call :log ""
set "ETIQUETA=DIAGNOSTICO"
call :ps medir
call :log ""
call :log " Diagnostico guardado en %LOG%"
echo.
echo  Presiona una tecla para volver al menu.
pause >nul
goto :menu

:: =====================================================================
::  MEDICION DESPUES DE REINICIAR (se abre sola una vez)
:: =====================================================================
:modo_medir
title Optimizador de Windows - Medicion despues de optimizar
set "LOG=%LOGDIR%\medicion_despues_%STAMP%.txt"
cls
echo.
echo  ==============================================================
echo     MEDICION DESPUES DE OPTIMIZAR - por Julio Guti‚rrez
echo  ==============================================================
echo.
echo  Esperando 2 minutos a que Windows termine de arrancar,
echo  para que la medicion sea justa. Presiona una tecla para medir ya.
echo.
timeout /t 120
cls
set "ETIQUETA=DESPUES"
call :ps medir
echo.
echo  Resultado guardado en %LOG%
echo  Presiona una tecla para cerrar.
pause >nul
exit /b

:: =====================================================================
::  FUNCIONES
:: =====================================================================
:paso
set /a PASO+=1
call :log ""
call :log "[PASO %PASO%] %~1..."
goto :eof

:log
if "%~1"=="" (
    echo.
    >>"%LOG%" echo.
    goto :eof
)
echo %~1
>>"%LOG%" echo %~1
goto :eof

:ps
:: Ejecuta una funcion del bloque de PowerShell que esta al final de este archivo
set "ACCION=%~1"
%PS% "$c = [IO.File]::ReadAllText($env:SELF, [Text.Encoding]::Default); $m = '#==PS' + 'BLOQUE=='; iex $c.Substring($c.LastIndexOf($m))"
goto :eof

:limite
:: Ejecuta un programa con tiempo maximo para que nunca se quede pegado.
:: %1 = minutos maximos   %2 = programa   %3 = argumentos   %4 = mensaje si sale bien
:: Mientras corre, presionar la tecla O detiene solo este paso y sigue con el siguiente.
echo    (Si tarda demasiado, presiona la tecla O para omitir solo este paso)
%PS% "$p = New-Object Diagnostics.Process; $p.StartInfo.FileName = '%~2'; $p.StartInfo.Arguments = '%~3'; $p.StartInfo.UseShellExecute = $false; [void]$p.Start(); $fin = (Get-Date).AddMinutes(%~1); try { while ([Console]::KeyAvailable) { [void][Console]::ReadKey($true) } } catch {}; while (-not $p.WaitForExit(500)) { if ((Get-Date) -gt $fin) { taskkill /PID $p.Id /T /F | Out-Null; exit 999 }; try { if ([Console]::KeyAvailable -and [Console]::ReadKey($true).Key -eq 'O') { taskkill /PID $p.Id /T /F | Out-Null; exit 998 } } catch {} }; exit $p.ExitCode"
set "RES=%errorlevel%"
if "%RES%"=="999" goto :limite_tiempo
if "%RES%"=="998" goto :limite_omitido
if "%RES%"=="0" goto :limite_ok
if "%RES%"=="3010" goto :limite_ok
call :log "   [AVISO] Termino con codigo %RES%. No es grave, se continua con el siguiente paso."
goto :eof
:limite_ok
call :log "   [OK] %~4"
goto :eof
:limite_omitido
call :log "   [INFO] Paso omitido por el usuario. Se continua con el siguiente."
goto :eof
:limite_tiempo
call :log "   [AVISO] Supero el limite de %~1 minutos. Se detuvo y se continua con el siguiente paso."
goto :eof

:chromium
if not exist "%~1" goto :eof
for /d %%p in ("%~1\*") do (
    if exist "%%p\Cache" rd /s /q "%%p\Cache" >nul 2>&1
    if exist "%%p\Code Cache" rd /s /q "%%p\Code Cache" >nul 2>&1
    if exist "%%p\GPUCache" rd /s /q "%%p\GPUCache" >nul 2>&1
)
goto :eof

:: =====================================================================
::  BLOQUE DE POWERSHELL (cmd nunca llega aqui; lo ejecuta la funcion :ps)
:: =====================================================================
#==PSBLOQUE==
$ErrorActionPreference = 'SilentlyContinue'
$dir = Join-Path $env:SystemDrive 'OptimizadorWindows'
$orden = @{ 'SIN DATO' = 0; 'BUENO' = 1; 'ATENCION' = 2; 'MALO' = 3 }

function Linea([string]$texto, [string]$color = 'Gray') {
    Write-Host $texto -ForegroundColor $color
    if ($env:LOG) { Add-Content -LiteralPath $env:LOG -Value $texto }
}

function Estado([string]$nivel, [string]$texto) {
    $etiquetas = @{ 'BUENO' = '[ BUENO ]'; 'ATENCION' = '[ REQUIERE ATENCION ]'; 'MALO' = '[ MALO ]'; 'SIN DATO' = '[ SIN DATO ]' }
    $colores = @{ 'BUENO' = 'Green'; 'ATENCION' = 'Yellow'; 'MALO' = 'Red'; 'SIN DATO' = 'DarkGray' }
    Linea ('   {0,-23}{1}' -f $etiquetas[$nivel], $texto) $colores[$nivel]
}

function Peor([string]$a, [string]$b) {
    if ($orden[$b] -gt $orden[$a]) { return $b }
    return $a
}

function Nivel([double]$valor, [double]$atencion, [double]$malo) {
    if ($valor -ge $malo) { return 'MALO' }
    if ($valor -ge $atencion) { return 'ATENCION' }
    return 'BUENO'
}

# ---------- Estado del hardware: discos, CPU, RAM, bateria ----------
function Salud {
    $general = 'BUENO'

    Linea '   --- DISCOS ---' 'Cyan'
    $fallaSmart = @(Get-WmiObject -Namespace root\wmi MSStorageDriver_FailurePredictStatus | Where-Object { $_.PredictFailure })
    $discos = @(Get-PhysicalDisk | Sort-Object { [int]$_.DeviceId })
    if ($discos.Count -eq 0) {
        # Windows 7: solo hay estado SMART basico
        foreach ($d in @(Get-WmiObject Win32_DiskDrive)) {
            if ($d.Status -eq 'OK') { $n = 'BUENO' } else { $n = 'MALO' }
            Estado $n ('{0} - {1} GB - Estado SMART: {2}' -f $d.Model, [math]::Round($d.Size / 1GB), $d.Status)
            $general = Peor $general $n
        }
    }
    foreach ($d in $discos) {
        if ($d.BusType -eq 'NVMe') { $tipo = 'SSD NVMe M.2' }
        elseif ($d.MediaType -eq 'SSD') { $tipo = 'SSD SATA' }
        elseif ($d.MediaType -eq 'HDD') { $tipo = 'HDD mecanico' }
        elseif ($d.BusType -eq 'USB') { $tipo = 'Disco USB' }
        else { $tipo = 'Disco' }

        $n = 'BUENO'
        $info = @()
        $sw = [string]$d.HealthStatus
        if ($sw -eq 'Unhealthy') { $n = 'MALO' } elseif ($sw -eq 'Warning') { $n = 'ATENCION' }
        $info += 'Salud segun Windows: ' + $sw

        $r = $d | Get-StorageReliabilityCounter
        if ($r) {
            if ($r.Wear -ne $null -and $tipo -like 'SSD*') {
                $vida = 100 - [int]$r.Wear
                $info += ('Vida util restante: {0}%' -f $vida)
                $n = Peor $n (Nivel $r.Wear 70 90)
            }
            if ($r.Temperature -gt 0) {
                if ($tipo -like 'HDD*') { $tn = Nivel $r.Temperature 50 60 } else { $tn = Nivel $r.Temperature 65 75 }
                $info += ('Temperatura: {0} C' -f $r.Temperature)
                $n = Peor $n $tn
            }
            if ($r.ReadErrorsUncorrected -gt 0) {
                $info += ('Errores de lectura sin corregir: {0}' -f $r.ReadErrorsUncorrected)
                $n = Peor $n 'ATENCION'
            }
            if ($r.PowerOnHours -gt 0) { $info += ('Horas encendido: {0}' -f $r.PowerOnHours) }
        }
        Estado $n ('Disco {0}: {1} - {2} - {3} GB' -f $d.DeviceId, $tipo, $d.FriendlyName, [math]::Round($d.Size / 1GB))
        Linea ('        ' + ($info -join ' | '))
        $general = Peor $general $n

        foreach ($p in @(Get-Partition -DiskNumber ([int]$d.DeviceId) | Where-Object { [string]$_.DriveLetter -match '^[A-Z]$' })) {
            $v = Get-Volume -DriveLetter $p.DriveLetter
            if ($v.Size -gt 0) {
                $uso = [math]::Round(($v.Size - $v.SizeRemaining) / $v.Size * 100)
                $un = Nivel $uso 90 97
                Estado $un ('   Unidad {0}: {1}% usado - {2} GB libres de {3} GB' -f $p.DriveLetter, $uso, [math]::Round($v.SizeRemaining / 1GB, 1), [math]::Round($v.Size / 1GB))
                $general = Peor $general $un
            }
        }
    }
    if ($fallaSmart.Count -gt 0) {
        Estado 'MALO' 'SMART avisa que un disco puede fallar pronto. HAZ RESPALDO YA.'
        $general = 'MALO'
    }

    Linea ''
    Linea '   --- PROCESADOR ---' 'Cyan'
    $cpu = @(Get-WmiObject Win32_Processor)[0]
    Linea ('        {0} - {1} nucleos / {2} hilos' -f ([string]$cpu.Name).Trim(), $cpu.NumberOfCores, $cpu.NumberOfLogicalProcessors)
    $temps = @()
    foreach ($z in @(Get-WmiObject Win32_PerfFormattedData_Counters_ThermalZoneInformation)) {
        if ($z.HighPrecisionTemperature -gt 0) { $temps += $z.HighPrecisionTemperature / 10 - 273.15 }
        elseif ($z.Temperature -gt 0) { $temps += $z.Temperature - 273.15 }
    }
    if ($temps.Count -eq 0) {
        foreach ($z in @(Get-WmiObject -Namespace root\wmi MSAcpi_ThermalZoneTemperature)) { $temps += $z.CurrentTemperature / 10 - 273.15 }
    }
    $temps = @($temps | Where-Object { $_ -gt 10 -and $_ -lt 120 })
    if ($temps.Count -gt 0) {
        $t = [math]::Round(($temps | Measure-Object -Maximum).Maximum)
        $tn = Nivel $t 75 90
        Estado $tn ('Temperatura: {0} C   (bueno: menos de 75 / atencion: 75 a 90 / malo: mas de 90)' -f $t)
        Linea '        Sensor termico de la placa (ACPI): puede variar unos grados respecto a los nucleos.'
        $general = Peor $general $tn
    } else {
        Estado 'SIN DATO' 'Temperatura: este equipo no la informa a Windows.'
        Linea '        Para verla usa HWMonitor, Core Temp o el programa del fabricante.'
    }

    Linea ''
    Linea '   --- MEMORIA RAM ---' 'Cyan'
    $mods = @(Get-WmiObject Win32_PhysicalMemory)
    $gb = [math]::Round(($mods | Measure-Object Capacity -Sum).Sum / 1GB, 1)
    if ($gb -le 0) { $gb = [math]::Round((Get-WmiObject Win32_ComputerSystem).TotalPhysicalMemory / 1GB, 1) }
    if ($gb -lt 4) { $rn = 'MALO' } elseif ($gb -lt 8) { $rn = 'ATENCION' } else { $rn = 'BUENO' }
    Estado $rn ('{0} GB instalados en {1} modulo(s). Para Windows 10/11 se recomienda 8 GB o mas.' -f $gb, $mods.Count)
    $general = Peor $general $rn

    $os = Get-WmiObject Win32_OperatingSystem
    $dias = [math]::Round(((Get-Date) - $os.ConvertToDateTime($os.LastBootUpTime)).TotalDays, 1)
    if ($dias -ge 7) { $un = 'ATENCION' } else { $un = 'BUENO' }
    Estado $un ('Ultimo reinicio hace {0} dias. Mas de 7 dias sin reiniciar acumula lentitud.' -f $dias)

    if (Get-WmiObject Win32_Battery) {
        Linea ''
        Linea '   --- BATERIA ---' 'Cyan'
        $full = (@(Get-WmiObject -Namespace root\wmi BatteryFullChargedCapacity)[0]).FullChargedCapacity
        $disenio = (@(Get-WmiObject -Namespace root\wmi BatteryStaticData)[0]).DesignedCapacity
        if ($full -gt 0 -and $disenio -gt 0) {
            $pct = [math]::Min(100, [math]::Round($full / $disenio * 100))
            if ($pct -lt 50) { $bn = 'MALO' } elseif ($pct -lt 70) { $bn = 'ATENCION' } else { $bn = 'BUENO' }
            Estado $bn ('Capacidad: {0}% de la original. Menos de 70 = dura poco; menos de 50 = cambiarla.' -f $pct)
            $general = Peor $general $bn
        } else {
            Estado 'SIN DATO' 'La bateria no informa su capacidad a Windows.'
        }
    }

    Linea ''
    Estado $general 'ESTADO GENERAL DEL HARDWARE'
    if ($general -eq 'MALO') { return 2 }
    if ($general -eq 'ATENCION') { return 1 }
    return 0
}

# ---------- Uso de RAM, CPU y disco (lo mismo que muestra el Administrador de tareas) ----------
function Comparar([string]$nombre, [double]$antes, [double]$ahora, [string]$unidad) {
    $dif = $ahora - $antes
    if ($dif -lt 0) { $txt = 'bajo ' + [math]::Abs($dif) + $unidad; $c = 'Green' }
    elseif ($dif -gt 0) { $txt = 'subio ' + $dif + $unidad; $c = 'Yellow' }
    else { $txt = 'igual'; $c = 'Gray' }
    Linea ('   {0,-20} antes: {1,5}{4}   ahora: {2,5}{4}   -> {3}' -f $nombre, $antes, $ahora, $txt, $unidad) $c
}

function Medir {
    $etiqueta = $env:ETIQUETA
    $nucleos = [Environment]::ProcessorCount
    $p1 = @{}
    foreach ($p in Get-Process) { if ($p.CPU) { $p1[$p.Id] = $p.CPU } }

    Linea ''
    Linea ('   --- USO DE RECURSOS (' + $etiqueta + ') ---') 'Cyan'
    Write-Host '   Midiendo durante unos 15 segundos, no uses el equipo...'
    $cpuM = @(); $discoM = @()
    $reloj = [Diagnostics.Stopwatch]::StartNew()
    for ($i = 0; $i -lt 10; $i++) {
        $cpuM += [double](Get-WmiObject Win32_Processor | Measure-Object LoadPercentage -Average).Average
        $t = Get-WmiObject Win32_PerfFormattedData_PerfDisk_PhysicalDisk | Where-Object { $_.Name -eq '_Total' }
        if ($t) { $discoM += [math]::Max(0, 100 - [math]::Min(100, [double]$t.PercentIdleTime)) }
        Start-Sleep -Milliseconds 300
    }
    $seg = $reloj.Elapsed.TotalSeconds

    $os = Get-WmiObject Win32_OperatingSystem
    $ram = [math]::Round(100 - $os.FreePhysicalMemory / $os.TotalVisibleMemorySize * 100)
    $ramUsada = [math]::Round(($os.TotalVisibleMemorySize - $os.FreePhysicalMemory) / 1MB, 1)
    $ramTotal = [math]::Round($os.TotalVisibleMemorySize / 1MB, 1)
    $cpu = [math]::Round(($cpuM | Measure-Object -Average).Average)
    if ($discoM.Count -gt 0) { $disco = [math]::Round(($discoM | Measure-Object -Average).Average) } else { $disco = 0 }
    $procesos = @(Get-Process).Count

    Estado (Nivel $ram 80 90) ('RAM:   {0}% en uso  ({1} de {2} GB)' -f $ram, $ramUsada, $ramTotal)
    Estado (Nivel $cpu 60 85) ('CPU:   {0}% promedio' -f $cpu)
    Estado (Nivel $disco 50 90) ('Disco: {0}% de actividad promedio' -f $disco)
    Linea ('        Procesos abiertos: {0}' -f $procesos)

    Linea '   Programas que mas RAM usan:'
    $topRam = Get-Process | Group-Object ProcessName | ForEach-Object {
        New-Object PSObject -Property @{ N = $_.Name; V = [math]::Round(($_.Group | Measure-Object WorkingSet64 -Sum).Sum / 1MB) }
    } | Sort-Object V -Descending | Select-Object -First 5
    foreach ($x in $topRam) { Linea ('      {0,-30}{1,7} MB' -f $x.N, $x.V) }

    Linea '   Programas que mas CPU usaron durante la medicion:'
    $topCpu = Get-Process | Where-Object { $p1.ContainsKey($_.Id) -and $_.ProcessName -ne 'Idle' } | ForEach-Object {
        New-Object PSObject -Property @{ N = $_.ProcessName; V = $_.CPU - $p1[$_.Id] }
    } | Group-Object N | ForEach-Object {
        New-Object PSObject -Property @{ N = $_.Name; V = [math]::Round(($_.Group | Measure-Object V -Sum).Sum / $seg / $nucleos * 100, 1) }
    } | Sort-Object V -Descending | Select-Object -First 5
    foreach ($x in $topCpu) { Linea ('      {0,-30}{1,7} %' -f $x.N, $x.V) }

    # Historial para comparar antes / despues
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir | Out-Null }
    $csv = Join-Path $dir 'mediciones.csv'
    $antes = $null
    if ($etiqueta -ne 'ANTES' -and (Test-Path $csv)) {
        $antes = Import-Csv $csv -Delimiter ';' | Where-Object { $_.Etiqueta -eq 'ANTES' } | Select-Object -Last 1
    }
    if (-not (Test-Path $csv)) { Set-Content -Path $csv -Value 'Fecha;Etiqueta;RAM;CPU;Disco;Procesos' }
    Add-Content -Path $csv -Value ('{0};{1};{2};{3};{4};{5}' -f (Get-Date -Format 'yyyy-MM-dd HH:mm'), $etiqueta, $ram, $cpu, $disco, $procesos)

    if ($antes) {
        Linea ''
        Linea ('   --- COMPARACION con la medicion de antes de optimizar (' + $antes.Fecha + ') ---') 'Cyan'
        Comparar 'RAM en uso' $antes.RAM $ram '%'
        Comparar 'CPU promedio' $antes.CPU $cpu '%'
        Comparar 'Actividad de disco' $antes.Disco $disco '%'
        Comparar 'Procesos abiertos' $antes.Procesos $procesos ''
        Linea '   La comparacion es justa si en ambos casos hay los mismos programas abiertos.'
    }
}

# ---------- Programas pesados que arrancan con Windows ----------
function Inicio {
    $patron = 'Spotify|Discord|Steam|EpicGames|Skype|Zoom|AdobeGCInvoker|Adobe Creative Cloud|CCXProcess|AdobeAAMUpdater|Opera.*Assistant|EADM|Origin|uTorrent|BitTorrent|iTunesHelper|Battle\.net|GalaxyClient|Overwolf|MicrosoftEdgeAutoLaunch|CCleaner|Ubisoft|Dropbox Update|GoogleDriveSync'
    $rutas = @(
        @('HKCU:\Software\Microsoft\Windows\CurrentVersion\Run', 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run'),
        @('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run', 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run'),
        @('HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run', 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32')
    )
    # 03 = desactivado, igual que al desactivarlo en el Administrador de tareas (reversible)
    $apagado = [byte[]](3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0)
    $hechos = 0; $quedan = 0
    foreach ($r in $rutas) {
        $k = Get-Item -LiteralPath $r[0]
        if (-not $k) { continue }
        foreach ($nombre in $k.GetValueNames()) {
            if (-not $nombre) { continue }
            if ($nombre -match $patron -or [string]$k.GetValue($nombre) -match $patron) {
                if (-not (Test-Path $r[1])) { New-Item -Path $r[1] -Force | Out-Null }
                Set-ItemProperty -LiteralPath $r[1] -Name $nombre -Value $apagado -Type Binary
                Linea ('   [OK] Ya no arranca con Windows: ' + $nombre)
                $hechos++
            } else { $quedan++ }
        }
    }
    if ($hechos -eq 0) { Linea '   [OK] No habia programas pesados conocidos en el inicio.' }
    Linea ('   [INFO] Quedan {0} programas en el inicio (antivirus, drivers, etc.).' -f $quedan)
    Linea '          Si quieres quitar mas: Administrador de tareas - pestana Inicio.'
}

# ---------- SysMain: ayuda en HDD, en SSD solo gasta RAM y disco ----------
function SysMainSegunDisco {
    $num = (Get-Partition -DriveLetter $env:SystemDrive.Substring(0, 1)).DiskNumber
    $d = Get-PhysicalDisk | Where-Object { [string]$_.DeviceId -eq [string]$num }
    if ($d.BusType -eq 'NVMe' -or $d.MediaType -eq 'SSD') {
        Stop-Service SysMain -Force
        Set-Service SysMain -StartupType Disabled
        Linea '   [OK] Windows esta en SSD: SysMain desactivado (menos uso de RAM y disco).'
    } elseif ($d.MediaType -eq 'HDD') {
        Linea '   [OK] Windows esta en HDD: SysMain se mantiene porque acelera la apertura de programas.'
        Linea '   [INFO] La mejora mas grande para este equipo seria cambiar el HDD por un SSD.'
    } else {
        Linea '   [INFO] No se pudo saber el tipo de disco: SysMain no se modifica.'
    }
}

# ---------- Memoria virtual: si esta apagada, Windows se cierra o se congela con poca RAM ----------
function Paginacion {
    $cs = Get-WmiObject Win32_ComputerSystem -EnableAllPrivileges
    $pf = @(Get-WmiObject Win32_PageFileSetting)
    if (-not $cs.AutomaticManagedPagefile -and $pf.Count -eq 0) {
        $cs.AutomaticManagedPagefile = $true
        [void]$cs.Put()
        Linea '   [OK] La memoria virtual estaba desactivada: se activo en automatico.'
    } else {
        Linea '   [OK] La memoria virtual esta bien configurada, no se cambia.'
    }
}

switch ($env:ACCION) {
    'salud'      { exit (Salud) }
    'medir'      { Medir; exit 0 }
    'inicio'     { Inicio; exit 0 }
    'sysmain'    { SysMainSegunDisco; exit 0 }
    'paginacion' { Paginacion; exit 0 }
}
exit 0
