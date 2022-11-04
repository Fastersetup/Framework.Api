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

using System.Text.Json.Serialization;
using Fastersetup.Framework.Api.Controllers.Filtering;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Fastersetup.Framework.Api {
	public class PropertyOrder {
		public string Name { get; set; }
		public SortOrder Order { get; set; }
		[JsonIgnore]
		public IReadOnlyPropertyBase? CachedProperty { get; set; }
	}
}