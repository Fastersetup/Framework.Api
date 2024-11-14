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

using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using Fastersetup.Framework.Api.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Fastersetup.Framework.Api {
	public static class EFUtils {
		/// <summary>
		/// Resolves a tracked <typeparamref name="T"/> <paramref name="context"/> instance extracting its primary keys
		/// from the given <paramref name="item"/><br/>
		/// If no <typeparamref name="T"/> instance can be resolved with the extracted keys the <paramref name="item"/>
		/// instance will be added to <paramref name="context"/>
		/// </summary>
		[return: NotNullIfNotNull(nameof(item))]
		public static T? Resolve<T>(this DbContext context, T? item) where T : class {
			if (item == null)
				return item;
			var pks = context.GetPrimaryKeysRef(item).Select(t => t.Value).ToArray();
			if (context is IContextualDbContext ctx) {
				var set = ctx.Set<T>();
				return set.Find(pks) ?? set.Add(item).Entity;
			} else {
				var set = context.Set<T>();
				return set.Find(pks) ?? set.Add(item).Entity;
			}
		}

		/// <inheritdoc cref="Resolve{T}"/>
		public static async ValueTask<T?> ResolveAsync<T>(this DbContext context, T? item,
			CancellationToken token = default)
			where T : class {
			if (item == null)
				return item;
			var pks = context.GetPrimaryKeysRef(item).Select(t => t.Value).ToArray();
			if (context is IContextualDbContext ctx) {
				var set = ctx.Set<T>();
				return await set.FindAsync(pks, token)
				       ?? (await set.AddAsync(item, token)).Entity;
			} else {
				var set = context.Set<T>();
				return await set.FindAsync(pks, token)
				       ?? (await set.AddAsync(item, token)).Entity;
			}
		}

		/// <inheritdoc cref="ResolveExpression{T}(IKey, object[])"/>
		public static Expression<Func<T, bool>> ResolveExpression<T>(this DbContext context, object[] keys)
			where T : class {
			return context.Model.RequirePrimaryKey(typeof(T)).ResolveExpression<T>(keys);
		}

		/// <summary>
		/// Creates a new lambda expression that compares <typeparamref name="T"/> primary keys with the given
		/// <paramref name="keys"/> values and return true if the values are equal
		/// </summary>
		/// <remarks>Useful for querying a specific <typeparamref name="T"/> entry when working with generic types</remarks>
		public static Expression<Func<T, bool>> ResolveExpression<T>(this IKey pk, params object?[] keys)
			where T : class {
			var p = Expression.Parameter(typeof(T), "o");
			var properties = pk.Properties;
			var member = properties[0].GetMember();
			if (member == null)
				throw new NotSupportedException(
					$"No member found for primary key property {properties[0].DeclaringType.Name}.{properties[0].Name}");
			var e = Expression.MakeMemberAccess(p, member)
				.BuildEqualsFilterExpression(keys[0]);
			if (properties.Count > 1)
				for (var i = 1; i < properties.Count; i++) {
					member = properties[i].GetMember();
					if (member == null)
						throw new NotSupportedException(
							$"No member found for primary key property {properties[i].DeclaringType.Name}.{properties[i].Name}");
					e = Expression.And(e, Expression.MakeMemberAccess(p, member)
						.BuildEqualsFilterExpression(keys[i]));
				}

			return Expression.Lambda<Func<T, bool>>(e, p);
		}

		/// <summary>
		/// Requires the given <paramref name="model"/> to contain the given <paramref name="type"/> entity type or
		/// throws an <see cref="ArgumentException"/>
		/// </summary>
		/// <returns>The entity type definition</returns>
		/// <exception cref="ArgumentNullException">If any parameter is null</exception>
		/// <exception cref="ArgumentException">If <paramref name="model"/> doesn't define the given <paramref name="type"/></exception>
		public static IEntityType RequireEntityType(this IModel model, Type type) {
			if (model == null) throw new ArgumentNullException(nameof(model));
			if (type == null) throw new ArgumentNullException(nameof(type));
			return model.FindEntityType(type)
			       ?? throw new ArgumentException($"Type {type.FullName} is not a valid tracked entity", nameof(type));
		}

		/// <summary>
		/// Requires the given <paramref name="entity"/> to provide a primary <see cref="IKey"/> or throws
		/// an <see cref="ArgumentException"/>
		/// </summary>
		/// <returns>The primary <see cref="IKey"/> definition</returns>
		/// <exception cref="ArgumentNullException">If any parameter is null</exception>
		/// <exception cref="ArgumentException">If <paramref name="entity"/> doesn't define a primary key</exception>
		public static IKey RequirePrimaryKey(this IEntityType entity) {
			if (entity == null) throw new ArgumentNullException(nameof(entity));
			return entity.FindPrimaryKey()
			       ?? throw new ArgumentException(
				       $"Type {entity.ClrType.FullName} doesn't have a configured primary key", nameof(entity));
		}

		/// <summary>
		/// Requires the given <paramref name="model"/> to define the given <paramref name="type"/> providing
		/// a primary <see cref="IKey"/> for the mapped entity or throws an <see cref="ArgumentException"/>
		/// </summary>
		/// <returns>The primary <see cref="IKey"/> definition</returns>
		/// <exception cref="ArgumentNullException">If any parameter is null</exception>
		/// <exception cref="ArgumentException">
		/// If <paramref name="model"/> doesn't define the given <paramref name="type"/> or the model definition
		/// doesn't provide a primary <see cref="IKey"/>
		/// </exception>
		public static IKey RequirePrimaryKey(this IModel model, Type type) {
			return model.RequireEntityType(type).RequirePrimaryKey();
		}

		/// <summary>
		/// Gets <typeparamref name="T"/> definition from the given <paramref name="context"/> to identify its primary
		/// key and return a collection of properties name and value for the given <paramref name="model"/> instance<br/>
		/// See <see cref="GetKeyRef"/>
		/// </summary>
		/// <exception cref="ArgumentNullException">If any parameter is null</exception>
		/// <remarks>
		/// The method values could result null on primary key properties not mappable to valid
		/// <see cref="FieldInfo"/> or <see cref="PropertyInfo"/>
		/// </remarks>
		public static (string Name, object? Value)[] GetPrimaryKeysRef<T>(this DbContext context, T model) {
			if (model == null) throw new ArgumentNullException(nameof(model));
			return context.GetPrimaryKeysRef(typeof(T), model);
		}

		/// <summary>
		/// Gets <paramref name="type"/> definition from the given <paramref name="context"/> to identify its primary
		/// key and returns a collection of properties name and value for the given <paramref name="model"/> instance<br/>
		/// See <see cref="GetKeyRef"/>
		/// </summary>
		/// <exception cref="ArgumentNullException">If any parameter is null</exception>
		/// <remarks>
		/// The method values could result null on primary key properties not mappable to valid
		/// <see cref="FieldInfo"/> or <see cref="PropertyInfo"/>
		/// </remarks>
		public static (string Name, object? Value)[]
			GetPrimaryKeysRef(this DbContext context, Type type, object model) {
			if (type == null) throw new ArgumentNullException(nameof(type));
			if (model == null) throw new ArgumentNullException(nameof(model));
			return context.Model.RequirePrimaryKey(type).GetKeyRef(model);
		}

		/// <summary>
		/// Gets a collection of the properties from the given <paramref name="key"/> paired with the property values
		/// gathered from the <paramref name="model"/> instance
		/// </summary>
		/// <exception cref="ArgumentNullException">If any parameter is null</exception>
		/// <remarks>
		/// The method values could result null on <paramref name="key"/> properties not mappable to valid
		/// <see cref="FieldInfo"/> or <see cref="PropertyInfo"/>
		/// </remarks>
		public static (string Name, object? Value)[] GetKeyRef(this IKey key, object model) {
			if (key == null) throw new ArgumentNullException(nameof(key));
			if (model == null) throw new ArgumentNullException(nameof(model));
			var props = key.Properties;
			var keys = new (string Name, object? Value)[props.Count];
			for (var i = 0; i < props.Count; i++) {
				var k = props[i];
				keys[i] = (k.Name, k.Get(model));
			}

			return keys;
		}

		// Inheriting property mapping warning
		/// <inheritdoc cref="GetMember"/>
		/// <summary>
		/// Gets a custom attribute defined on the given <paramref name="property"/><br/>
		/// <see cref="CustomAttributeExtensions.GetCustomAttribute{T}(System.Reflection.Assembly)"/> is used to get
		/// the attribute instance
		/// </summary>
		/// <returns>
		/// <typeparamref name="T"/> custom attribute instance if found or null if the property didn't have a
		/// <typeparamref name="T"/> attribute or <paramref name="property"/> is invalid (see remarks)
		/// </returns>
		public static T? GetAttribute<T>(this IReadOnlyPropertyBase property) where T : Attribute {
			var p = property.PropertyInfo;
			return p == null
				? property.FieldInfo?.GetCustomAttribute<T>()
				: p.GetCustomAttribute<T>();
		}

		/// <summary>
		/// Gets <paramref name="property"/> referenced <see cref="MemberInfo"/> definition
		/// </summary>
		/// <returns>The mapped <see cref="MemberInfo"/> reference or null (see remarks)</returns>
		/// <remarks>
		/// If the given <paramref name="property"/> isn't mappable to a valid <see cref="FieldInfo"/> or
		/// <see cref="PropertyInfo"/> the method will always do nothing and return null
		/// </remarks>
		public static MemberInfo? GetMember(this IReadOnlyPropertyBase property) {
			return (MemberInfo?) property.PropertyInfo ?? property.FieldInfo;
		}

		// Inheriting property mapping warning
		/// <inheritdoc cref="GetMember"/>
		/// <summary>
		/// Gets a <paramref name="property"/> value from the given object <paramref name="instance"/>
		/// </summary>
		/// <returns>The <paramref name="property"/> <paramref name="instance"/> value or null (see remarks)</returns>
		public static object? Get(this IReadOnlyPropertyBase property, object instance) {
			var p = property.PropertyInfo;
			return p == null
				? property.FieldInfo?.GetValue(instance)
				: p.GetValue(instance);
		}

		// Inheriting property mapping warning
		/// <inheritdoc cref="GetMember"/>
		/// <summary>
		/// Sets a <paramref name="property"/> value to <paramref name="value"/> in the given object <paramref name="instance"/>
		/// </summary>
		public static void Set(this IReadOnlyPropertyBase property, object instance, object value) {
			var p = property.PropertyInfo;
			if (p == null)
				property.FieldInfo?.SetValue(instance, value);
			else
				p.SetValue(instance, value);
		}
	}
}