namespace PRODUCTOS_SINVENTA.DTOs;

public sealed class ResultadoNotificacionIntranetDTO
{
    public long IdEvento { get; init; }
    public bool EventoNuevo { get; init; }
    public int NotificacionesCreadas { get; init; }
}
