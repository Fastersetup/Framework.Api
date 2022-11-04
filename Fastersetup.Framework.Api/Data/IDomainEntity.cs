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

namespace Fastersetup.Framework.Api.Data {
	/// <summary>
	/// Contextualized entity defining a referenced <see cref="Domain"/><br/>
	/// All entity navigations should always refer the same <see cref="Domain"/> even in many-to-many relationships
	/// </summary>
	public interface IDomainEntity {
		/// <summary>
		/// Entity domain context
		/// </summary>
		Domain Domain { get; set; }
	}
}