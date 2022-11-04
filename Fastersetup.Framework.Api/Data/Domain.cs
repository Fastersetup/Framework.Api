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

using System.ComponentModel.DataAnnotations;
using Fastersetup.Framework.Api.Attributes.Security;
using Fastersetup.Framework.Api.Services;

namespace Fastersetup.Framework.Api.Data {
	/// <summary>
	/// Domain entity base POCO class implementation
	/// </summary>
	/// <remarks>
	/// The class is used as default domain definition but can be extended implementing a custom
	/// <see cref="ISessionDomainResolver"/> and using the extended class in entities
	/// </remarks>
	public class Domain {
		[Key]
		public virtual Guid Id { get; set; }
		[Required, Filterable]
		public virtual string Name { get; set; }

		public override string ToString() {
			return $"{Name} [{Id:N}]";
		}
	}
}