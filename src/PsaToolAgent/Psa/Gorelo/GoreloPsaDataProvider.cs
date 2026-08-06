namespace PsaToolAgent.Psa.Gorelo;

public sealed class GoreloPsaDataProvider
{
    /// <summary>
    /// Pure mapping from raw Gorelo ticket data (the full paginated set, open and closed) to the
    /// provider-agnostic <see cref="PsaSnapshot"/>. Internal (not private) so tests can exercise
    /// the mapping logic directly, without HTTP.
    ///
    /// A ticket counts as active/open when <see cref="GoreloTicket.ClosedOn"/> is null — Gorelo's
    /// API has no server-side open filter and no documented status taxonomy, so this is the one
    /// tenant-customization-proof signal.
    ///
    /// Priority rank is resolved by name against <see cref="PriorityRankByName"/>, not by
    /// <see cref="GoreloCodeModel.Id"/>: <see cref="PsaSnapshot.PriorityCounts"/>'s "1 = most
    /// urgent" contract has no guaranteed relationship to a tenant-assigned numeric id, and
    /// PriorityEngine hardcodes rank 1 as its CRITICAL trigger, so getting this wrong would
    /// misfire the display. A ticket whose priority name isn't in the table is excluded from
    /// PriorityCounts/PriorityNames (still counted in OpenTicketCount).
    ///
    /// VipTickets is always empty and OrganizationName is always null: neither concept exists in
    /// Gorelo's public API as of this implementation.
    /// </summary>
    internal static PsaSnapshot MapSnapshot(IReadOnlyList<GoreloTicket> tickets)
    {
        var activeTickets = tickets.Where(t => t.ClosedOn is null).ToList();

        var priorityCounts = new Dictionary<int, int>();
        var priorityNames = new Dictionary<int, string>();

        foreach (var ticket in activeTickets)
        {
            var name = ticket.Priority?.Name;
            if (name is null || !PriorityRankByName.TryGetValue(name, out var rank))
            {
                continue;
            }

            priorityCounts[rank] = priorityCounts.GetValueOrDefault(rank) + 1;
            priorityNames[rank] = name;
        }

        var slaRiskTickets = activeTickets
            .Where(t => t.Sla?.FirstResponse?.Minutes is not null)
            .Select(t => new SlaRiskTicket(t.DisplayNumber ?? t.Id, (int)t.Sla!.FirstResponse!.Minutes!.Value))
            .ToList();

        var unassignedCount = activeTickets.Count(t => t.LeadAssigneeId is null);

        return new PsaSnapshot
        {
            OpenTicketCount = activeTickets.Count,
            PriorityCounts = priorityCounts,
            PriorityNames = priorityNames,
            SlaRiskTickets = slaRiskTickets,
            UnassignedTicketCount = unassignedCount,
            VipTickets = Array.Empty<VipTicket>(),
            OrganizationName = null
        };
    }

    /// <summary>Gorelo tenants run one of two fixed priority schemes (2-level Urgent/Normal, or
    /// 5-level Urgent/High/Normal/Low/None) — unlike Halo, priority names aren't arbitrary, so a
    /// fixed table (rather than per-tenant discovery) is sufficient.</summary>
    private static readonly IReadOnlyDictionary<string, int> PriorityRankByName =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Urgent"] = 1,
            ["High"] = 2,
            ["Normal"] = 3,
            ["Low"] = 4,
            ["None"] = 5
        };
}
