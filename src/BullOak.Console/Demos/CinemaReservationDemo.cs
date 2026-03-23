using BullOak.Console.Domain;
using BullOak.Repositories;
using BullOak.Repositories.Config;
using BullOak.Repositories.InMemory;
using Spectre.Console;

namespace BullOak.Console.Demos;

/// <summary>
/// Demo 13: Cinema Reservation — A Complete Domain Example
///
/// This demo shows a second domain to prove BullOak is not tied to
/// any specific domain. We model a cinema screening where:
///   - Seats can be reserved
///   - Seats can be cancelled
///   - State tracks reserved seats and total reservations
///
/// This also demonstrates using Dictionary&lt;string, string&gt; in state,
/// showing that BullOak works with complex state types.
/// </summary>
public static class CinemaReservationDemo
{
    public static async Task RunAsync()
    {
        AnsiConsole.Write(new Rule("[bold blue]Demo 13: Cinema Reservation (Second Domain)[/]").LeftJustified());
        AnsiConsole.WriteLine();

        AnsiConsole.Write(new Panel(
            "[dim]BullOak is domain-agnostic. This demo models a cinema screening\n" +
            "to show that the same patterns work with any domain.\n\n" +
            "Events: SeatReserved(SeatNumber, CustomerName), SeatCancelled(SeatNumber)\n" +
            "State:  CinemaScreeningState { ReservedSeats, TotalReservations }[/]")
            .Header("[yellow]A Different Domain[/]")
            .Border(BoxBorder.Rounded));

        var config = Configuration.Begin()
            .WithDefaultCollection()
            .WithDefaultStateFactory()
            .NeverUseThreadSafe()
            .WithNoEventPublisher()
            .WithAnyAppliersFrom(typeof(SeatReservedApplier).Assembly)
            .AndNoMoreAppliers()
            .WithNoUpconverters()
            .Build();

        var repo = new InMemoryEventSourcedRepository<string, CinemaScreeningState>(config);
        var screeningId = "AVENGERS-2024-SCREEN1";

        // ── Reserve seats ──────────────────────────────────────
        AnsiConsole.MarkupLine("[yellow]Booking seats for Avengers screening...[/]");
        using (var session = await repo.BeginSessionFor(screeningId, throwIfNotExists: false))
        {
            session.AddEvent(new SeatReserved("A1", "Alice"));
            session.AddEvent(new SeatReserved("A2", "Bob"));
            session.AddEvent(new SeatReserved("B1", "Charlie"));
            session.AddEvent(new SeatReserved("B2", "Diana"));
            session.AddEvent(new SeatReserved("C1", "Eve"));

            var state = session.GetCurrentState();
            DisplaySeatingChart(state, "After Initial Bookings");

            await session.SaveChanges();
        }

        // ── Cancel a seat ──────────────────────────────────────
        AnsiConsole.MarkupLine("[yellow]Charlie cancels seat B1...[/]");
        using (var session = await repo.BeginSessionFor(screeningId, throwIfNotExists: true))
        {
            session.AddEvent(new SeatCancelled("B1"));
            var state = session.GetCurrentState();
            DisplaySeatingChart(state, "After Cancellation");

            await session.SaveChanges();
        }

        // ── New booking in cancelled seat ──────────────────────
        AnsiConsole.MarkupLine("[yellow]Frank takes the freed seat B1...[/]");
        using (var session = await repo.BeginSessionFor(screeningId, throwIfNotExists: true))
        {
            session.AddEvent(new SeatReserved("B1", "Frank"));
            session.AddEvent(new SeatReserved("C2", "Grace"));
            var state = session.GetCurrentState();
            DisplaySeatingChart(state, "Final State");

            await session.SaveChanges();
        }

        // ── Replay from scratch ────────────────────────────────
        AnsiConsole.MarkupLine("[yellow]Verifying: Reload all events from store...[/]");
        using var verifySession = await repo.BeginSessionFor(screeningId, throwIfNotExists: true);
        var finalState = verifySession.GetCurrentState();

        AnsiConsole.MarkupLine($"  Total reservations (including cancelled): [aqua]{finalState.TotalReservations}[/]");
        AnsiConsole.MarkupLine($"  Currently reserved seats: [green]{finalState.ReservedSeats.Count}[/]");
        AnsiConsole.MarkupLine("[green]State perfectly reconstructed from event history.[/]");
    }

    private static void DisplaySeatingChart(CinemaScreeningState state, string title)
    {
        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("[bold]Seat[/]");
        table.AddColumn("[bold]Customer[/]");

        var allSeats = new[] { "A1", "A2", "B1", "B2", "C1", "C2" };
        foreach (var seat in allSeats)
        {
            if (state.ReservedSeats.TryGetValue(seat, out var customer))
                table.AddRow(seat, $"[green]{customer}[/]");
            else
                table.AddRow(seat, "[dim]Available[/]");
        }

        AnsiConsole.Write(new Panel(table)
            .Header($"[yellow]{title}[/]")
            .Border(BoxBorder.Rounded));
    }
}
