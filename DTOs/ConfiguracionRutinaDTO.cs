namespace PRODUCTOS_SINVENTA.DTOs;

public sealed class ConfiguracionRutinaDTO
{
    public bool Habilitado { get; set; } = true;
    public bool CanalCorreo { get; set; }
    public bool CanalIntranet { get; set; }
    public int DiasAtras { get; set; } = 3;
}
