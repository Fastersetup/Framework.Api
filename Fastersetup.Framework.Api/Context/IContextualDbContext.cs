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

using System.Linq.Expressions;
using Fastersetup.Framework.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Fastersetup.Framework.Api.Context {
	public interface IContextualDbContext {
		ContextualDbSet<T> Set<T>() where T : class;
		ContextualDbSet<T> Contextualize<T>(DbSet<T> source) where T : class, IDomainEntity;

		ContextualDbSet<T> Contextualize<T>(DbSet<T> source, Expression<Func<T, IDomainEntity>> accessor)
			where T : class;
	}
}