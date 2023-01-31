using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Fastersetup.Framework.Api.Controllers.Injection;

internal class LoggerControllerInjector : IControllerPropertyActivator {
	public void Activate(ControllerContext context, object controller) {
		if (controller is IControllerDependsOnLogger casted)
			casted.Logger = (ILogger) context.HttpContext.RequestServices
				.GetRequiredService(typeof(ILogger<>)
					.MakeGenericType(context.ActionDescriptor.ControllerTypeInfo));
	}

	public Action<ControllerContext, object> GetActivatorDelegate(ControllerActionDescriptor actionDescriptor) {
		var type = typeof(ILogger<>).MakeGenericType(actionDescriptor.ControllerTypeInfo);
		return (context, controller) => {
			if (controller is IControllerDependsOnLogger casted)
				casted.Logger = (ILogger) context.HttpContext.RequestServices.GetRequiredService(type);
		};
	}
}

public interface IControllerDependsOnLogger {
	ILogger Logger { set; }
}