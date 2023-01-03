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
using Fastersetup.Framework.Api.Context;
using Fastersetup.Framework.Api.Controllers.Models;
using Fastersetup.Framework.Api.Data;
using Fastersetup.Framework.Api.Services;
using Fastersetup.Framework.Api.Services.Utilities;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Fastersetup.Framework.Api {
	public class ApiController<TModel> : ReadonlyApiController<TModel>
		where TModel : class, new() {
		private static readonly bool Contextualize = typeof(TModel).IsAssignableTo(typeof(IDomainEntity));
		private readonly DbContext _context;
		protected readonly IObjectUtils Utils;
		private readonly IAccessControlService<TModel>? _aclService;
		private readonly ILogger _logger;

		[NotNull]
		protected virtual DbSet<TModel> Set => _context.Set<TModel>();
		protected virtual ContextualDbSet<TModel>? ContextualSet =>
			Contextualize && _context is IContextualDbContext contextual
				? contextual.Set<TModel>()
				: null;
		protected override IQueryable<TModel> Source => ((IQueryable<TModel>?) ContextualSet ?? Set).AsNoTracking();
		protected virtual IQueryable<TModel> ReadSource => CrudSource;
		protected virtual IQueryable<TModel> EditSource => CrudSource;
		protected virtual IQueryable<TModel> DeleteSource =>
			// Delete entity loading optimization. Override with CrudSource if filtering is applied
			Source;
		protected IAccessControlService<TModel>? Acl => _aclService;

		public ApiController(DbContext context, FilteringService filteringService, IObjectUtils utils,
			ILogger logger, IAccessControlService<TModel>? aclService = null) : base(context, filteringService) {
			_context = context;
			Utils = utils;
			_aclService = aclService;
			_logger = logger;
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

		protected virtual Task<TModel> Copy(TModel source, TModel target) {
			return CopyNavigationProperties(source, Utils.Copy(source, target));
		}

		protected virtual Task<TModel> CopyNavigationProperties(TModel source, TModel target) {
			return Task.FromResult(target);
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