@echo off
setlocal
set SERVICE_NAME=PRODUCTOS_SINVENTA
sc stop "%SERVICE_NAME%"
sc delete "%SERVICE_NAME%"
echo Servicio desinstalado (o eliminado al completar el apagado).
pause