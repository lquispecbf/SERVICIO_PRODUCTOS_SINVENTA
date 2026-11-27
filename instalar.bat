@echo off
setlocal

set SERVICE_NAME=PRODUCTOS_SINVENTA
set SERVICE_DISPLAY_NAME=CBF_ProductosSinVenta
set SERVICE_DESCRIPTION=Consulta diariamente productos sin venta y envia correo si la rutina y suscriptores estan habilitados.
set SERVICE_EXE=%~dp0PRODUCTOS_SINVENTA.exe

if not exist "%SERVICE_EXE%" (
    echo ERROR: No se encontro "%SERVICE_EXE%". Publique primero la aplicacion en esta carpeta.
    pause
    exit /b 1
)

sc stop "%SERVICE_NAME%" >nul 2>&1
sc delete "%SERVICE_NAME%" >nul 2>&1
sc create "%SERVICE_NAME%" binPath= "\"%SERVICE_EXE%\"" start= auto DisplayName= "%SERVICE_DISPLAY_NAME%"
if errorlevel 1 (
    echo ERROR: No se pudo crear el servicio. Ejecute este archivo como administrador.
    pause
    exit /b 1
)
sc description "%SERVICE_NAME%" "%SERVICE_DESCRIPTION%"
sc start "%SERVICE_NAME%"

echo.
echo Servicio instalado e iniciado. Verifique el appsettings.json publicado y el log de la primera ejecucion.
pause
