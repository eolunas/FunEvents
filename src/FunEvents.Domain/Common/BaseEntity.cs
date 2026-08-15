namespace FunEvents.Domain.Common;

/// <summary>
/// Raiz comun de las entidades persistidas.
/// Deliberadamente minima: identidad + auditoria temporal.
/// </summary>
/// <remarks>
/// Aqui NO hay infraestructura de domain events. Se evaluo incluirla, pero
/// en el alcance actual no hay ningun suscriptor, y una coleccion de eventos
/// que nadie despacha es codigo muerto que confunde a quien lee el dominio.
/// Cuando se necesiten (webhooks a partners, proyecciones de lectura) se
/// introducen junto con su dispatcher. Ver architecture.md, Fase 3.
/// </remarks>
public abstract class BaseEntity
{
    public Guid Id { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    protected BaseEntity()
    {
        Id = Guid.NewGuid();
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    protected void Touch() => UpdatedAt = DateTimeOffset.UtcNow;
}
