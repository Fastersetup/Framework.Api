using Fastersetup.Framework.Api.Services;

namespace Fastersetup.Framework.Api.Controllers.Injection;

internal class FilteringServiceControllerInjector
	: ServiceControllerInjectorBase<IControllerDependsOnFilteringService, FilteringService> {
	protected override void Set(IControllerDependsOnFilteringService controller, FilteringService value) {
		controller.FilteringService = value;
	}
}

public interface IControllerDependsOnFilteringService {
	FilteringService FilteringService { set; }
}