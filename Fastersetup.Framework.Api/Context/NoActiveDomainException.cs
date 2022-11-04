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

namespace Fastersetup.Framework.Api.Context {
	/// <summary>
	/// Thrown when no domain could be resolved in the executing context
	/// </summary>
	public class NoActiveDomainException : InvalidOperationException {
		public NoActiveDomainException() : base("No active domain found in the current session context") {
		}

		public NoActiveDomainException(string message) : base(message) {
		}
	}
}