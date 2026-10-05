namespace Pulsatilla.Wpf;

public sealed record ExposureLookupRequest(string[] EmailAddresses, bool ConsentToExternalLookup);
public sealed record ProviderExposure(string Email, string Service, string Date);
public sealed record ExposureLookupResult(string ProviderName, IReadOnlyList<ProviderExposure> Matches, string CoverageNote);

// Implementations can connect an optional paid service without changing offline review.
// Credentials, subscriptions and checkout belong in a provider/backend, never this interface.
public interface IExposureProvider
{
    string Name { get; }
    Task<ExposureLookupResult> LookupAsync(IReadOnlyList<string> emailAddresses, CancellationToken cancellationToken);
}

public sealed class ExposureProviderCoordinator(IExposureProvider? provider = null)
{
    public bool IsConfigured => provider is not null;
    public async Task<ExposureLookupResult> LookupAsync(ExposureLookupRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.ConsentToExternalLookup) throw new InvalidOperationException("External lookup requires explicit consent.");
        var addresses = (request.EmailAddresses ?? []).Select(value => EmailAddressRules.Address(value ?? "")).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (addresses.Length is 0 or > 20 || addresses.Any(address => string.IsNullOrEmpty(address) || !address.Contains('@')))
            throw new ArgumentException("Choose between one and twenty complete email addresses.", nameof(request));
        if (provider is null) throw new InvalidOperationException("No external exposure provider is configured. Local review remains available.");
        cancellationToken.ThrowIfCancellationRequested();
        return await provider.LookupAsync(Array.AsReadOnly(addresses.OfType<string>().ToArray()), cancellationToken).ConfigureAwait(false);
    }
}
