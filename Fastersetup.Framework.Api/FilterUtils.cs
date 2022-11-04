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
using System.Reflection;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace Fastersetup.Framework.Api {
	public static class FilterUtils {
		private static readonly PropertyInfo _efFunctions;
		private static readonly MethodInfo _likeMethod;
		private static readonly MethodInfo _orderBy;
		private static readonly MethodInfo _orderByDescending;
		private static readonly MethodInfo _thenBy;
		private static readonly MethodInfo _thenByDescending;
		private static readonly ImmutableDictionary<Type, MethodInfo> _compareToMethods;
		private static readonly MethodInfo _getValue;
		private static readonly char[] Wildcards = {'%', '_', '[', ']', '^'};
		private const char WildcardEscapeChar = '~';

		static FilterUtils() {
			_efFunctions = typeof(EF)
				               .GetProperty(nameof(EF.Functions), BindingFlags.Static | BindingFlags.Public)
			               ?? throw new InvalidOperationException("Failed to reflect EF.Functions property");
			_likeMethod = typeof(DbFunctionsExtensions)
				.GetMethod(nameof(DbFunctionsExtensions.Like), new[] {
					typeof(DbFunctions),
					typeof(string),
					typeof(string),
					typeof(string)
				}) ?? throw new InvalidOperationException("Failed to reflect EF.Functions.Like(...) method");

			_orderBy = new Func<
					IQueryable<object>,
					Expression<Func<object, object>>,
					IOrderedQueryable<object>>(Queryable.OrderBy)
				.GetMethodInfo()
				.GetGenericMethodDefinition();
			_orderByDescending = new Func<
					IQueryable<object>,
					Expression<Func<object, object>>,
					IOrderedQueryable<object>>(Queryable.OrderByDescending)
				.GetMethodInfo()
				.GetGenericMethodDefinition();
			_thenBy = new Func<
					IOrderedQueryable<object>,
					Expression<Func<object, object>>,
					IOrderedQueryable<object>>(Queryable.ThenBy)
				.GetMethodInfo()
				.GetGenericMethodDefinition();
			_thenByDescending = new Func<
					IOrderedQueryable<object>,
					Expression<Func<object, object>>,
					IOrderedQueryable<object>>(Queryable.ThenByDescending)
				.GetMethodInfo()
				.GetGenericMethodDefinition();
			_getValue = new Func<Expression, ParameterExpression, object, object?>(GetValue<object, object>)
				.GetMethodInfo()
				.GetGenericMethodDefinition();
			var dict = new Dictionary<Type, MethodInfo>();

			void Add<T>(Func<T, int> fun) {
				dict.Add(typeof(T), fun.GetMethodInfo());
			}

			Add<string>("".CompareTo);
			Add<Guid>(Guid.Empty.CompareTo);
			Add<DateTime>(DateTime.MinValue.CompareTo);
			Add<DateTimeOffset>(DateTimeOffset.MinValue.CompareTo);
			_compareToMethods = dict.ToImmutableDictionary();
		}

		private static object? GetValue<TModel, TProperty>(
			Expression expression, ParameterExpression parameter, TModel value) {
			return Expression.Lambda<Func<TModel, TProperty>>(expression, parameter).Compile()(value);
		}

		public static object? EvaluateGet<T>(this Expression expression, ParameterExpression parameter, Type resultType,
			T value) {
			return _getValue.MakeGenericMethod(typeof(T), resultType)
				.Invoke(null, new object?[] {expression, parameter, value});
		}

		private static string PrepareFilter(string s, bool before, bool after) {
			var idx = -1;
			var offset = 0;
			var sb = new StringBuilder(s);
			// Projecting the original string indexes onto the altered StringBuilder by keeping track of the
			// added character number and considering it while calculating the next escape character position
			while ((idx = s.IndexOfAny(Wildcards, idx + 1)) >= 0)
				sb.Insert(idx + offset++, WildcardEscapeChar); // Add escape string before character

			if (before)
				sb.Insert(0, '%');
			if (after)
				sb.Append('%');
			return sb.ToString();
		}

		public static MethodCallExpression BuildLikeFilterExpression(
			this Expression member, string filter, bool before, bool after) {
			return Expression.Call(null, _likeMethod,
				Expression.Property(null, _efFunctions),
				member,
				Expression.Constant(PrepareFilter(filter, before, after), typeof(string)),
				Expression.Constant(WildcardEscapeChar.ToString(), typeof(string)));
		}

		public static BinaryExpression BuildEqualsFilterExpression(
			this Expression member, object? value) {
			return Expression.Equal(
				member,
				Expression.Constant(value, member.Type));
		}

		public static BinaryExpression BuildEqualsFilterExpression(
			this Expression member, object? value, Type type) {
			return Expression.Equal(
				member.Type == type ? member : Expression.TypeAs(member, type), // TODO: Is it ok to null it if invalid?
				Expression.Constant(value, type));
		}

		public static BinaryExpression BuildNotEqualsFilterExpression(
			this Expression member, object? value) {
			return Expression.NotEqual(
				member,
				Expression.Constant(value, member.Type));
		}

		public static BinaryExpression BuildNotEqualsFilterExpression(
			this Expression member, object? value, Type type) {
			return Expression.NotEqual(
				member.Type == type ? member : Expression.TypeAs(member, type), // TODO: Is it ok to null it if invalid?
				Expression.Constant(value, type));
		}

		public static BinaryExpression BuildComparisonFilterExpression(
			this Expression member, object? value, bool greater, bool equal) {
			var v = Expression.Constant(value, member.Type);
			// Unsupported binary equality (e.g. strings)
			if (_compareToMethods.TryGetValue(member.Type, out var compareToMethod)) {
				/*member = Expression.Call(null, _stringCompare,
					member, v, Expression.Constant(StringComparison.Ordinal, typeof(StringComparison)));*/
				member = Expression.Call(member, compareToMethod, v);
				v = Expression.Constant(0, typeof(int));
			}

			if (greater)
				if (equal)
					return Expression.GreaterThanOrEqual(member, v);
				else
					return Expression.GreaterThan(member, v);
			if (equal)
				return Expression.LessThanOrEqual(member, v);
			return Expression.LessThan(member, v);
		}

		public static Expression<Func<T, bool>> BuildLikeFilter<T>(
			this Expression member, ParameterExpression parameter, string filter, bool before, bool after) {
			return Expression.Lambda<Func<T, bool>>(
				member.BuildLikeFilterExpression(filter, before, after), parameter);
		}

		public static Expression<Func<T, bool>> BuildEqualsFilter<T>(this Expression member,
			ParameterExpression parameter, object? filter) {
			return Expression.Lambda<Func<T, bool>>(member.BuildEqualsFilterExpression(filter), parameter);
		}

		public static Expression<Func<T, bool>> BuildNotEqualsFilter<T>(this Expression member,
			ParameterExpression parameter, object? filter) {
			return Expression.Lambda<Func<T, bool>>(member.BuildNotEqualsFilterExpression(filter), parameter);
		}

		public static Expression<Func<T, bool>> BuildComparisonFilter<T>(
			this Expression member, ParameterExpression parameter, object? filter, bool greater, bool equal) {
			return Expression.Lambda<Func<T, bool>>(
				member.BuildComparisonFilterExpression(filter, greater, equal), parameter);
		}

		public static LambdaExpression ToExpression(this PropertyInfo property, Type? declaringType = null) {
			var p = Expression.Parameter(declaringType ?? property.DeclaringType!, nameof(ToExpression) + "_o");
			return Expression.Lambda(Expression.Property(p, property), p);
		}

		public static IOrderedQueryable<T> OrderByProperty<T>(
			this IQueryable<T> source, PropertyInfo property, bool desc) {
			return (IOrderedQueryable<T>) source.Provider.CreateQuery<T>(
				Expression.Call(null,
					(desc ? _orderByDescending : _orderBy).MakeGenericMethod(typeof(T), property.PropertyType),
					source.Expression,
					Expression.Quote(property.ToExpression())));
		}

		public static IOrderedQueryable<T> OrderByProperty<T>(
			this IQueryable<T> source, LambdaExpression expression, Type propertyType, bool desc) {
			return (IOrderedQueryable<T>) source.Provider.CreateQuery<T>(
				Expression.Call(null,
					(desc ? _orderByDescending : _orderBy).MakeGenericMethod(typeof(T), propertyType),
					source.Expression,
					Expression.Quote(expression)));
		}

		public static IOrderedQueryable<T> ThenByProperty<T>(
			this IOrderedQueryable<T> source, PropertyInfo property, bool desc) {
			return (IOrderedQueryable<T>) source.Provider.CreateQuery<T>(
				Expression.Call(null,
					(desc ? _thenByDescending : _thenBy).MakeGenericMethod(typeof(T), property.PropertyType),
					source.Expression,
					Expression.Quote(property.ToExpression())));
		}

		public static IOrderedQueryable<T> ThenByProperty<T>(
			this IOrderedQueryable<T> source, LambdaExpression expression, Type propertyType, bool desc) {
			return (IOrderedQueryable<T>) source.Provider.CreateQuery<T>(
				Expression.Call(null,
					(desc ? _thenByDescending : _thenBy).MakeGenericMethod(typeof(T), propertyType),
					source.Expression,
					Expression.Quote(expression)));
		}
	}
}