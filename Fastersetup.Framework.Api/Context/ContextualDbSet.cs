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

using System.Collections;
using System.Linq.Expressions;
using Fastersetup.Framework.Api.Data;
using Fastersetup.Framework.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Fastersetup.Framework.Api.Context {
	/// <summary>
	/// Represents a <see cref="DbSet{TEntity}"/> contextualized in a specified <see cref="Domain"/>.<br/>
	/// All data handled by this instance will (and are forced to) reference the given <see cref="Domain"/>
	/// </summary>
	/// <remarks>
	/// The main entity should implement <see cref="IDomainEntity"/> or an accessor expression to reach a navigable
	/// <see cref="IDomainEntity"/> must be provided.<br/>
	/// The supplied reference will be used for querying and to set the currently referenced <see cref="Domain"/>
	/// context as the <see cref="IDomainEntity"/>-referenced <see cref="IDomainEntity.Domain"/><br/>
	/// <b>NB:</b> multi-layered <see cref="IDomainEntity.Domain"/> references are not yet supported so all nested
	/// <see cref="Domain"/> references must have been already set beforehand
	/// </remarks>
	/// <typeparam name="TModel">Main model type</typeparam>
	public class ContextualDbSet<TModel> : IQueryable<TModel> where TModel : class {
		// Domain property reference and accessors (and expressions lambda model parameter)
		private readonly MemberExpression _property;
		private readonly ParameterExpression _modelParameter;
		private readonly Expression<Action<TModel, Domain?>> _setterExpression;
		private readonly Expression<Func<TModel, Domain?>> _getterExpression;
		// Compiled expressions cache
		private readonly Action<TModel, Domain?> _setter;
		private readonly Func<TModel, Domain?> _getter;
		// Underlying source set, used key and active domain provider
		private readonly DbSet<TModel> _source;
		private readonly IKey _pk;
		private readonly IActiveDomainProvider _provider;

		/// <summary>
		/// Initialize the contextual set with a <see cref="IDomainEntity"/>-implementing <typeparamref name="TModel"/><br/>
		/// Only the <typeparamref name="TModel"/> <see cref="IDomainEntity.Domain"/> will be referenced.
		/// </summary>
		/// <param name="source">Underlying <see cref="DbSet{TEntity}"/></param>
		/// <param name="pk">Used primary key (used to enumerate key properties during <see cref="Find"/> operations)</param>
		/// <param name="provider">Active current domain provider</param>
		/// <exception cref="ArgumentException">If <typeparamref name="TModel"/> does not implement <see cref="IDomainEntity"/></exception>
		public ContextualDbSet(DbSet<TModel> source, IKey pk, IActiveDomainProvider provider) {
			_source = source;
			_pk = pk;
			_provider = provider;
			if (!typeof(IDomainEntity).IsAssignableFrom(typeof(TModel)))
				throw new ArgumentException("Model must implement " + nameof(IDomainEntity) + " interface");
			var model = _modelParameter = Expression.Parameter(typeof(TModel), "model");
			var domain = Expression.Parameter(typeof(Domain), "domain");
			var property = _property = Expression.PropertyOrField(model, nameof(IDomainEntity.Domain));
			_setter = (_setterExpression = Expression.Lambda<Action<TModel, Domain?>>(
					Expression.Assign(property, domain),
					model, domain))
				.Compile();
			_getter = (_getterExpression = Expression.Lambda<Func<TModel, Domain?>>(property, model))
				.Compile();
		}

		/// <summary>
		/// Initialize the contextual set with a <typeparamref name="TModel"/> that depends form another entity
		/// implementing <see cref="IDomainEntity"/> and has a valid navigation property that references it.<br/>
		/// Only the <paramref name="accessor"/> referenced <see cref="IDomainEntity.Domain"/> will be used as reference.
		/// </summary>
		/// <param name="source">Underlying <see cref="DbSet{TEntity}"/></param>
		/// <param name="pk">Used primary key (used to enumerate key properties during <see cref="Find"/> operations)</param>
		/// <param name="provider">Active current domain provider</param>
		/// <param name="accessor"><see cref="IDomainEntity"/> direct navigation expression</param>
		public ContextualDbSet(DbSet<TModel> source, IKey pk, IActiveDomainProvider provider,
			Expression<Func<TModel, IDomainEntity>> accessor) {
			_source = source;
			_pk = pk;
			_provider = provider;
			var model = _modelParameter = Expression.Parameter(typeof(TModel), "model");
			var domain = Expression.Parameter(typeof(Domain), "domain");
			var property = _property = Expression.PropertyOrField(
				Expression.Invoke(accessor, _modelParameter), nameof(IDomainEntity.Domain));
			_setter = (_setterExpression = Expression.Lambda<Action<TModel, Domain?>>(
					Expression.Assign(property, domain),
					model, domain))
				.Compile();
			_getter = (_getterExpression = Expression.Lambda<Func<TModel, Domain?>>(property, model))
				.Compile();
		}

		/// <summary>
		/// Set context domain to the given <paramref name="model"/>
		/// </summary>
		/// <returns>The same <paramref name="model"/> reference</returns>
		/// <exception cref="NoActiveDomainException">If the provider service doesn't provide a context</exception>
		private TModel Contextualize(TModel model) {
			// TODO: recurse and set all missing domains in navigation properties
			_setter(model, _provider.GetActiveDomain() ?? throw new NoActiveDomainException());
			return model;
		}

		/// <inheritdoc cref="Contextualize(TModel)"/>
		/// <remarks>
		/// A <paramref name="preloadedDomain"/> can be provided to optimize the operation and avoid re-querying the
		/// provider service<br/>
		/// The <paramref name="preloadedDomain"/> must be fetched from the provider service during the same overall
		/// operation and shouldn't be cached
		/// </remarks>
		/// <exception cref="NoActiveDomainException">
		/// If no <paramref name="preloadedDomain"/> is supplied and the provider service doesn't provide a context
		/// </exception>
		private TModel Contextualize(TModel model, Domain? preloadedDomain) {
			// TODO: recurse and set all missing domains in navigation properties
			_setter(model, preloadedDomain ?? _provider.GetActiveDomain() ?? throw new NoActiveDomainException());
			return model;
		}

		/// <inheritdoc cref="Contextualize(TModel)"/>
		private async Task<TModel> ContextualizeAsync(TModel model, CancellationToken token = default) {
			// TODO: recurse and set all missing domains in navigation properties
			_setter(model, await _provider.GetActiveDomainAsync(token) ?? throw new NoActiveDomainException());
			return model;
		}

		/// <summary>
		/// Applies a query filter to the given <paramref name="model"/> query to filter out all entities not
		/// referencing the current <see cref="Domain"/>
		/// </summary>
		/// <exception cref="NoActiveDomainException">If the provider service doesn't provide a context</exception>
		private IQueryable<TModel> Contextualize(IQueryable<TModel> model) {
			var domain = _provider.GetActiveDomain() ?? throw new NoActiveDomainException();
			return model.Where(Expression.Lambda<Func<TModel, bool>>(
				Expression.Equal(_property, Expression.Constant(domain)), _modelParameter));
		}

		/// <summary>
		/// Checks if the given <paramref name="model"/> references the current <see cref="Domain"/>
		/// throwing an <see cref="UnauthorizedDomainException"/> if not
		/// </summary>
		/// <returns>The same <paramref name="model"/> reference</returns>
		/// <exception cref="UnauthorizedDomainException">
		/// If <paramref name="model"/> doesn't reference the current <see cref="Domain"/>
		/// </exception>
		private TModel VerifyContext(TModel model) {
			var domain = _getter(model);
			var current = _provider.GetActiveDomain();
			if (domain?.Id != current?.Id)
				throw new UnauthorizedDomainException(domain, current);
			return model;
		}

		/// <inheritdoc cref="VerifyContext(TModel)"/>
		private async Task<TModel> VerifyContextAsync(TModel model, CancellationToken token = default) {
			var domain = _getter(model);
			var current = await _provider.GetActiveDomainAsync(token);
			if (domain != current)
				throw new UnauthorizedDomainException(domain, current);
			return model;
		}

		/// <summary>
		/// Checks if the given <paramref name="model"/> references the current <see cref="Domain"/>
		/// </summary>
		/// <returns>True if the two domains match</returns>
		private bool TryVerifyContext(TModel model) {
			return _getter(model) == _provider.GetActiveDomain();
		}

		/// <inheritdoc cref="TryVerifyContext"/>
		private async Task<bool> TryVerifyContextAsync(TModel model, CancellationToken token) {
			return _getter(model) == await _provider.GetActiveDomainAsync(token);
		}

		/// <summary>
		/// Tries to resolve a <typeparamref name="TModel"/> identified by the given <paramref name="keys"/>
		/// </summary>
		/// <returns>The resolved <typeparamref name="TModel"/> instance or null if no entity was found</returns>
		/// <exception cref="UnauthorizedDomainException">
		/// If the resolved entity doesn't reference the current <see cref="Domain"/><br/>
		/// <br/>
		/// The exception is thrown because during the application regular flow the user should never reach a state
		/// where it tries to access another domain entity so if a malicious actor is trying to access unauthorized data
		/// and no access control was able to stop it the application flow will be forced to stop by the thrown exception.
		/// </exception>
		public TModel? Find(params object?[] keys) {
			var item = _source.Include(_getterExpression).FirstOrDefault(_pk.ResolveExpression<TModel>(keys));
			return item == null ? item : VerifyContext(item); // Use soft check instead and return null if fail?
		}

		/// <inheritdoc cref="Find"/>
		public async Task<TModel?> FindAsync(params object[] keys) {
			var item = await _source.Include(_getterExpression)
				.FirstOrDefaultAsync(_pk.ResolveExpression<TModel>(keys));
			return item == null ? item : await VerifyContextAsync(item);
		}

		/// <inheritdoc cref="Find"/>
		/// <remarks>A <paramref name="token"/> can be provided to handle operation cancellation</remarks>
		public async Task<TModel?> FindAsync(CancellationToken token, params object[] keys) {
			var item = await _source.Include(_getterExpression)
				.FirstOrDefaultAsync(_pk.ResolveExpression<TModel>(keys), token);
			return item == null ? item : await VerifyContextAsync(item, token);
		}

		/// <inheritdoc cref="FindAsync(CancellationToken, object[])"/>
		public async Task<TModel?> FindAsync(object?[] keys, CancellationToken token) {
			var item = await _source.Include(_getterExpression)
				.FirstOrDefaultAsync(_pk.ResolveExpression<TModel>(keys), token);
			return item == null ? item : await VerifyContextAsync(item, token);
		}

		/// <summary>
		/// Executes <see cref="DbSet{TEntity}.Add"/> after forcing the current <see cref="Domain"/> context on the
		/// given <paramref name="model"/>
		/// </summary>
		public EntityEntry<TModel> Add(TModel model) {
			return _source.Add(Contextualize(model));
		}

		/// <summary>
		/// Executes <see cref="DbSet{TEntity}.AddRange(TEntity[])"/> after forcing the current <see cref="Domain"/>
		/// context on all elements of the given <paramref name="source"/>
		/// </summary>
		public void AddRange(IEnumerable<TModel> source) {
			_source.AddRange(source.Select(Contextualize));
		}

		/// <summary>
		/// Executes <see cref="DbSet{TEntity}.AddAsync"/> after forcing the current <see cref="Domain"/> context on the
		/// given <paramref name="model"/>
		/// </summary>
		public async ValueTask<EntityEntry<TModel>> AddAsync(TModel model, CancellationToken token = default) {
			return await _source.AddAsync(await ContextualizeAsync(model, token), token);
		}

		/// <summary>
		/// Executes <see cref="DbSet{TEntity}.AddRangeAsync(TEntity[])"/> after forcing the current <see cref="Domain"/>
		/// context on all elements of the given <paramref name="source"/>
		/// </summary>
		public async Task AddRangeAsync(IEnumerable<TModel> source, CancellationToken token = default) {
			var domain = await _provider.GetActiveDomainAsync(token);
			await _source.AddRangeAsync(source.Select(s => Contextualize(s, domain)), token);
		}

		// Copying UnauthorizedDomainException exception definition from Find()
		/// <inheritdoc cref="Find"/>
		/// <summary>
		/// Executes <see cref="DbSet{TEntity}.Remove"/> after checking the current <see cref="Domain"/> context against the
		/// given <paramref name="model"/>
		/// </summary>
		/// <returns>The removed entity reference</returns>
		public EntityEntry<TModel> Remove(TModel model) {
			return _source.Remove(VerifyContext(model));
		}

		/// <inheritdoc cref="Remove"/>
		public async Task<EntityEntry<TModel>> RemoveAsync(TModel model, CancellationToken token = default) {
			return _source.Remove(await VerifyContextAsync(model, token));
		}

		#region IQueryable

		IEnumerator<TModel> IEnumerable<TModel>.GetEnumerator() => Contextualize(_source).GetEnumerator();
		IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable) Contextualize(_source)).GetEnumerator();
		Type IQueryable.ElementType => Contextualize(_source).ElementType;
		Expression IQueryable.Expression => Contextualize(_source).Expression;
		IQueryProvider IQueryable.Provider => Contextualize(_source).Provider;

		#endregion
	}
}