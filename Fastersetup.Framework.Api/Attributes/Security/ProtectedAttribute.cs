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

using Fastersetup.Framework.Api.Services.Default;

namespace Fastersetup.Framework.Api.Attributes.Security {
	/// <summary>
	/// Attribute to prevent <see cref="ObjectUtils"/> from copying the marked property
	/// </summary>
	[AttributeUsage(AttributeTargets.Property)]
	public class ProtectedAttribute : Attribute {
	}
}