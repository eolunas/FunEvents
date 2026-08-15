using FluentAssertions;
using FunEvents.Domain.Common;
using FunEvents.Domain.Events;

namespace FunEvents.UnitTests.Domain;

public class EventTests
{
    private static Event Create(int capacity = 100)
        => new("Test Event", "Descripcion", "Recinto",
            DateTimeOffset.UtcNow.AddDays(1),
            DateTimeOffset.UtcNow.AddDays(1).AddHours(4),
            capacity);

    [Fact]
    public void Nace_en_borrador_y_no_esta_a_la_venta()
    {
        var @event = Create();

        @event.State.Should().Be(EventState.Draft);
        @event.IsOpenForSale().Should().BeFalse("un evento en borrador no puede vender entradas");
        @event.AvailableCapacity().Should().Be(100);
    }

    [Fact]
    public void Publicar_lo_abre_a_la_venta()
    {
        var @event = Create();

        @event.Publish();

        @event.State.Should().Be(EventState.Published);
        @event.IsOpenForSale().Should().BeTrue();
    }

    [Fact]
    public void No_se_puede_publicar_dos_veces()
    {
        var @event = Create();
        @event.Publish();

        var act = () => @event.Publish();

        act.Should().Throw<DomainException>()
            .Which.ErrorCode.Should().Be(EventErrors.InvalidTransition);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Rechaza_aforos_no_positivos(int capacity)
    {
        var act = () => Create(capacity);

        act.Should().Throw<DomainException>()
            .Which.ErrorCode.Should().Be(EventErrors.InvalidCapacity);
    }

    [Fact]
    public void Rechaza_una_fecha_de_fin_anterior_al_inicio()
    {
        var start = DateTimeOffset.UtcNow.AddDays(2);

        var act = () => new Event("X", "d", "v", start, start.AddHours(-1), 10);

        act.Should().Throw<DomainException>()
            .Which.ErrorCode.Should().Be(EventErrors.InvalidDates);
    }

    [Fact]
    public void Un_evento_cancelado_deja_de_estar_a_la_venta()
    {
        var @event = Create();
        @event.Publish();

        @event.Cancel();

        @event.State.Should().Be(EventState.Cancelled);
        @event.IsOpenForSale().Should().BeFalse();
    }
}
