using FunEvents.Domain.Partners;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FunEvents.Infrastructure.Data.Configurations;

public class PartnerConfiguration : IEntityTypeConfiguration<Partner>
{
    public void Configure(EntityTypeBuilder<Partner> builder)
    {
        builder.ToTable("Partners");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Name).IsRequired().HasMaxLength(200);
        builder.Property(p => p.ApiKeyHash).IsRequired().HasMaxLength(256);
        builder.Property(p => p.IsActive).IsRequired();
        builder.Property(p => p.RateLimit).IsRequired().HasDefaultValue(500);
        builder.Property(p => p.CreatedAt).IsRequired();
        builder.Property(p => p.UpdatedAt).IsRequired();

        // Npgsql mapea List<string> a text[] de forma nativa: sin conversor y,
        // por tanto, sin necesidad de un ValueComparer para el change tracking.
        builder.Property(p => p.Scopes).HasColumnType("text[]");

        builder.HasIndex(p => p.ApiKeyHash).IsUnique();
    }
}
