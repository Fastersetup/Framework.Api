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

namespace Fastersetup.Framework.Api.Context {
	/// <summary>
	/// Thrown when an entity is referencing a <see cref="Domain"/> different than the one resolved in the current context
	/// </summary>
	public class UnauthorizedDomainException : InvalidOperationException {
		public UnauthorizedDomainException(Domain? target, Domain? current)
			: base(
				$"The current session context is not authorized to perform this operation. Domain {target} is not accessible from {current}") {
		}
	}
}