namespace PRODUCTOS_SINVENTA;

public sealed class ConfiguracionLogs
{
    public string Ruta { get; set; } = "Logs";
}

public sealed class ConfiguracionSAP
{
    public string CompanyDB { get; set; } = "";
    public int DiasAtras { get; set; } = 3;
}

public sealed class ConfiguracionSQLServer
{
    public string CadenaConexion { get; set; } = "";
}

public sealed class ConfiguracionProgramacion
{
    public string HoraEjecucion { get; set; } = "08:00";
    public bool EjecutarAhora { get; set; } = false;
    public bool EnviarCorreoEnPrueba { get; set; } = false;
    public bool EnviarNotificacionIntranetEnPrueba { get; set; } = false;
}

public sealed class ConfiguracionRutina
{
    public string CodigoRutina { get; set; } = "PRODUCTOS_SINVENTA";
}
