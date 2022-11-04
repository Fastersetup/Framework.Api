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

namespace Fastersetup.Framework.Api.Services {
	public interface IAccessControlService<in T> where T : class {
		Task Read(T? model);
		Task Create(T model, T? body);
		Task Edit(T existing, T? patch); // EditBefore & EditAfter?
		Task Delete(T existing, T? body);
	}
}