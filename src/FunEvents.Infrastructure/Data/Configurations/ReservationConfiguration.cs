using FunEvents.Domain.Events;
using FunEvents.Domain.Reservations;
using FunEvents.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FunEvents.Infrastructure.Data.Configurations;

public class ReservationConfiguration : IEntityTypeConfiguration<Reservation>
{
    public void Configure(EntityTypeBuilder<Reservation> builder)
    {
        builder.ToTable("Reservations", t => t.HasCheckConstraint(
            "CK_Reservations_TicketQuantity_Positive", "\"TicketQuantity\" > 0"));

        builder.HasKey(r => r.Id);

        builder.Property(r => r.EventId).IsRequired();
        builder.Property(r => r.UserId).IsRequired();
        builder.Property(r => r.PartnerId).IsRequired(false);
        builder.Property(r => r.TicketQuantity).IsRequired();
        builder.Property(r => r.ExpiresAt).IsRequired();
        builder.Property(r => r.CreatedAt).IsRequired();
        builder.Property(r => r.UpdatedAt).IsRequired();

        builder.Property(r => r.State)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(r => r.Channel)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.HasOne<Event>()
            .WithMany()
            .HasForeignKey(r => r.EventId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        // IMPORTANTE: los filtros parciales deben usar EXACTAMENTE los valores
        // que escribe el ValueConverter. Con HasConversion<string>() eso es el
        // nombre del enum en PascalCase ('Reserved'), no 'RESERVED'.
        // La version anterior filtraba por 'RESERVED'/'CONFIRMED', asi que los
        // dos indices parciales nunca cubrian ninguna fila: existian en el
        // esquema y el planificador jamas los usaba.
        builder.HasIndex(r => new { r.UserId, r.EventId })
            .HasDatabaseName("IX_Reservations_User_Event_Active")
            .HasFilter("\"State\" IN ('Reserved', 'Confirmed')");

        builder.HasIndex(r => new { r.State, r.ExpiresAt })
            .HasDatabaseName("IX_Reservations_Pending_Expiration")
            .HasFilter("\"State\" = 'Reserved'");

        builder.HasIndex(r => r.EventId);
        builder.HasIndex(r => r.PartnerId);
    }
}
