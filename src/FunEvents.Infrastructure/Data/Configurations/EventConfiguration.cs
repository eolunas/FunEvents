using FunEvents.Domain.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FunEvents.Infrastructure.Data.Configurations;

public class EventConfiguration : IEntityTypeConfiguration<Event>
{
    public void Configure(EntityTypeBuilder<Event> builder)
    {
        builder.ToTable("Events", t => t.HasCheckConstraint(
            "CK_Events_ReservedCount_Within_Capacity",
            "\"ReservedCount\" >= 0 AND \"ReservedCount\" <= \"Capacity\""));

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Name).IsRequired().HasMaxLength(200);
        builder.Property(e => e.Description).HasMaxLength(2000);
        builder.Property(e => e.Venue).IsRequired().HasMaxLength(200);
        builder.Property(e => e.StartDate).IsRequired();
        builder.Property(e => e.EndDate).IsRequired();
        builder.Property(e => e.Capacity).IsRequired();
        builder.Property(e => e.ReservedCount).IsRequired().HasDefaultValue(0);

        // Se persiste el nombre del enum, no su valor numerico: la tabla se
        // puede leer sin consultar el codigo y reordenar el enum no corrompe
        // los datos existentes.
        builder.Property(e => e.State)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(e => e.PartnerId).IsRequired(false);
        builder.Property(e => e.CreatedAt).IsRequired();
        builder.Property(e => e.UpdatedAt).IsRequired();

        // Soporta el listado del catalogo: filtro por estado + orden por fecha.
        builder.HasIndex(e => new { e.State, e.StartDate });
        builder.HasIndex(e => e.PartnerId);
    }
}
