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

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using Fastersetup.Framework.Api.Attributes.Security;
using Fastersetup.Framework.Api.Controllers.Filtering;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Logging;

namespace Fastersetup.Framework.Api.Services {
	public delegate bool ToComparable(Expression member, string? value, out IComparable? result);

	public class FilteringService {
		private const string InvalidActionMessage = "Unsupported action {0} performed on property \"{1}\"";
		private static readonly ConcurrentDictionary<Type, LambdaExpression> EnumCache = new();
		private readonly DbContext _context;
		private readonly ILogger<FilteringService> _logger;

		public FilteringService(DbContext context, ILogger<FilteringService> logger) {
			_context = context;
			_logger = logger;
		}

		public bool TryBuildFilterExpression(string name, FilterAction action, string? value, bool extended,
			ToComparable toComparable, Expression accessor, Type propertyType, out Expression? result,
			out string? errorMessage) {
			errorMessage = null;
			switch (action) {
				case FilterAction.Exists:
					result = Expression.AndAlso(
						accessor.BuildNotEqualsFilterExpression(null),
						accessor.BuildNotEqualsFilterExpression("", typeof(string)));
					return true;
				case FilterAction.IsNull:
					result = accessor.BuildEqualsFilterExpression(null);
					return true;
				case FilterAction.IsNullOrEmpty:
					result = Expression.OrElse(
						accessor.BuildEqualsFilterExpression(null),
						accessor.BuildEqualsFilterExpression("", typeof(string)));
					return true;
				case FilterAction.StartsWith:
					result = Stringify(accessor).BuildLikeFilterExpression(value ?? "", false, true, !extended);
					return true;
				case FilterAction.Contains:
					result = Stringify(accessor).BuildLikeFilterExpression(value ?? "", true, true, !extended);
					return true;
				case FilterAction.EndsWith:
					result = Stringify(accessor).BuildLikeFilterExpression(value ?? "", true, false, !extended);
					return true;
				case FilterAction.Equals:
					if (accessor.Type.IsEnum)
						result = value == null
							? Expression.Constant(false)
							: accessor.BuildEqualsFilterExpression(
								Enum.Parse(accessor.Type, value)); // TODO: validate value
					else
						result = Stringify(accessor).BuildEqualsFilterExpression(value);
					return true;
				case FilterAction.NotEquals:
					if (accessor.Type.IsEnum)
						result = value == null
							? Expression.Constant(false)
							: accessor.BuildNotEqualsFilterExpression(
								Enum.Parse(accessor.Type, value)); // TODO: validate value
					else
						result = Stringify(accessor).BuildNotEqualsFilterExpression(value);
					return true;
				case FilterAction.Greater: {
					if (!toComparable(accessor, value, out var comparable)) {
						_logger.LogWarning(
							"Unsupported action {Action} performed on property {PropertyName} of non-comparable type {PropertyType} [{Expression}]: Unable to convert filter value",
							action, name, propertyType.FullName, accessor.ToString());
						errorMessage = string.Format(InvalidActionMessage, action, name);
						result = null;
						return false;
					}

					result = accessor.BuildComparisonFilterExpression(comparable, true, false);
					return true;
				}
				case FilterAction.GreaterEqual: {
					if (!toComparable(accessor, value, out var comparable)) {
						_logger.LogWarning(
							"Unsupported action {Action} performed on property {PropertyName} of non-comparable type {PropertyType} [{Expression}]: Unable to convert filter value",
							action, name, propertyType.FullName, accessor.ToString());
						errorMessage = string.Format(InvalidActionMessage, action, name);
						result = null;
						return false;
					}

					result = accessor.BuildComparisonFilterExpression(comparable, true, true);
					return true;
				}
				case FilterAction.Less: {
					if (!toComparable(accessor, value, out var comparable)) {
						_logger.LogWarning(
							"Unsupported action {Action} performed on property {PropertyName} of non-comparable type {PropertyType} [{Expression}]: Unable to convert filter value",
							action, name, propertyType.FullName, accessor.ToString());
						errorMessage = string.Format(InvalidActionMessage, action, name);
						result = null;
						return false;
					}

					result = accessor.BuildComparisonFilterExpression(comparable, false, false);
					return true;
				}
				case FilterAction.LessEqual: {
					if (!toComparable(accessor, value, out var comparable)) {
						_logger.LogWarning(
							"Unsupported action {Action} performed on property {PropertyName} of non-comparable type {PropertyType} [{Expression}]: Unable to convert filter value",
							action, name, propertyType.FullName, accessor.ToString());
						errorMessage = string.Format(InvalidActionMessage, action, name);
						result = null;
						return false;
					}

					result = accessor.BuildComparisonFilterExpression(comparable, false, true);
					return true;
				}
				default:
					throw new ArgumentOutOfRangeException();
			}
		}

		public LambdaExpression ToEnumNameLambda(Type type) {
			return EnumCache.GetOrAdd(type, enumType => {
				var parameter = Expression.Parameter(enumType, "e");
				return Expression.Lambda(Expression.Switch(parameter,
					Expression.Constant(""), // TODO: Throw exception to make the request fail?
					Enum.GetValues(enumType)
						.Cast<object>()
						.Select(o => Expression.SwitchCase(
							Expression.Constant(Enum.GetName(enumType, o)),
							Expression.Constant(o))).ToArray()), parameter);
			});
		}

		public bool TryGetPropertyPath(string name, Type baseType, ParameterExpression parameter,
			[NotNullWhen(true)] out Expression? exp, [NotNullWhen(true)] out Type? propertyType) {
			if (name.Length > 0 && name[0] == '.')
				name = name[1..];
			return TryGetPropertyPath(null, name, baseType, parameter, out exp, out propertyType);
		}

		private bool TryGetPropertyPath(string? prefix, string name, Type baseType,
			Expression parameter, [NotNullWhen(true)] out Expression? exp, [NotNullWhen(true)] out Type? propertyType) {
			var prefixed = prefix == null ? name : $"{prefix}.{name}";
			var idx = name.IndexOf('.');
			if (idx < 0) {
				var result = Branch(parameter, baseType, true, name, null, name);
				if (result.HasValue)
					(exp, propertyType) = result.Value;
				else {
					exp = null;
					propertyType = null;
				}

				return result.HasValue;
			}

			var prev = -1;
			propertyType = baseType;
			var partial = new StringBuilder(prefixed.Length);
			if (prefix != null)
				partial.Append(prefix).Append('.');
			exp = parameter;
			do {
				var part = idx >= 0 ? name.Substring(prev + 1, idx) : name.Substring(prev + 1);
				partial.Append(part);

				var r = Branch(exp, propertyType, true, part, partial, prefixed);
				if (r == null) {
					exp = null;
					propertyType = null;
					return false;
				}

				(exp, propertyType) = r.Value;

				prev = idx;
				partial.Append('.');
			} while (idx >= 0 && (idx = name.IndexOf('.', idx + 1)) >= 0
			         || prev >= 0); // Executing an additional cycle after no more '.' are found

			return true;
		}

		private static readonly MethodInfo StrConcatMethod = new Func<string[], string>(string.Concat).Method;

		private (Expression, Type)? Branch(Expression source,
			Type type, bool allowNavigation, string name, StringBuilder? partialPath, string fullPath) {
			if (name[0] == '[' && name[^1] == ']') {
				var parts = name[1..^1].Split(',');
				var values = new Expression[parts.Length];
				for (var i = 0; i < parts.Length; i++) {
					var s = parts[i];
					if ((s[0] == '"' || s[0] == '\'') && (s[^1] == '"' || s[^1] == '\''))
						values[i] = Expression.Constant(s[1..^1], typeof(string));
					else {
						if (!TryGetPropertyPath(partialPath?.ToString(), s, type, source, out var exp, out var t))
							return null;
						values[i] = t == typeof(string) ? exp : Stringify(exp);
					}
				}

				var v = Expression.NewArrayInit(typeof(string), values);
				return (Expression.Call(null, StrConcatMethod, v), typeof(string));
			}

			var p = ExploreProperty(type, name, partialPath, fullPath);
			if (p == null || !ValidateNavigation(p, allowNavigation, partialPath, fullPath)) {
				return null;
			}

			var e = NavigateInto(source, p.PropertyInfo!);
			return (e, p.PropertyInfo!.PropertyType);
		}

		private IReadOnlyPropertyBase?
			ExploreProperty(Type type, string name, StringBuilder? partialPath, string fullPath) {
			if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ICollection<>)) {
				if (partialPath == null)
					_logger.LogInformation(
						"Collection properties navigation is not supported {PropertyName} ({FullPropertyPath})",
						name, fullPath);
				else
					_logger.LogInformation(
						"Collection properties navigation is not supported {PropertyName} ([{PartialPath}]{FullPropertyPath})",
						name, partialPath.ToString(), fullPath[partialPath.Length..]);
				return null;
			}

			var entity = _context.Model.FindEntityType(type);
			if (entity == null) {
				if (partialPath == null)
					_logger.LogInformation("Referenced entity is not mapped {EntityType} ({FullPropertyPath})",
						type.FullName, fullPath);
				else
					_logger.LogInformation(
						"Referenced entity is not mapped {EntityType} ([{PartialPath}]{FullPropertyPath})",
						type.FullName, partialPath.ToString(), fullPath[partialPath.Length..]);
				return null;
			}

			var prop = (IReadOnlyPropertyBase?) entity.FindNavigation(name) ?? entity.FindProperty(name);
			if (prop == null) {
				if (partialPath == null)
					_logger.LogInformation("Referenced unknown property {PropertyName} ({FullPropertyPath})",
						name, fullPath);
				else
					_logger.LogInformation(
						"Referenced unknown property {PropertyName} ([{PartialPath}]{FullPropertyPath})",
						name, partialPath.ToString(), fullPath[partialPath.Length..]);
				return null;
			}

			return prop;
		}

		private bool ValidateNavigation(
			IReadOnlyPropertyBase p, bool allowNavigation, StringBuilder? partialPath, string fullPath) {
			if (!allowNavigation && p is INavigation) {
				if (partialPath == null)
					_logger.LogInformation(
						"Navigation property {PropertyName} ({FullPropertyPath}) found as final expression component",
						p.Name, fullPath);
				else
					_logger.LogInformation(
						"Navigation property {PropertyName} found as final component of ([{PartialPath}]{FullPropertyPath})",
						p.Name, partialPath.ToString(), fullPath[partialPath.Length..]);
				return false;
			}

			var property = p.PropertyInfo;
			if (property == null) {
				if (partialPath == null)
					_logger.LogWarning(
						"Filterable members such as {PropertyName} ({FullPropertyPath}) must be defined as properties. Field types are not supported",
						p.Name, fullPath);
				else
					_logger.LogWarning(
						"Filterable members such as {PropertyName} ([{PartialPath}]{FullPropertyPath}) must be defined as properties. Field types are not supported",
						p.Name, partialPath.ToString(), fullPath[partialPath.Length..]);
				// TODO: [verify this type of errors beforehand to avoid deployment with this type of issues]
				return false;
			}

			var attr = property.GetCustomAttribute<FilterableAttribute>();
			if (attr == null) {
				if (partialPath == null)
					_logger.LogInformation(
						"Filter for property {PropertyName} ({FullPropertyPath}) was rejected as the property doesn't have a FilterableAttribute decorator",
						p.Name, fullPath);
				else
					_logger.LogInformation(
						"Filter for property {PropertyName} ([{PartialPath}]{FullPropertyPath}) was rejected as the property doesn't have a FilterableAttribute decorator",
						p.Name, partialPath.ToString(), fullPath[partialPath.Length..]);
				return false;
			}

			return true;
		}

		public MemberExpression NavigateInto(Expression source, PropertyInfo property) {
			return Expression.MakeMemberAccess(source, property);
		}

		public virtual Expression Stringify(Expression member) {
			if (member.Type == typeof(string))
				return member;
			if (member.Type.IsEnum) {
				// return Expression.Call(null, _filteringService.ToEnumNameLambda(member.Type).Method, member);
				// return Expression.Invoke(_filteringService.ToEnumNameLambda(member.Type), member);
				throw new NotImplementedException();
				/*return Expression.Switch(member,
					Expression.Constant(""), // TODO: Throw exception to make the request fail?
					Enum.GetValues(member.Type)
						.Cast<object>()
						.Select(o => Expression.SwitchCase(
							Expression.Constant(Enum.GetName(member.Type, o)),
							Expression.Constant(o))).ToArray());*/
				/*return Expression.Call(null,
					new Func<BindingFlags, string>(Enum.GetName)
						.Method.GetGenericMethodDefinition()
						.MakeGenericMethod(member.Type), member);*/
			}

			return Expression.Call(member, nameof(ToString), null);
		}

		public virtual string? StringifyValue(object? value) {
			return value switch {
				null => null,
				string s => s,
				Guid guid => guid.ToString("N"),
				_ => value.ToString()
			};
		}
	}
}