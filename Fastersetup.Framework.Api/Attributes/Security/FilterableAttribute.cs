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

using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Fastersetup.Framework.Api.Attributes.Security {
	/// <summary>
	/// Attribute to mark filterable properties exposed through a defined <see cref="ApiController{TModel}"/> implementation
	/// </summary>
	public class FilterableAttribute : Attribute, IAnnotation {
		public string Name => "FSFilterable";
		public object Value => true;
	}
}