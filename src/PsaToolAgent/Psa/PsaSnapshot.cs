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

    /// <summary>
    /// Every ticket that has a known SLA timer, unfiltered by any risk threshold — including
    /// tickets with days left and already-breached tickets (negative <see cref="SlaRiskTicket.MinutesRemaining"/>).
    /// A provider populating this must NOT pre-filter by threshold: <c>PriorityEngine</c> is the
    /// single place that applies the SLA-risk threshold, so a provider-side filter here would
    /// silently duplicate (and risk diverging from) that logic.
    /// </summary>
    public required IReadOnlyList<SlaRiskTicket> SlaRiskTickets { get; init; }
    public required int UnassignedTicketCount { get; init; }
    public required IReadOnlyList<VipTicket> VipTickets { get; init; }
}

/// <param name="TicketId">Provider-specific ticket identifier, as a display-ready string.</param>
/// <param name="MinutesRemaining">Minutes remaining until this ticket's SLA breaches. Negative
/// once the ticket has already breached.</param>
public sealed record SlaRiskTicket(string TicketId, int MinutesRemaining);

/// <param name="TicketId">Provider-specific ticket identifier, as a display-ready string.</param>
/// <param name="CustomerName">Name of the VIP customer the ticket belongs to.</param>
public sealed record VipTicket(string TicketId, string CustomerName);
