using Microsoft.EntityFrameworkCore;

namespace Fastersetup.Framework.Api.Controllers.Injection;

internal class DbContextControllerInjector : ServiceControllerInjectorBase<IControllerDependsOnDbContext, DbContext> {
	protected override void Set(IControllerDependsOnDbContext controller, DbContext value) {
		controller.DbContext = value;
	}
}

public interface IControllerDependsOnDbContext {
	DbContext DbContext { get; set; }
}