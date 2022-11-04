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
using Fastersetup.Framework.Api.Context;
using Fastersetup.Framework.Api.Controllers.Filtering;
using Fastersetup.Framework.Api.Controllers.Models;
using Fastersetup.Framework.Api.Data;
using Fastersetup.Framework.Api.Services;
using Fastersetup.Framework.Api.Services.Utilities;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Fastersetup.Framework.Api {
	public class ApiControllerBase<TModel> : ControllerBase
		where TModel : class, new() {
		private static readonly bool Contextualize = typeof(TModel).IsAssignableTo(typeof(IDomainEntity));
		private readonly DbContext _context;
		private readonly FilteringService _filteringService;
		protected readonly IObjectUtils Utils;
		private readonly IAccessControlService<TModel>? _aclService;
		private readonly ILogger _logger;

		public delegate bool ValueDeserializer(string? value, out IComparable? result);

		public delegate bool ValueDeserializer<T>(string? value, out T? result);

		protected readonly Dictionary<Type, ValueDeserializer> Conversions = new();

		[NotNull]
		protected virtual DbSet<TModel> Set => _context.Set<TModel>();
		protected virtual ContextualDbSet<TModel>? ContextualSet =>
			Contextualize && _context is IContextualDbContext contextual
				? contextual.Set<TModel>()
				: null;
		protected virtual IQueryable<TModel> Source => ((IQueryable<TModel>?) ContextualSet ?? Set).AsNoTracking();
		protected virtual IQueryable<TModel> ListSource => Source;
		protected virtual IQueryable<TModel> CrudSource => Source;
		protected virtual IQueryable<TModel> ReadSource => CrudSource;
		protected virtual IQueryable<TModel> EditSource => CrudSource;
		protected virtual IQueryable<TModel> DeleteSource =>
			// Delete entity loading optimization. Override with CrudSource if filtering is applied
			Source;
		protected IAccessControlService<TModel>? Acl => _aclService;

		public ApiControllerBase(DbContext context, FilteringService filteringService, IObjectUtils utils,
			ILogger logger, IAccessControlService<TModel>? aclService = null) {
			_context = context;
			_filteringService = filteringService;
			Utils = utils;
			_aclService = aclService;
			_logger = logger;
		}

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

		protected virtual (string Name, object Value)[] GetPrimaryKeysRef(TModel model) {
			var props = _context.Model.RequirePrimaryKey(typeof(TModel)).Properties;
			var keys = new (string Name, object Value)[props.Count];
			for (var i = 0; i < props.Count; i++) {
				var key = props[i];
				if (key.FieldInfo != null)
					keys[i] = (key.Name, key.FieldInfo.GetValue(model));
				else
					keys[i] = (key.Name, key.PropertyInfo.GetValue(model));
			}

			return keys;
		}

		/*protected virtual object[] GetPrimaryKeys(TModel model) {
			var props = _context.Model.FindEntityType(typeof(TModel)).FindPrimaryKey().Properties;
			var keys = new object[props.Count];
			for (var i = 0; i < props.Count; i++) {
				var key = props[i];
				if (key.FieldInfo != null)
					keys[i] = key.FieldInfo.GetValue(model);
				else
					keys[i] = key.PropertyInfo.GetValue(model);
			}

			return keys;
		}*/

		protected virtual Task<TModel> Copy(TModel source, TModel target) {
			return CopyNavigationProperties(source, Utils.Copy(source, target));
		}

		protected virtual Task<TModel> CopyNavigationProperties(TModel source, TModel target) {
			return Task.FromResult(target);
		}

		private IActionResult? TryGetPropertyPath(string name, ParameterExpression parameter,
			out Expression exp, out Type propertyType) {
			return _filteringService.TryGetPropertyPath(name, typeof(TModel), parameter, out exp, out propertyType)
				? null
				: BadRequest(new ErrorResponse($"Invalid property name \"{name}\""));
		}

		protected virtual Task<TModel?> Resolve(IQueryable<TModel> q, TModel reference, out object[] pks) {
			var r = GetPrimaryKeysRef(reference);
			var p = Expression.Parameter(typeof(TModel), "o");
			// var e = Expression.MakeMemberAccess(p, r[0].Member)
			var e = Expression.PropertyOrField(p, r[0].Name)
				.BuildEqualsFilterExpression(r[0].Value);
			pks = new object[r.Length];
			pks[0] = r[0].Value;
			if (r.Length > 1)
				for (var i = 1; i < r.Length; i++) {
					var (name, value) = r[i];
					pks[i] = value;
					// e = Expression.And(e, Expression.MakeMemberAccess(p, member)
					e = Expression.And(e, Expression.PropertyOrField(p, name)
						.BuildEqualsFilterExpression(value));
				}

			return q.FirstOrDefaultAsync(Expression.Lambda<Func<TModel, bool>>(e, p), HttpContext.RequestAborted);
		}

		/// <remarks>The collection MUST be always not empty</remarks>
		protected virtual IEnumerable<PropertyOrder> EnumerateDefaultOrderBy() {
			return _context.Model.RequireEntityType(typeof(TModel))
				.RequirePrimaryKey()
				.Properties
				.Select(p => {
					var m = p.GetMember();
					return m == null
						? null
						: new PropertyOrder() {
							Name = m.Name,
							Order = SortOrder.ASC,
							CachedProperty = p
						};
				})
				.Where(p => p != null)!;
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
							error = Conflict(new ErrorResponse(errorMessage));
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
						error = Conflict(new ErrorResponse(errorMessage));
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

		[HttpPut]
		public virtual async Task<IActionResult> Create([FromBody] TModel model) {
			if (!ModelState.IsValid)
				return ValidationProblem();
			var o = await Copy(model, new TModel());
			var set = ContextualSet;
			var entity = set == null
				? await Set.AddAsync(o, HttpContext.RequestAborted)
				: await set.AddAsync(o, HttpContext.RequestAborted);
			if (_aclService != null)
				await _aclService.Create(entity.Entity, model);
			try {
				await _context.SaveChangesAsync(HttpContext.RequestAborted);
				return Ok(entity.Entity);
			} catch (DbUpdateException ex) {
				// Show IDs?
				_logger.LogError(ex, "Failed to create {Model}", model.GetType().FullName);
				return StatusCode(500, new ErrorResponse("Failed to update the model"));
			}
		}

		[HttpPatch]
		public virtual async Task<IActionResult> Update([FromBody] TModel model) {
			if (!ModelState.IsValid)
				return ValidationProblem();
			var v = await Resolve(EditSource.AsTracking(), model, out var keys);
			if (v == null) {
				_logger.LogDebug("No entity was found matching {@Keys} [{@Model}]", keys, model);
				return NotFound(new NotFoundResponse());
			}

			if (_aclService != null)
				await _aclService.Edit(v, model);
			var o = await Copy(model, v);
			try {
				await _context.SaveChangesAsync(HttpContext.RequestAborted);
				return Ok(o);
			} catch (DbUpdateException ex) {
				// Show IDs?
				_logger.LogError(ex, "Failed to update {Model}", model.GetType().FullName);
				return StatusCode(500, new ErrorResponse("Failed to update the model"));
			}
		}

		[HttpDelete]
		public virtual async Task<IActionResult> Delete([FromBody] TModel model) {
			var v = await Resolve(DeleteSource.AsTracking(), model, out var keys);
			if (v == null) {
				_logger.LogDebug("No entity was found matching {@Keys} [{@Model}]", keys, model);
				return NotFound(new NotFoundResponse());
			}

			if (_aclService != null)
				await _aclService.Delete(v, model);
			var set = ContextualSet;
			if (set == null)
				Set.Remove(v);
			else
				await set.RemoveAsync(v, HttpContext.RequestAborted);
			try {
				await _context.SaveChangesAsync(HttpContext.RequestAborted);
				return Ok();
			} catch (DbUpdateException ex) {
				// Show IDs?
				_logger.LogError(ex, "Failed to delete {Model}", model.GetType().FullName);
				return StatusCode(500, new ErrorResponse("Failed to delete the model"));
			}
		}
	}
}