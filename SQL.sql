USE CBF
GO
CREATE OR ALTER PROCEDURE dbo.SP_BUSCAR_PRODUCTOS_LOTES_SIN_VENTAS
    @BD_HANA VARCHAR(100),
    @DIAS_ATRAS INT,
    @FECHA_REFERENCIA DATE = NULL
AS
BEGIN
    SET NOCOUNT ON;

    IF NULLIF(LTRIM(RTRIM(@BD_HANA)), '') IS NULL
       OR @BD_HANA LIKE '%[^A-Za-z0-9_]%' 
    BEGIN
        THROW 50001,
            'Debe especificar un esquema válido de SAP HANA.',
            1;
    END;

    IF @DIAS_ATRAS IS NULL OR @DIAS_ATRAS < 0
    BEGIN
        THROW 50002,
            'La cantidad de días debe ser cero o un número positivo.',
            1;
    END;

    SET @FECHA_REFERENCIA = COALESCE(@FECHA_REFERENCIA, CONVERT(DATE, GETDATE()));

    DECLARE @SQL                    NVARCHAR(MAX);
    DECLARE @SQL_HANA               NVARCHAR(MAX);
    DECLARE @ESQUEMA                NVARCHAR(100);
    DECLARE @FECHA_INICIO_TEXTO     NVARCHAR(10);
    DECLARE @FECHA_FIN_TEXTO        NVARCHAR(10);

    SET @ESQUEMA = LTRIM(RTRIM(@BD_HANA));
    SET @FECHA_INICIO_TEXTO = CONVERT(NVARCHAR(10), DATEADD(DAY, -@DIAS_ATRAS, @FECHA_REFERENCIA), 23);
    SET @FECHA_FIN_TEXTO = CONVERT(NVARCHAR(10), DATEADD(DAY, 1, @FECHA_REFERENCIA), 23);

    SET @SQL_HANA = N'
        SELECT
            W."ItemCode" AS "CODIGO",
            I."ItemName" AS "PRODUCTO",
            I."BuyUnitMsr" AS "UNIDAD_MEDIDA",
            W."OnHand" AS "STOCK_UNIDADES",
            CASE
                WHEN COALESCE(I."NumInBuy", 0) > 0
                    THEN W."OnHand" / I."NumInBuy"
                ELSE NULL
            END AS "STOCK_CAJAS",
            CASE
                WHEN COALESCE(I."NumInBuy", 0) > 0
                    THEN W."AvgPrice" * I."NumInBuy"
                ELSE NULL
            END AS "COSTO_X_CAJA",
            CASE
                WHEN COALESCE(I."NumInBuy", 0) > 0
                    THEN (W."OnHand" / I."NumInBuy") * (W."AvgPrice" * I."NumInBuy")
                ELSE NULL
            END AS "VALORIZACION"
        FROM "' + @ESQUEMA + N'"."OITW" W
        INNER JOIN "' + @ESQUEMA + N'"."OITM" I
            ON I."ItemCode" = W."ItemCode"
        WHERE W."WhsCode" = ''ALM-01''
          AND W."OnHand" > 0
          AND NOT EXISTS
          (
              SELECT 1
              FROM "' + @ESQUEMA + N'"."OINV" H
              INNER JOIN "' + @ESQUEMA + N'"."INV1" L
                  ON L."DocEntry" = H."DocEntry"
              WHERE L."ItemCode" = W."ItemCode"
                AND L."WhsCode" = W."WhsCode"
                AND H."CANCELED" = ''N''
                AND H."DocDate" >= TO_DATE(''' + @FECHA_INICIO_TEXTO + N''', ''YYYY-MM-DD'')
                AND H."DocDate" < TO_DATE(''' + @FECHA_FIN_TEXTO + N''', ''YYYY-MM-DD'')
          )
          AND NOT EXISTS
          (
              SELECT 1
              FROM "' + @ESQUEMA + N'"."ODLN" H
              INNER JOIN "' + @ESQUEMA + N'"."DLN1" L
                  ON L."DocEntry" = H."DocEntry"
              WHERE L."ItemCode" = W."ItemCode"
                AND L."WhsCode" = W."WhsCode"
                AND H."CANCELED" = ''N''
                AND H."DocDate" >= TO_DATE(''' + @FECHA_INICIO_TEXTO + N''', ''YYYY-MM-DD'')
                AND H."DocDate" < TO_DATE(''' + @FECHA_FIN_TEXTO + N''', ''YYYY-MM-DD'')
          )
    ';

    SET @SQL = N'
        SELECT
            R.CODIGO,
            R.PRODUCTO,
            R.UNIDAD_MEDIDA,
            R.STOCK_CAJAS,
            R.COSTO_X_CAJA,
            R.VALORIZACION
        FROM OPENQUERY(SAP, '''
        + REPLACE(@SQL_HANA, '''', '''''')
        + N''') AS R
        ORDER BY R.PRODUCTO, R.CODIGO;
    ';

    EXEC sys.sp_executesql @SQL;
END;
GO

CREATE OR ALTER PROCEDURE dbo.SP_REGISTRAR_ALERTA_PRODUCTOS_SINVENTA
    @CODIGO_RUTINA          VARCHAR(50),
    @FECHA_REFERENCIA       DATE,
    @FECHA_DETECCION        DATETIME,
    @CANTIDAD_PRODUCTOS     INT,
    @DETALLE_JSON           NVARCHAR(MAX),
    @TITULO                 VARCHAR(200),
    @MENSAJE                VARCHAR(1000),
    @ID_EVENTO              BIGINT OUTPUT,
    @EVENTO_NUEVO           BIT OUTPUT,
    @NOTIFICACIONES_CREADAS INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @FECHA_REFERENCIA IS NULL OR @CANTIDAD_PRODUCTOS IS NULL OR @CANTIDAD_PRODUCTOS < 1
        THROW 50010, 'La fecha y la cantidad de productos deben ser válidas.', 1;

    IF ISJSON(@DETALLE_JSON) <> 1
        THROW 50011, 'El detalle de productos debe ser JSON válido.', 1;

    IF NULLIF(LTRIM(RTRIM(@TITULO)), '') IS NULL
       OR NULLIF(LTRIM(RTRIM(@MENSAJE)), '') IS NULL
        THROW 50012, 'El título y el mensaje de intranet son obligatorios.', 1;

    DECLARE @ID_RUTINA INT;
    DECLARE @ID_EVENTO_EXISTENTE BIGINT;
    DECLARE @ID_BASE BIGINT;
    DECLARE @ID_NOTIFICACION_BASE BIGINT;
    DECLARE @RECURSO_BLOQUEO NVARCHAR(255);
    DECLARE @RESULTADO_BLOQUEO INT;
    DECLARE @CLAVE_DOCUMENTO VARCHAR(50);

    SET @FECHA_DETECCION = COALESCE(@FECHA_DETECCION, GETDATE());
    SET @CLAVE_DOCUMENTO = LEFT(
        @CODIGO_RUTINA + '-' + CONVERT(CHAR(8), @FECHA_REFERENCIA, 112),
        50
    );
    SET @RECURSO_BLOQUEO = N'ALERTA_PRODUCTOS_SINVENTA|' + @CLAVE_DOCUMENTO;
    SET @ID_EVENTO = NULL;
    SET @EVENTO_NUEVO = 0;
    SET @NOTIFICACIONES_CREADAS = 0;

    DECLARE @DESTINATARIOS TABLE
    (
        ID_USUARIO VARCHAR(50) NOT NULL PRIMARY KEY
    );

    DECLARE @PENDIENTES TABLE
    (
        ID_USUARIO VARCHAR(50) NOT NULL PRIMARY KEY
    );

    BEGIN TRY
        BEGIN TRANSACTION;

        EXEC @RESULTADO_BLOQUEO = sys.sp_getapplock
            @Resource = @RECURSO_BLOQUEO,
            @LockMode = 'Exclusive',
            @LockOwner = 'Transaction',
            @LockTimeout = 15000;

        IF @RESULTADO_BLOQUEO < 0
            THROW 50013, 'No se pudo reservar el registro diario de la alerta.', 1;

        SELECT @ID_RUTINA = R.ID_RUTINA
        FROM dbo.ALERTA_RUTINA AS R WITH (UPDLOCK, HOLDLOCK)
        WHERE R.CODIGO_RUTINA = @CODIGO_RUTINA
          AND R.HABILITADO = 1
          AND R.ESTADO = 1
          AND R.CANAL_INTRANET = 1;

        IF @ID_RUTINA IS NULL
            THROW 50014, 'La rutina no existe, está deshabilitada o tiene apagado el canal intranet.', 1;

        SELECT @ID_EVENTO_EXISTENTE = E.ID_EVENTO
        FROM dbo.ALERTA_HISTORIAL_EVENTO AS E WITH (UPDLOCK, HOLDLOCK)
        WHERE E.ID_RUTINA = @ID_RUTINA
          AND E.CLAVE_DOCUMENTO = @CLAVE_DOCUMENTO
          AND E.LINEA_DOCUMENTO = 0;

        IF @ID_EVENTO_EXISTENTE IS NULL
        BEGIN
            -- Las tablas usan IDs manuales (no IDENTITY); el bloqueo evita colisiones al asignarlos.
            SELECT @ID_BASE = ISNULL(MAX(E.ID_EVENTO), 0)
            FROM dbo.ALERTA_HISTORIAL_EVENTO AS E WITH (TABLOCKX, HOLDLOCK);

            SET @ID_EVENTO = @ID_BASE + 1;

            INSERT INTO dbo.ALERTA_HISTORIAL_EVENTO
            (
                ID_EVENTO,
                ID_RUTINA,
                CLAVE_DOCUMENTO,
                LINEA_DOCUMENTO,
                NUMERO_DOCUMENTO,
                FECHA_DOCUMENTO,
                PROVEEDOR_CODIGO,
                PROVEEDOR_NOMBRE,
                ITEM_CODE,
                ITEM_NAME,
                PRECIO_COMPRA,
                COSTO_REGISTRADO,
                DIFERENCIA_MONTO,
                ALMACEN_CODIGO,
                ENV_WMS,
                DETALLE_JSON,
                FECHA_DETECCION
            )
            VALUES
            (
                @ID_EVENTO,
                @ID_RUTINA,
                @CLAVE_DOCUMENTO,
                0,
                @CLAVE_DOCUMENTO,
                @FECHA_REFERENCIA,
                NULL,
                NULL,
                'PRODUCTOS_SINVENTA',
                CONCAT(@CANTIDAD_PRODUCTOS, ' producto(s) sin venta'),
                0,
                0,
                0,
                'ALM-01',
                'N',
                @DETALLE_JSON,
                @FECHA_DETECCION
            );

            SET @EVENTO_NUEVO = 1;
        END
        ELSE
        BEGIN
            SET @ID_EVENTO = @ID_EVENTO_EXISTENTE;
        END;

        INSERT INTO @DESTINATARIOS (ID_USUARIO)
        SELECT DISTINCT LOWER(LTRIM(RTRIM(S.ID_USUARIO)))
        FROM dbo.ALERTA_SUSCRIPTOR AS S WITH (HOLDLOCK)
        WHERE S.ID_RUTINA = @ID_RUTINA
          AND S.ESTADO = 1
          AND S.RECIBE_INTRANET = 1
          AND NULLIF(LTRIM(RTRIM(S.ID_USUARIO)), '') IS NOT NULL;

        INSERT INTO @PENDIENTES (ID_USUARIO)
        SELECT D.ID_USUARIO
        FROM @DESTINATARIOS AS D
        WHERE NOT EXISTS
        (
            SELECT 1
            FROM dbo.ALERTA_NOTIFICACION_INTRANET AS N WITH (UPDLOCK, HOLDLOCK)
            WHERE N.ID_EVENTO = @ID_EVENTO
              AND LOWER(LTRIM(RTRIM(N.ID_USUARIO))) = D.ID_USUARIO
        );

        IF EXISTS (SELECT 1 FROM @PENDIENTES)
        BEGIN
            SELECT @ID_NOTIFICACION_BASE = ISNULL(MAX(N.ID_NOTIFICACION), 0)
            FROM dbo.ALERTA_NOTIFICACION_INTRANET AS N WITH (TABLOCKX, HOLDLOCK);

            INSERT INTO dbo.ALERTA_NOTIFICACION_INTRANET
            (
                ID_NOTIFICACION,
                ID_EVENTO,
                ID_USUARIO,
                TITULO,
                MENSAJE,
                NIVEL_ALERTA,
                LEIDO,
                CONFIRMADO
            )
            SELECT
                @ID_NOTIFICACION_BASE + ROW_NUMBER() OVER (ORDER BY P.ID_USUARIO),
                @ID_EVENTO,
                P.ID_USUARIO,
                @TITULO,
                @MENSAJE,
                'WARNING',
                0,
                0
            FROM @PENDIENTES AS P;

            SET @NOTIFICACIONES_CREADAS = @@ROWCOUNT;
        END;

        COMMIT TRANSACTION;

        SELECT
            @ID_EVENTO AS ID_EVENTO,
            @EVENTO_NUEVO AS EVENTO_NUEVO,
            @NOTIFICACIONES_CREADAS AS NOTIFICACIONES_CREADAS;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0
            ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END;
GO

