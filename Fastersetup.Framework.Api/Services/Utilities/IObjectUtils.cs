/*
 * Copyright 2022 Francesco Cattoni
 * 
 * This program is free software; you can redistribute it and/or
 * modify it under the terms of the GNU General Public License
 * version 3 as published by the Free Software Foundation.
 * 
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU General Public License for more details.
 */

namespace Fastersetup.Framework.Api.Services.Utilities {
	public interface IObjectUtils {
		T Clone<T>(T origin) where T : class, new();
		T Copy<T>(T origin, T target) where T : class;
		bool AreEqual<T>(T origin, T target) where T : class;
		int GetHashCode<T>(T o) where T : class;
		IEqualityComparer<T> GetEqualityComparer<T>() where T : class;

		void MergeCollection<T, TKey>(ICollection<T> currentItems, IEnumerable<T>? newItems, Func<T, TKey> keyFunc,
			Action<T, T>? merge = null, bool allowAdd = true, bool allowRemove = true)
			where TKey : notnull
			where T : class, new();

		ValueTask MergeCollectionAsync<T, TKey>(ICollection<T> currentItems, IEnumerable<T>? newItems,
			Func<T, TKey> keyFunc,
			Func<T, T, CancellationToken, ValueTask>? merge = null, bool allowAdd = true, bool allowRemove = true,
			CancellationToken token = default)
			where TKey : notnull
			where T : class, new();

		ValueTask MergeReferenceCollectionAsync<T, TKey>(ICollection<T> currentItems, IEnumerable<T>? newItems,
			Func<T, TKey> keyFunc, bool allowAdd = true, bool allowRemove = true, CancellationToken token = default)
			where TKey : notnull
			where T : class;
	}
}