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

using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace Fastersetup.Framework.Api.Attributes.Data {
	/// <summary>
	/// Marks a mapped property as a timestamp column with CURRENT_TIMESTAMP() default value
	/// </summary>
	/// <remarks>
	/// This attribute <b>does not</b> force the behavior and is only a convention to allow integrations
	/// to recognize the property role
	/// </remarks>
	[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
	public class RowTimestampAttribute : Attribute, IPropertyValidationFilter {
		/// <summary>
		/// Marks if the property gets renewed each row update
		/// </summary>
		public bool SetOnUpdate { get; set; }

		public RowTimestampAttribute() {
		}

		public RowTimestampAttribute(bool setOnUpdate) {
			SetOnUpdate = setOnUpdate;
		}

		/// <summary>
		/// Validation is always ignored because the field cannot be set by the request
		/// </summary>
		public bool ShouldValidateEntry(ValidationEntry entry, ValidationEntry parentEntry) {
			return false;
		}
	}
}