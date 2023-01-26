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

using Fastersetup.Framework.Api.Controllers.Filtering;

namespace Fastersetup.Framework.Api {
	public class PropertyFilter {
		public string Name { get; set; }
		public FilterAction Action { get; set; } = FilterAction.StartsWith;
		public bool? Extended { get; set; }
		public string? Value { get; set; }
		public IList<string?>? Values { get; set; }
	}
}