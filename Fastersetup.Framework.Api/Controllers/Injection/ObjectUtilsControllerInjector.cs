using Fastersetup.Framework.Api.Services.Utilities;

namespace Fastersetup.Framework.Api.Controllers.Injection;

internal class ObjectUtilsControllerInjector
	: ServiceControllerInjectorBase<IControllerDependsOnIObjectUtils, IObjectUtils> {
	protected override void Set(IControllerDependsOnIObjectUtils controller, IObjectUtils value) {
		controller.Utils = value;
	}
}

public interface IControllerDependsOnIObjectUtils {
	IObjectUtils Utils { set; }
}