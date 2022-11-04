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

namespace Fastersetup.Framework.Api.Attributes.Data {
	public enum TimeSpanPrecision {
		Tick,
		Microsecond,
		Millisecond,
		Second,
		Minute,
		Hour,
		Day
	}

	/// <summary>
	/// Defines a TimeSpan property precision. Defines which scale is saved as field value
	/// </summary>
	/// <example>
	/// If <see cref="TimeSpanPrecision.Minute"/> is used the value from <see cref="TimeSpan.TotalMinutes"/> will be stored
	/// </example>
	public class TimeSpanPrecisionAttribute : Attribute {
		public TimeSpanPrecision Precision { get; }
		public bool UseShorterUnit { get; }

		public TimeSpanPrecisionAttribute(TimeSpanPrecision precision, bool useShorterUnit = false) {
			Precision = precision;
			UseShorterUnit = useShorterUnit;
		}
	}
}