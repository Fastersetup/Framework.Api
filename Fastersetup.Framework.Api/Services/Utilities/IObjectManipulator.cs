using Microsoft.EntityFrameworkCore;

namespace Fastersetup.Framework.Api.Services.Utilities;

public interface IObjectManipulator {
	void CopyTo(DbContext context, object origin, object target);
	bool AreEqual(object origin, object target);
}

public interface IObjectManipulator<in T> : IObjectManipulator {
	void CopyTo(DbContext context, T origin, T target);
	bool AreEqual(T origin, T target);
}