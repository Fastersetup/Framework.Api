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
	/// Simple not found response model with default message
	/// </summary>
	public class NotFoundResponse : Response {
		public NotFoundResponse() : base("Requested entity not found") {
		}

		public NotFoundResponse(string message) : base(message) {
		}
	}
}