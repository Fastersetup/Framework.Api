namespace Fastersetup.Framework.Api.Services;

public interface ISessionDomainResolver {
	/// <summary>
	/// Gets active domain id or null if no domain can be resolved from the current execution context
	/// </summary>
	Guid? GetActiveDomainId();

	/// <inheritdoc cref="GetActiveDomainId"/>
	ValueTask<Guid?> GetActiveDomainIdAsync(CancellationToken token = default);
}