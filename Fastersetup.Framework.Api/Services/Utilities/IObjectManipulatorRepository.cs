using Microsoft.EntityFrameworkCore;

namespace Fastersetup.Framework.Api.Services.Utilities;

public interface IObjectManipulatorRepository {
	IObjectManipulator<T> Get<T>(DbContext context) where T : class;
	IEqualityComparer<T> GetComparer<T>(DbContext context) where T : class;
	bool Prune<T>(DbContext context) where T : class;
	bool Prune<TDbContext, T>() where TDbContext : DbContext where T : class;
	void DisableCachingFor<T>(DbContext context) where T : class;
	void DisableCachingFor<TDbContext, T>() where TDbContext : DbContext where T : class;
}