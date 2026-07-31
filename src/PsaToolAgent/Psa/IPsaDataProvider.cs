namespace PsaToolAgent.Psa;

/// <summary>
/// The pluggable-provider seam: implement this for a new PSA. Register the implementation in DI
/// and select it via the <c>Psa:Provider</c> config setting.
/// </summary>
public interface IPsaDataProvider
{
    Task<PsaSnapshot> GetSnapshotAsync(CancellationToken cancellationToken);
}
