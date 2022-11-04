using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;
using System.Reflection;
using Fastersetup.Framework.Api.Attributes.Data;
using Fastersetup.Framework.Api.Attributes.Security;
using Fastersetup.Framework.Api.Services.Utilities;
using Microsoft.EntityFrameworkCore;

namespace Fastersetup.Framework.Api.Services.Default;

public class DefaultObjectManipulatorRepository : IObjectManipulatorRepository {
	private readonly ConcurrentDictionary<Type, ConcurrentDictionary<Type, IObjectManipulator?>> _cache = new();

	public IObjectManipulator<T> Get<T>(DbContext context) where T : class {
		return (IObjectManipulator<T>)
			(_cache.GetOrAdd(context.GetType(), _ => new ConcurrentDictionary<Type, IObjectManipulator?>())
				.GetOrAdd(typeof(T), static (_, context) => Construct<T>(context), context) ?? Construct<T>(context));
	}

	public IEqualityComparer<T> GetComparer<T>(DbContext context) where T : class {
		return (IEqualityComparer<T>) Get<T>(context);
	}

	public bool Prune<T>(DbContext context) where T : class {
		return Prune(context.GetType(), typeof(T));
	}

	public bool Prune<TDbContext, T>() where TDbContext : DbContext where T : class {
		return Prune(typeof(TDbContext), typeof(T));
	}

	private bool Prune(Type contextType, Type type) {
		if (_cache.TryGetValue(contextType, out var contextCache))
			if (contextCache.TryRemove(type, out _))
				return true;
		return false;
	}

	public void DisableCachingFor<T>(DbContext context) where T : class {
		DisableCachingFor(context.GetType(), typeof(T));
	}

	public void DisableCachingFor<TDbContext, T>() where TDbContext : DbContext where T : class {
		DisableCachingFor(typeof(TDbContext), typeof(T));
	}

	private void DisableCachingFor(Type contextType, Type type) {
		_cache.GetOrAdd(contextType, _ => new ConcurrentDictionary<Type, IObjectManipulator?>())
			.AddOrUpdate(type, (IObjectManipulator?) null, (_, _) => null);
	}


	private static bool MustSkip(MemberInfo member) {
		return member.GetCustomAttribute<ProtectedAttribute>() != null
		       || member.GetCustomAttribute<RowTimestampAttribute>() != null;
	}

	private static bool IsWritable(MemberInfo member) {
		return (member.MemberType == MemberTypes.Property && ((PropertyInfo) member).CanWrite)
		       || (member.MemberType == MemberTypes.Field && !((FieldInfo) member).IsInitOnly);
	}

	private static ClassOperator<T> Construct<T>(DbContext context) where T : class {
		var nullHashCheck = (Expression<Func<object?, int>>) (o => o == null ? 0 : o.GetHashCode());
		var nil = Expression.Constant(null);
		var zero = Expression.Constant(0);
		var mul = Expression.Constant(397);
//				var hashCode = typeof(object).GetMethod(nameof(object.GetHashCode)).MustBeNotNull();
		var source = Expression.Parameter(typeof(T), "source");
		var target = Expression.Parameter(typeof(T), "target");
		var contextParameter = Expression.Parameter(typeof(DbContext), "context");
		var srcNull = Expression.Equal(source, nil);
		var targetNull = Expression.Equal(target, nil);
		var equality = (Expression) Expression.OrElse(
			Expression.AndAlso(srcNull, targetNull),
			Expression.AndAlso(Expression.Not(srcNull), Expression.Not(targetNull)));
		var hashing = (Expression?) null;
		var copy = new List<Expression>();
		var model = context.Model.FindEntityType(typeof(T));
		var efUtilsModelResolve = ((Func<DbContext, T, T?>) EFUtils.Resolve).Method;
		foreach (var member in typeof(T).GetMembers(BindingFlags.Public | BindingFlags.Instance)) {
			if (member.MemberType != MemberTypes.Property && member.MemberType != MemberTypes.Field)
				// Methods support?
				continue;
			if (MustSkip(member))
				continue;
			var property = model?.FindProperty(member);
			if (property != null) {
				if (property.IsPrimaryKey())
					// Not copying pk nor considering it for equality (avoid equality issues with a non-tracked entity and a tracked entity)
					continue;
				if (property.IsForeignKey())
					// Foreign key gets considered by the navigation property and must be skipped to avoid duplicate operations
					continue;
			}

			MemberExpression t;
			MemberExpression src;
			var resolver = member.GetCustomAttribute<ResolveReferenceAttribute>();
			var navigation = model?.FindNavigation(member);
			if (navigation != null) {
				if (resolver == null)
					// Exclude navigation properties
					continue;
				var directSource = true;
				MemberExpression? targetMember = null;
				MemberExpression? sourceMember = null;
				if (IsWritable(member)) {
					directSource = false; // Source comparison values from the referenced object
					targetMember = Expression.MakeMemberAccess(target, member);
					sourceMember = Expression.MakeMemberAccess(source, member);
					MethodInfo efUtilsResolve;
					if (typeof(T) == navigation.ClrType)
						efUtilsResolve = efUtilsModelResolve;
					else
						efUtilsResolve = efUtilsModelResolve
							.GetGenericMethodDefinition()
							.MakeGenericMethod(navigation.ClrType);
					copy.Add(Expression.Assign(targetMember,
						Expression.Call(null, efUtilsResolve, contextParameter, sourceMember)));
				}

				var inverse = navigation.Inverse?.ForeignKey.Properties;
				for (var i = 0; i < navigation.ForeignKey.Properties.Count; i++) {
					var p = navigation.ForeignKey.Properties[i];
					var m = p.GetMember();
					if (p.IsPrimaryKey() || m == null || MustSkip(m))
						continue;
					var inverseMember = directSource || inverse == null ? null : inverse[i].GetMember();
					if (inverseMember == null) {
						t = Expression.MakeMemberAccess(target, m);
						src = Expression.MakeMemberAccess(source, m);
					} else { // Sourcing fk value directly from referenced object
						t = Expression.MakeMemberAccess(targetMember, inverseMember);
						src = Expression.MakeMemberAccess(sourceMember, inverseMember);
					}

					if (hashing == null)
						hashing = Expression.Invoke(nullHashCheck, Expression.Convert(src, typeof(object)));
					else
						hashing = Expression.ExclusiveOr(Expression.Multiply(hashing, mul),
							Expression.Invoke(nullHashCheck, Expression.Convert(src, typeof(object))));
					equality = Expression.AndAlso(equality, Expression.Equal(t, src));
					if (IsWritable(m))
						// Copying referenced value to local foreign key properties. Could be redundant
						copy.Add(Expression.Assign(t, src));
				}
			} else {
				t = Expression.MakeMemberAccess(target, member);
				src = Expression.MakeMemberAccess(source, member);

				// Exclude protected or readonly properties (TODO: "final" properties attribute)
				if (src.Member.GetCustomAttribute<ProtectedAttribute>() != null
				    || src.Member.GetCustomAttribute<KeyAttribute>() != null
				    || src.Member.GetCustomAttribute<RowTimestampAttribute>() != null)
					continue;
				if (hashing == null)
					hashing = Expression.Invoke(nullHashCheck, Expression.Convert(src, typeof(object)));
				else
					hashing = Expression.ExclusiveOr(Expression.Multiply(hashing, mul),
						Expression.Invoke(nullHashCheck, Expression.Convert(src, typeof(object))));
				equality = Expression.AndAlso(equality, Expression.Equal(t, src));
				if (IsWritable(member))
					copy.Add(Expression.Assign(t, src));
			}
		}

		return new ClassOperator<T>(
			Expression.Lambda<Func<T?, T?, bool>>(equality, source, target).Compile(),
			Expression.Lambda<Func<T, int>>(hashing ?? zero, source).Compile(),
			Expression
				.Lambda<Action<DbContext, T, T>>(Expression.Block(copy), contextParameter, source, target)
				.Compile());
	}

	private class ClassOperator<T> : IObjectManipulator<T>, IEqualityComparer<T> where T : class {
		private readonly Action<DbContext, T, T> _copier;
		private readonly Func<T?, T?, bool> _equalityComparer;
		private readonly Func<T, int> _hashCodeGenerator;

		public ClassOperator(
			Func<T?, T?, bool> equalityComparer,
			Func<T, int> hashCodeGenerator,
			Action<DbContext, T, T> copier) {
			_equalityComparer = equalityComparer;
			_hashCodeGenerator = hashCodeGenerator;
			_copier = copier;
		}

		public void CopyTo(DbContext context, T origin, T target) {
			_copier(context, origin, target);
		}

		public bool AreEqual(T origin, T target) {
			return _equalityComparer(origin, target);
		}

		void IObjectManipulator.CopyTo(DbContext context, object origin, object target) {
			CopyTo(context, (T) origin, (T) target);
		}

		bool IObjectManipulator.AreEqual(object origin, object target) {
			return AreEqual((T) origin, (T) target);
		}

		public bool Equals(T? x, T? y) {
			return _equalityComparer(x, y);
		}

		public int GetHashCode(T obj) {
			return _hashCodeGenerator(obj);
		}
	}
}