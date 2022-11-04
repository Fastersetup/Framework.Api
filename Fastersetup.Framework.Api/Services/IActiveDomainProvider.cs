/*
 * Copyright 2022 Francesco Cattoni
 * 
 * This program is free software; you can redistribute it and/or
 * modify it under the terms of the GNU General Public License
 * version 3 as published by the Free Software Foundation.
 * 
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU General Public License for more details.
 */

using Fastersetup.Framework.Api.Data;
using Microsoft.Extensions.DependencyInjection;

namespace Fastersetup.Framework.Api.Services {
	/// <summary>
	/// When registered as a <see cref="ServiceLifetime.Scoped"/> service keeps track of the domain registered with
	/// <see cref="SetDomain"/> or with other means depending on the service implementation
	/// </summary>
	public interface IActiveDomainProvider {
		/// <summary>
		/// Gets the active domain of this scope or null if no domain is resolved
		/// </summary>
		Domain? GetActiveDomain();

		/// <inheritdoc cref="GetActiveDomain"/>
		Task<Domain?> GetActiveDomainAsync(CancellationToken token);

		/// <summary>
		/// Sets the active domain for this scope to the given <paramref name="active"/> instance
		/// </summary>
		void SetDomain(Domain? active);
	}
}