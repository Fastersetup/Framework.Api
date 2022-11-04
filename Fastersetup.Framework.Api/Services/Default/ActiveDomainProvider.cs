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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Fastersetup.Framework.Api.Services.Default {
	/// <summary>
	/// Default <see cref="IActiveDomainProvider"/> implementation.<br/>
	/// When registered as a <see cref="ServiceLifetime.Scoped"/> service keeps track of the domain registered with
	/// <see cref="SetDomain"/> or the one provided by the registered <see cref="ISessionDomainResolver"/>
	/// </summary>
	/// <typeparam name="TDomain">The domain entity type used to query the provided <see cref="DbContext"/></typeparam>
	public class ActiveDomainProvider<TDomain> : IActiveDomainProvider where TDomain : Domain {
		private readonly DbContext _context;
		private readonly ISessionDomainResolver? _sessionService;
		private TDomain? _activeDomain;

		/// <inheritdoc/>
		public Domain? GetActiveDomain() {
			if (_activeDomain != null)
				return _activeDomain;
			var id = _sessionService?.GetActiveDomainId();
			if (!id.HasValue)
				return null;
			return _activeDomain = _context.Set<TDomain>().Find(id.Value);
		}

		/// <inheritdoc/>
		public async Task<Domain?> GetActiveDomainAsync(CancellationToken token) {
			if (_activeDomain != null)
				return _activeDomain;
			var id = _sessionService == null ? null : await _sessionService.GetActiveDomainIdAsync(token);
			if (!id.HasValue)
				return null;
			return _activeDomain = await _context.Set<TDomain>().FindAsync(new object[] {id.Value}, token);
		}

		/// <inheritdoc/>
		public void SetDomain(Domain? active) {
			_activeDomain = (TDomain?) active;
		}

		public ActiveDomainProvider(DbContext context, ISessionDomainResolver? sessionService = null) {
			_context = context;
			_sessionService = sessionService;
		}
	}
}