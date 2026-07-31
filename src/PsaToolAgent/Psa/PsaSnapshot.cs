namespace PsaToolAgent.Psa;

/// <summary>
/// Provider-agnostic snapshot of PSA ticket state, produced by any <see cref="IPsaDataProvider"/>.
/// </summary>
public sealed record PsaSnapshot
{
    public required int OpenTicketCount { get; init; }

    /// <summary>
    /// Ticket counts keyed by priority rank, where 1 is the most urgent tier a PSA defines and
    /// higher numbers are progressively less urgent. Using a numeric rank rather than a
    /// provider-specific label (e.g. Halo's "Critical"/"Urgent"/"High") keeps this contract usable
    /// by any future PSA provider, whatever it calls its own priority tiers. "P1"/"P2" text is a
    /// display-layer abbreviation of rank 1/rank 2, not part of this contract.
    /// </summary>
    public required IReadOnlyDictionary<int, int> PriorityCounts { get; init; }

    public required IReadOnlyList<SlaRiskTicket> SlaRiskTickets { get; init; }
    public required int UnassignedTicketCount { get; init; }
    public required IReadOnlyList<VipTicket> VipTickets { get; init; }
}

/// <param name="TicketId">Provider-specific ticket identifier, as a display-ready string.</param>
/// <param name="MinutesRemaining">Minutes remaining until this ticket's SLA breaches.</param>
public sealed record SlaRiskTicket(string TicketId, int MinutesRemaining);

/// <param name="TicketId">Provider-specific ticket identifier, as a display-ready string.</param>
/// <param name="CustomerName">Name of the VIP customer the ticket belongs to.</param>
public sealed record VipTicket(string TicketId, string CustomerName);
