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

using Fastersetup.Framework.Api.Services.Utilities;
using Microsoft.EntityFrameworkCore;

namespace Fastersetup.Framework.Api.Services.Default {
	internal class ObjectUtils : IObjectUtils {
		private readonly DbContext _context;
		private readonly IObjectManipulatorRepository _repository;

		public ObjectUtils(DbContext context, IObjectManipulatorRepository repository) {
			_context = context;
			_repository = repository;
		}

		public T Clone<T>(T origin) where T : class, new() {
			var t = new T();
			_repository.Get<T>(_context).CopyTo(_context, origin, t);
			return t;
		}

		public T Copy<T>(T origin, T target) where T : class {
			_repository.Get<T>(_context).CopyTo(_context, origin, target);
			return target;
		}

		public bool AreEqual<T>(T origin, T target) where T : class {
			return _repository.Get<T>(_context).AreEqual(origin, target);
		}

		public int GetHashCode<T>(T o) where T : class {
			return _repository.GetComparer<T>(_context).GetHashCode(o);
		}

		public IEqualityComparer<T> GetEqualityComparer<T>() where T : class {
			return _repository.GetComparer<T>(_context);
		}

		public void MergeCollection<T, TKey>(
			ICollection<T> currentItems, IEnumerable<T>? newItems, Func<T, TKey> keyFunc, Action<T, T>? merge = null,
			bool deleteRemovedObjects = true, bool allowAdd = true, bool allowRemove = true)
			where TKey : notnull where T : class, new() {
			MergeCollection(currentItems, newItems, keyFunc, item => {
				var i = Copy(item, new T());
				merge?.Invoke(item, i);
				return i;
			}, (found, existing) => {
				// context.Entry(item).CurrentValues.SetValues(found);
				if (merge != null)
					merge(found, Copy(found, existing));
				else
					Copy(found, existing);
			}, o => o, deleteRemovedObjects, allowAdd, allowRemove);
		}

		public ValueTask MergeCollectionAsync<T, TKey>(
			ICollection<T> currentItems, IEnumerable<T>? newItems, Func<T, TKey> keyFunc,
			Func<T, T, CancellationToken, ValueTask>? merge = null, bool deleteRemovedObjects = true,
			bool allowAdd = true, bool allowRemove = true, CancellationToken cancellationToken = default)
			where TKey : notnull where T : class, new() {
			return MergeCollectionAsync(currentItems, newItems, keyFunc, async (item, token) => {
				var i = Copy(item, new T());
				if (merge != null)
					await merge(item, i, token);
				return i;
			}, async (found, existing, token) => {
				// context.Entry(item).CurrentValues.SetValues(found);
				if (merge != null)
					await merge(found, Copy(found, existing), token);
				else
					Copy(found, existing);
			}, (o, _) => ValueTask.FromResult<T?>(o), deleteRemovedObjects, allowAdd, allowRemove, cancellationToken);
		}

		public ValueTask MergeReferenceCollectionAsync<T, TKey>(
			ICollection<T> currentItems, IEnumerable<T>? newItems, Func<T, TKey> keyFunc,
			bool allowAdd = true, bool allowRemove = true, CancellationToken cancellationToken = default)
			where TKey : notnull where T : class {
			return MergeCollectionAsync(currentItems, newItems, keyFunc,
				(item, token) => _context.ResolveAsync(item, token),
				(_, _, _) => ValueTask.CompletedTask,
				(item, token) => _context.ResolveAsync(item, token),
				false, allowAdd, allowRemove, cancellationToken);
		}

		private void MergeCollection<T, TKey>(
			ICollection<T> currentItems, IEnumerable<T>? newItems, Func<T, TKey> keyFunc,
			Func<T, T?> toAdd,
			Action<T, T> foundExisting,
			Func<T, T?> resolve,
			bool deleteRemovedObjects,
			bool allowAdd = true, bool allowRemove = true) where TKey : notnull where T : class {
			if (newItems == null)
				return;
			var data = new Dictionary<TKey, T>();
			List<T>? added = null;
			foreach (var item in newItems) {
				var key = keyFunc(item);
				if (EqualityComparer<TKey>.Default.Equals(key, default)) {
					var i = toAdd(item);
					if (i != null)
						(added ??= new List<T>()).Add(i);
				} else
					data.Add(key, item);
			}

			List<T>? removed = null;
			foreach (var item in currentItems) {
				var currentKey = keyFunc(item);
				if (!data.TryGetValue(currentKey, out var found))
					(removed ??= new List<T>()).Add(item);
				else if (!ReferenceEquals(found, item))
					foundExisting(found, item);
				data.Remove(currentKey);
			}

			if (allowRemove && removed != null)
				foreach (var item in removed) {
					currentItems.Remove(item);
					if (deleteRemovedObjects)
						_context.Remove(item);
				}

			if (allowAdd && added != null)
				foreach (var item in added)
					currentItems.Add(item);

			if (allowAdd)
				foreach (var newItem in data.Values) {
					var resolved = resolve(newItem);
					if (resolved != null)
						currentItems.Add(resolved);
				}
		}

		private async ValueTask MergeCollectionAsync<T, TKey>(
			ICollection<T> currentItems, IEnumerable<T>? newItems, Func<T, TKey> keyFunc,
			Func<T, CancellationToken, ValueTask<T?>> toAdd,
			Func<T, T, CancellationToken, ValueTask> foundExisting,
			Func<T, CancellationToken, ValueTask<T?>> resolve,
			bool deleteRemovedObjects,
			bool allowAdd = true, bool allowRemove = true, CancellationToken token = default)
			where TKey : notnull where T : class {
			if (newItems == null)
				return;
			var data = new Dictionary<TKey, T>();
			List<T>? added = null;
			foreach (var item in newItems) {
				var key = keyFunc(item);
				if (EqualityComparer<TKey>.Default.Equals(key, default)) {
					var i = await toAdd(item, token);
					if (i != null)
						(added ??= new List<T>()).Add(i);
				} else
					data.Add(key, item);
			}

			List<T>? removed = null;
			foreach (var item in currentItems) {
				var currentKey = keyFunc(item);
				if (!data.TryGetValue(currentKey, out var found))
					(removed ??= new List<T>()).Add(item);
				else if (!ReferenceEquals(found, item))
					await foundExisting(found, item, token);
				data.Remove(currentKey);
			}

			if (allowRemove && removed != null)
				foreach (var item in removed) {
					currentItems.Remove(item);
					if (deleteRemovedObjects)
						_context.Remove(item);
				}

			if (allowAdd && added != null)
				foreach (var item in added)
					currentItems.Add(item);

			if (allowAdd)
				foreach (var newItem in data.Values) {
					var resolved = await resolve(newItem, token);
					if (resolved != null)
						currentItems.Add(resolved);
				}
		}
	}
}