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

using System.Collections.Immutable;
using System.Linq.Expressions;
using Fastersetup.Framework.Api.Controllers.Filtering;
using Fastersetup.Framework.Api.Controllers.Models;
using Fastersetup.Framework.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Fastersetup.Framework.Api {
	public abstract class ReadonlyApiController<TModel> : ControllerBase
		where TModel : class, new() {
		private readonly FilteringService _filteringService;

		public delegate bool ValueDeserializer(string? value, out IComparable? result);

		public delegate bool ValueDeserializer<T>(string? value, out T? result);

		protected readonly Dictionary<Type, ValueDeserializer> Conversions = new();
		protected abstract IQueryable<TModel> Source { get; }
		protected virtual IQueryable<TModel> UntrackedSource => Source.AsNoTracking();
		protected virtual IQueryable<TModel> ListSource => UntrackedSource;
		protected virtual IQueryable<TModel> CrudSource => UntrackedSource;

		protected ReadonlyApiController(FilteringService filteringService) {
			_filteringService = filteringService;
		}

		/// <remarks>The collection MUST be always not empty</remarks>
		protected abstract IEnumerable<PropertyOrder> EnumerateDefaultOrderBy();

		protected virtual void RegisterConversion<T>(ValueDeserializer<T> converter) where T : struct {
			Conversions.Add(typeof(T), (string? value, out IComparable? result) => {
				if (converter.Invoke(value, out var o)) {
					result = (IComparable) o;
					return true;
				}

				result = default;
				return false;
			});
			Conversions.Add(typeof(T?), (string? value, out IComparable? result) => {
				if (string.IsNullOrWhiteSpace(value)) {
					result = null;
					return true;
				}

				if (converter.Invoke(value, out var o)) {
					result = (IComparable) o;
					return true;
				}

				result = default;
				return false;
			});
		}

		protected virtual void RegisterConverters() {
			RegisterConversion((string? value, out short result) => short.TryParse(value, out result));
			RegisterConversion((string? value, out int result) => int.TryParse(value, out result));
			RegisterConversion((string? value, out long result) => long.TryParse(value, out result));
			RegisterConversion((string? value, out ushort result) => ushort.TryParse(value, out result));
			RegisterConversion((string? value, out uint result) => uint.TryParse(value, out result));
			RegisterConversion((string? value, out ulong result) => ulong.TryParse(value, out result));
			RegisterConversion((string? value, out DateTime result) => DateTime.TryParse(value, out result));
			RegisterConversion((string? value, out float result) => float.TryParse(value, out result));
			RegisterConversion((string? value, out double result) => double.TryParse(value, out result));
			RegisterConversion((string? value, out decimal result) => decimal.TryParse(value, out result));
		}

		protected virtual bool ToComparable(Expression member, string value, out IComparable? result) {
			if (Conversions.Count == 0)
				RegisterConverters();
			if (!Conversions.TryGetValue(member.Type, out var converter)) {
				result = null;
				return false;
			}

			return converter(value, out result);
		}

		private IActionResult? TryGetPropertyPath(string name, ParameterExpression parameter,
			out Expression exp, out Type propertyType) {
			return _filteringService.TryGetPropertyPath(name, typeof(TModel), parameter, out exp, out propertyType)
				? null
				: BadRequest(new ErrorResponse($"Invalid property name \"{name}\""));
		}

		protected IQueryable<TModel>? PrepareQueryFilter(IQueryable<TModel> q, FilterModel filter,
			out IActionResult? error) {
			foreach (var f in filter.Filters ?? Enumerable.Empty<PropertyFilter>()) {
				var parameter = Expression.Parameter(typeof(TModel), "o");
				error = TryGetPropertyPath(f.Name, parameter, out var accessor, out var propertyType);
				if (error != null)
					return null;

				if (f.Values is {Count: > 0}) {
					Expression? exp = null;
					/***
					 * Normalize values to be compared to the stored format
					 */
					if (propertyType == typeof(Guid) || propertyType == typeof(Guid?))
						for (var i = 0; i < f.Values.Count; i++)
							if (Guid.TryParse(f.Values[i], out var guid))
								f.Values[i] = guid.ToString("N");
					/***
					 * Build comparison expression
					 */
					foreach (var value in f.Values) {
						if (!_filteringService.TryBuildFilterExpression(f.Name, f.Action, value,
							ToComparable, accessor, propertyType, out var expression, out var errorMessage)) {
							error = Conflict(new ErrorResponse(errorMessage ?? string.Empty));
							return null;
						}

						exp = exp == null ? expression : Expression.OrElse(exp, expression!);
					}

					q = q.Where(Expression.Lambda<Func<TModel, bool>>(exp!, parameter));
				} else {
					/***
					 * Normalize values to be compared to the stored format
					 */
					if ((propertyType == typeof(Guid) || propertyType == typeof(Guid?))
					    && Guid.TryParse(f.Value, out var guid))
						f.Value = guid.ToString("N");
					/***
					 * Build comparison expression
					 */
					if (!_filteringService.TryBuildFilterExpression(f.Name, f.Action, f.Value,
						ToComparable, accessor, propertyType, out var expression, out var errorMessage)) {
						error = Conflict(new ErrorResponse(errorMessage ?? string.Empty));
						return null;
					}

					q = q.Where(Expression.Lambda<Func<TModel, bool>>(expression!, parameter));
				}
			}

			error = null;
			return q;
		}

		protected IQueryable<TModel>? AppendSorting(IQueryable<TModel> q, FilterModel filter, out IActionResult? error) {
			var entries =
				(filter.Order is {Count: > 0} ? filter.Order : EnumerateDefaultOrderBy()).ToImmutableList();
			var p = Expression.Parameter(typeof(TModel), "o");
			var r = TryGetPropertyPath(entries[0].Name, p, out var exp, out var propertyType);
			if (r != null) {
				error = r;
				return null;
			}

			/*if (propertyType == typeof(string))
				exp = Expression.Call(exp,
					((Func<int, int, string>) "".Substring).Method,
					Expression.Constant(0), Expression.Constant(32));*/
			var o = q.OrderByProperty(Expression.Lambda(exp, p), propertyType, entries[0].Order == SortOrder.DESC);
			if (entries.Count > 1)
				foreach (var entry in entries.Skip(1)) {
					p = Expression.Parameter(typeof(TModel), "o");
					r = TryGetPropertyPath(entry.Name, p, out exp, out propertyType);
					if (r != null) {
						error = r;
						return null;
					}

					/*if (propertyType == typeof(string))
						exp = Expression.Call(exp,
							((Func<int, int, string>) "".Substring).Method,
							Expression.Constant(0), Expression.Constant(32));*/
					o = o.ThenByProperty(Expression.Lambda(exp, p), propertyType, entry.Order == SortOrder.DESC);
				}

			q = o;

			if (filter.Length > 0) {
				if (filter.Page > 0)
					q = q.Skip((int) (filter.Page * filter.Length));
				q = q.Take((int) filter.Length);
				Response.Headers["X-Offset"] = (filter.Page * filter.Length).ToString();
			} else
				Response.Headers["X-Offset"] = "0";

			error = null;
			return q;
		}

		[HttpGet]
		public virtual async Task<IActionResult> List([FromQuery] FilterModel filter) {
			var q = PrepareQueryFilter(ListSource, filter, out var r);
			if (q == null)
				return r ?? StatusCode(418);

			Response.Headers["X-Count"] = (await q.LongCountAsync(HttpContext.RequestAborted)).ToString();

			q = AppendSorting(q, filter, out r);
			if (q == null)
				return r ?? StatusCode(418);
			return Ok(await q.TagWith(RemoveLastOrderByInterceptor.QueryTag).ToListAsync(HttpContext.RequestAborted));
		}
	}
}