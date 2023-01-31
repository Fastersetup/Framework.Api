using System.Diagnostics;
using System.Reflection;

namespace Fastersetup.Framework.Api;

internal static class InternalUtils {
	private const BindingFlags DeclaredOnlyLookup = BindingFlags.Public
	                                                | BindingFlags.NonPublic
	                                                | BindingFlags.Instance
	                                                | BindingFlags.Static
	                                                | BindingFlags.DeclaredOnly;
	private static readonly MethodInfo CallPropertyGetterOpenGenericMethod =
		typeof(InternalUtils).GetMethod(nameof(CallPropertyGetter), DeclaredOnlyLookup)!;
	private static readonly MethodInfo CallPropertySetterOpenGenericMethod =
		typeof(InternalUtils).GetMethod(nameof(CallPropertySetter), DeclaredOnlyLookup)!;

	// Called via reflection
	private static object? CallPropertyGetter<TDeclaringType, TValue>(
		Func<TDeclaringType, TValue> getter, object target) {
		return getter((TDeclaringType) target);
	}

	// Called via reflection
	private static void CallPropertySetter<TDeclaringType, TValue>(
		Action<TDeclaringType, TValue> setter, object target, object value) {
		setter((TDeclaringType) target, (TValue) value);
	}

	// Copied from Microsoft.Extensions.Internal.PropertyHelper
	/// <summary>
	/// Creates a single fast property setter for reference types. The result is not cached.
	/// </summary>
	/// <param name="propertyInfo">propertyInfo to extract the setter for.</param>
	/// <returns>a fast getter.</returns>
	/// <remarks>
	/// This method is more memory efficient than a dynamically compiled lambda, and about the
	/// same speed. This only works for reference types.
	/// </remarks>
	public static Action<object, object?> MakeFastPropertySetter(PropertyInfo propertyInfo) {
		Debug.Assert(propertyInfo != null);
		Debug.Assert(!propertyInfo.DeclaringType!.IsValueType);

		var setMethod = propertyInfo.SetMethod;
		Debug.Assert(setMethod != null);
		Debug.Assert(!setMethod.IsStatic);
		Debug.Assert(setMethod.ReturnType == typeof(void));
		var parameters = setMethod.GetParameters();
		Debug.Assert(parameters.Length == 1);

		// Instance methods in the CLR can be turned into static methods where the first parameter
		// is open over "target". This parameter is always passed by reference, so we have a code
		// path for value types and a code path for reference types.
		var typeInput = setMethod.DeclaringType!;
		var parameterType = parameters[0].ParameterType;

		// Create a delegate TDeclaringType -> { TDeclaringType.Property = TValue; }
		var propertySetterAsAction =
			setMethod.CreateDelegate(typeof(Action<,>).MakeGenericType(typeInput, parameterType));
		var callPropertySetterClosedGenericMethod =
			CallPropertySetterOpenGenericMethod.MakeGenericMethod(typeInput, parameterType);
		var callPropertySetterDelegate =
			callPropertySetterClosedGenericMethod.CreateDelegate(
				typeof(Action<object, object?>), propertySetterAsAction);

		return (Action<object, object?>) callPropertySetterDelegate;
	}
}