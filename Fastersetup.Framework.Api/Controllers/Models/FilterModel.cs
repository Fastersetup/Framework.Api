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

namespace Fastersetup.Framework.Api.Controllers.Models {
	/// <summary>
	/// List-type request parameters model
	/// </summary>
	public class FilterModel {
		/// <summary>
		/// Unused field useful to avoid request content caching if a content refresh is required by the client
		/// </summary>
		public ulong Draw { get; set; } = 0;
		/// <summary>
		/// Requested page number
		/// </summary>
		/// <remarks>Zero-based value</remarks>
		public ulong Page { get; set; } = 0;
		/// <summary>
		/// Page length
		/// </summary>
		public ulong Length { get; set; } = 50;
		/// <summary>
		/// Filtering rules collection
		/// </summary>
		public ICollection<PropertyFilter>? Filters { get; set; }
		/// <summary>
		/// Sorting rules collection
		/// </summary>
		/// <remarks>Collection order is followed as provided</remarks>
		public ICollection<PropertyOrder>? Order { get; set; }
	}
}