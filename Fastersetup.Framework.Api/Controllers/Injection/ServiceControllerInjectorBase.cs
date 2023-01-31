using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Internal;
using Microsoft.Extensions.DependencyInjection;

namespace Fastersetup.Framework.Api.Controllers.Injection;

internal abstract class ServiceControllerInjectorBase<TInterface, TService> : IControllerPropertyActivator
	where TService : notnull {
	protected abstract void Set(TInterface controller, TService value);

	public void Activate(ControllerContext context, object controller) {
		if (controller is TInterface casted)
			Set(casted, context.HttpContext.RequestServices.GetRequiredService<TService>());
	}

	public Action<ControllerContext, object> GetActivatorDelegate(ControllerActionDescriptor actionDescriptor) {
		return Activate;
	}
}