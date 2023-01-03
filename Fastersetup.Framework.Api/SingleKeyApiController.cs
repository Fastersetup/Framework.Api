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
using Fastersetup.Framework.Api.Controllers.Filtering;
using Fastersetup.Framework.Api.Controllers.Models;
using Fastersetup.Framework.Api.Services;
using Fastersetup.Framework.Api.Services.Utilities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Fastersetup.Framework.Api {
	public abstract class SingleKeyApiController<T, TPk> : ApiController<T> where T : class, new() {
		private readonly DbContext _context;
		private readonly FilteringService _filteringService;

		public SingleKeyApiController(DbContext context, FilteringService filteringService, IObjectUtils utils,
			ILogger logger, IAccessControlService<T>? aclService = null)
			: base(context, filteringService, utils, logger, aclService) {
			_context = context;
			_filteringService = filteringService;
		}

		protected abstract Expression<Func<T, bool>> ResolvePkExpression(TPk pk); // Could get it with reflection

		[HttpGet("{id}")]
		public async Task<IActionResult> Get([FromRoute] TPk id, [FromQuery] FilterModel? filter = null) {
			var o = await ReadSource.FirstOrDefaultAsync(ResolvePkExpression(id), HttpContext.RequestAborted);
			if (Acl != null)
				await Acl.Read(o);
			if (o == null)
				return NotFound(new NotFoundResult());
			if (filter != null)
				await TryAppendNavigationMetadata(filter, o);
			return Ok(o);
		}

		private async Task TryAppendNavigationMetadata(FilterModel filter, T o) {
			var q = PrepareQueryFilter(Source, filter, out _); // Force no Includes
			if (q == null)
				return;
			q = AppendSorting(q, filter, out _);
			if (q == null)
				return;
			// Build conditions for comparison to get next and previous values
			// > The block assumes all ordered properties are comparable
			var nextQuery = q;
			var prevQuery = q;
			Expression? nextExpr = null;
			Expression? prevExpr = null;
			var parameter = Expression.Parameter(typeof(T), "o");
			foreach (var property in filter.Order is {Count: > 0} ? filter.Order : EnumerateDefaultOrderBy()) {
				object? value;
				Expression source;
				if (property.CachedProperty == null) {
					if (!_filteringService.TryGetPropertyPath(property.Name, typeof(T), parameter,
						out source, out var type))
						return;
					// Cache compiled lambda?
					value = source.EvaluateGet(parameter, type, o);
				} else {
					value = property.CachedProperty.Get(o);
					source = Expression.Property(parameter, property.Name);
				}

				if (nextExpr == null)
					nextExpr = source.BuildComparisonFilterExpression(value, property.Order != SortOrder.DESC, true);
				else
					nextExpr = Expression.AndAlso(nextExpr,
						source.BuildComparisonFilterExpression(value, property.Order != SortOrder.DESC, true));
				if (prevExpr == null)
					prevExpr = source.BuildComparisonFilterExpression(value, property.Order == SortOrder.DESC, true);
				else
					prevExpr = Expression.AndAlso(prevExpr,
						source.BuildComparisonFilterExpression(value, property.Order == SortOrder.DESC, true));
			}

			var model = _context.Model.RequireEntityType(typeof(T));
			var pk = model.RequirePrimaryKey().Properties.Single(); // Must be single

			// Append negation to avoid fetching itself
			var v = pk.Get(o);
			var expr = Expression.Property(parameter, pk.Name)
				.BuildNotEqualsFilterExpression(v);
			// nextExpr and prevExpr should be not null as at least one ordering property should be provided
			// by either the filter model or defaults
			nextExpr = Expression.AndAlso(expr, nextExpr!);
			prevExpr = Expression.AndAlso(expr, prevExpr!);

			// Compile lambda expression and evaluate resulting values
			nextQuery = nextQuery.Where(Expression.Lambda<Func<T, bool>>(nextExpr, parameter));
			prevQuery = prevQuery.Where(Expression.Lambda<Func<T, bool>>(prevExpr, parameter));
			var next = await nextQuery.FirstOrDefaultAsync();
			if (next != null)
				Response.Headers["X-Navigation-Next"] = _filteringService.StringifyValue(pk.Get(next));
			var prev = await prevQuery.Reverse().FirstOrDefaultAsync();
			if (prev != null)
				Response.Headers["X-Navigation-Previous"] = _filteringService.StringifyValue(pk.Get(prev));
		}
	}
}