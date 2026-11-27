# PRODUCTOS_SINVENTA

Servicio .NET 8 para Windows. Ejecuta `dbo.SP_BUSCAR_PRODUCTOS_LOTES_SIN_VENTAS`, agrupa los resultados por código de artículo, registra el reporte y envía correo/notificaciones de intranet según la rutina y sus canales habilitados.

## Configuración

- `ConfiguracionSAP.CompanyDB`: esquema de SAP HANA.
- `ConfiguracionSAP.DiasAtras`: valor local de respaldo si no se logra leer la configuración de intranet.
- `ConfiguracionSQLServer.CadenaConexion`: base SQL Server que contiene los procedimientos y tablas de alertas.
- `Programacion.HoraEjecucion`: hora local del equipo, formato `HH:mm`.
- `Programacion.EjecutarAhora`: `false` en operación diaria.
- `ConfiguracionRutina.CodigoRutina`: código de rutina en `dbo.ALERTA_RUTINA`, por ejemplo `PRODUCTOS_SINVENTA`.
- `SmtpSettings`: configuración SMTP.
- `ConfiguracionLogs.Ruta`: carpeta de logs relativa al ejecutable o ruta absoluta.

## Días y recarga de configuración de intranet

El valor activo de días se lee de `ALERTA_RUTINA.DIAS_ATRAS` (el campo `txtDiasAtras` de intranet) y se envía al query como `@DIAS_ATRAS`. `ConfiguracionSAP.DiasAtras` se conserva como valor de respaldo. Define el período sin ventas a nivel de código de artículo: se incluye una fila por producto con stock en `ALM-01` si no tiene ventas por factura ni entrega entre la fecha de referencia menos esos días y la fecha de referencia. El stock total se toma de `OITW.OnHand`, que consolida las cantidades del artículo en el almacén, sin separar lotes. El correo muestra la unidad de medida de compra (`OITM.BuyUnitMsr`), las cajas calculadas como `OnHand / OITM.NumInBuy`, el costo por caja como `OITW.AvgPrice * OITM.NumInBuy` y la valorización como cajas por costo por caja. Si `NumInBuy` no es positivo, las cifras de cajas, costo y valorización aparecen como `N/D`.

El servicio consulta `dbo.SP_OBTENER_VERSION_CONFIG_ALERTAS` cada tres segundos. Si `VERSION_CONFIG` cambia, vuelve a leer `dbo.SP_OBTENER_CONFIG_RUTINA` y actualiza días, estado habilitado y canales en memoria. Esto recarga la configuración del servicio sin reiniciar el proceso de Windows. Si `HABILITADO` queda en falso, pausa la planificación; cuando vuelve a verdadero, la reanuda. Una consulta que ya comenzó puede terminar con la configuración con la que inició.

El planificador usa el reloj local de Windows y revisa la hora cada dos segundos. Si se habilita la rutina después de la hora programada y aún no se ejecutó ese día, la consulta se hace al reanudar. Si ya hay una marca diaria, no repite el ciclo de esa fecha.

Para solicitar un reenvío sin reiniciar el servicio, elimine la marca de hoy `Logs\ControlEjecucion\EjecucionProductosSinVenta_yyyyMMdd.done`. El servicio detectará la eliminación en su siguiente revisión y volverá a consultar los productos, aunque ya hayan pasado las 08:00. El reenvío procesa el correo si hay resultados y el canal y los suscriptores están habilitados; omite una nueva notificación de intranet. Como la consulta se ejecuta otra vez, el contenido puede cambiar si los datos de origen cambiaron. Eliminar el archivo `.log` no solicita un reenvío.

## Canales de alerta

El canal de correo consulta `dbo.SP_OBTENER_SUSCRIPTORES_RUTINA_CORREO`. El canal intranet registra un evento en `dbo.ALERTA_HISTORIAL_EVENTO` y crea una notificación por cada suscriptor activo con `RECIBE_INTRANET = 1`. El JSON de artículos por código se guarda en `DETALLE_JSON`.

Ejecutar `SQL.sql` en `CBF_TEST` antes de publicar. El script define el query SAP, actualiza `SP_OBTENER_CONFIG_RUTINA` para devolver `DIAS_ATRAS` y ambos canales, crea `SP_OBTENER_VERSION_CONFIG_ALERTAS` y registra el procedimiento de notificaciones intranet.

Si la rutina está deshabilitada, no se inicia una nueva consulta diaria. Si la consulta no devuelve filas, no se envía correo ni se crea un evento.

## Prueba inmediata

Para una consulta de prueba sin correo ni notificación persistente:

```powershell
.\PRODUCTOS_SINVENTA.exe --Programacion:EjecutarAhora=true
```

Para incluir notificación intranet en la prueba, habilitar explícitamente el canal de prueba:

```powershell
.\PRODUCTOS_SINVENTA.exe --Programacion:EjecutarAhora=true --Programacion:EnviarNotificacionIntranetEnPrueba=true
```

## Logs

Se genera un único archivo técnico por día: `ProductosSinVenta-YYYYMMDD.log`. Incluye consultas, el reporte, cambios de `VERSION_CONFIG`, pausas/reactivaciones y errores.

## Publicación

Aplicar primero `SQL.sql` en `CBF_TEST`, luego publicar en una carpeta nueva para no sobrescribir una versión activa:

```powershell
dotnet publish .\PRODUCTOS_SINVENTA.csproj -c Release -r win-x64 --self-contained false -o .\publish-config-dinamica
```

Copiar a esa carpeta el `appsettings.json` de producción y verificar la conexión antes de instalar el servicio actualizado.
