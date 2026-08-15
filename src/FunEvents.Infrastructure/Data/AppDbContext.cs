using FunEvents.Domain.Events;
using FunEvents.Domain.Partners;
using FunEvents.Domain.Reservations;
using FunEvents.Domain.Users;
using FunEvents.Infrastructure.Idempotency;
using Microsoft.EntityFrameworkCore;

namespace FunEvents.Infrastructure.Data;

public class AppDbContext : DbContext
{
    public DbSet<Event> Events => Set<Event>();
    public DbSet<Reservation> Reservations => Set<Reservation>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Partner> Partners => Set<Partner>();
    public DbSet<IdempotencyKey> IdempotencyKeys => Set<IdempotencyKey>();

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Descubre todas las IEntityTypeConfiguration<> de este ensamblado.
        // Antes se registraban una a una, lo que garantiza que tarde o temprano
        // alguien anada una configuracion y olvide engancharla aqui.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        SeedData.Apply(modelBuilder);
    }
}
