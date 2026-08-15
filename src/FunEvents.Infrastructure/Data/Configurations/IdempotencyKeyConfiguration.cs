using FunEvents.Infrastructure.Idempotency;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FunEvents.Infrastructure.Data.Configurations;

public class IdempotencyKeyConfiguration : IEntityTypeConfiguration<IdempotencyKey>
{
    public void Configure(EntityTypeBuilder<IdempotencyKey> builder)
    {
        builder.ToTable("IdempotencyKeys");

        // La clave primaria ES el mecanismo de exclusion mutua: dos peticiones
        // concurrentes con la misma key compiten por insertar la misma PK y
        // PostgreSQL garantiza que solo una gana.
        builder.HasKey(ik => ik.Key);
        builder.Property(ik => ik.Key).HasMaxLength(128);

        builder.Property(ik => ik.RequestFingerprint).IsRequired().HasMaxLength(64);
        builder.Property(ik => ik.ReservationId).IsRequired(false);
        builder.Property(ik => ik.StatusCode).IsRequired(false);
        builder.Property(ik => ik.ResponseBody).HasColumnType("jsonb").IsRequired(false);
        builder.Property(ik => ik.CreatedAt).IsRequired();
        builder.Property(ik => ik.CompletedAt).IsRequired(false);

        // Soporta la purga periodica de keys antiguas.
        builder.HasIndex(ik => ik.CreatedAt);
    }
}
