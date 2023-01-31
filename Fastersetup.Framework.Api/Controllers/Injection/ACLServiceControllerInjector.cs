using Fastersetup.Framework.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Internal;
using Microsoft.Extensions.DependencyInjection;

namespace Fastersetup.Framework.Api.Controllers.Injection;

internal class ACLServiceControllerInjector : IControllerPropertyActivator {
	public void Activate(ControllerContext context, object controller) {
		var iFace = controller.GetType().GetInterface(nameof(IControllerHasACLSupport) + "For`1");
		var arg = iFace?.GetGenericArguments().FirstOrDefault(); // Should always have one if not null
		if (iFace == null || arg == null)
			return;
		var serviceType = typeof(IAccessControlService<>).MakeGenericType(arg);
		var property = iFace.GetProperty(nameof(IControllerHasACLSupportFor<ACLServiceControllerInjector>.Acl));
		if (property == null)
			return; // Shouldn't happen
		var service = context.HttpContext.RequestServices.GetRequiredService(serviceType);
		property.SetValue(controller, service);
	}

	public Action<ControllerContext, object> GetActivatorDelegate(ControllerActionDescriptor actionDescriptor) {
		var iFace = actionDescriptor.ControllerTypeInfo.GetInterface(nameof(IControllerHasACLSupport) + "For`1");
		var arg = iFace?.GetGenericArguments().FirstOrDefault(); // Should always have one if not null
		if (iFace == null || arg == null)
			return (_, _) => { };
		var serviceType = typeof(IAccessControlService<>).MakeGenericType(arg);
		var property = iFace.GetProperty(nameof(IControllerHasACLSupportFor<ACLServiceControllerInjector>.Acl));
		if (property == null)
			return (_, _) => { }; // Shouldn't happen
		var setter = InternalUtils.MakeFastPropertySetter(property);
		return (context, controller) => {
			var service = context.HttpContext.RequestServices.GetRequiredService(serviceType);
			setter(controller, service);
		};
	}
}

public interface IControllerHasACLSupport {
}

public interface IControllerHasACLSupportFor<out TModel> : IControllerHasACLSupport where TModel : class {
	IAccessControlService<TModel> Acl { set; }
}