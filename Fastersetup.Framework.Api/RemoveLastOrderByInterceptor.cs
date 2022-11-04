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

using System.Data.Common;
using System.Text;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Fastersetup.Framework.Api {
	/// <summary>
	/// Workaround to avoid out of memory on queries with bigger field lists<br/>
	/// Inspired by <a href="https://github.com/dotnet/efcore/issues/19828#issuecomment-847222980">chazt3n workaround</a><br/>
	/// <a href="https://github.com/dotnet/efcore/issues/19828#issuecomment-1224927906">Source</a>
	/// </summary>
	/// <remarks>
	/// The interceptor works on the assumption of first field of the main SELECT statement being also the first field
	/// in the ORDER BY statement after the explicitly requested OrderBy expressions
	/// </remarks>
	public class RemoveLastOrderByInterceptor : DbCommandInterceptor {
		public const string QueryTag = "RemoveLastOrderBy";

		public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
			DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
			CancellationToken token = new()) {
			try {
				TryApplyPatch(command);
			} catch (Exception ex) {
				// _logger.LogError(ex, "Failed to intercept query.");
				Console.WriteLine(ex);
				throw; // Fails forcefully to avoid unexpected silent behaviours
			}

			return base.ReaderExecutingAsync(command, eventData, result, token);
		}

		private static bool TryApplyPatch(DbCommand command) {
			const string orderBy = "ORDER BY";
			const string select = "SELECT ";
			var query = command.CommandText;
			int idx, endIdx = 0;
			if (!query.StartsWith("-- "))
				// Check if the command is tagged
				return false;
			var separatorIdx = query.IndexOf("\n\n", StringComparison.Ordinal);
			if (separatorIdx < 0
			    || query.IndexOf(QueryTag, 0, separatorIdx + 1, StringComparison.Ordinal) < 0)
				// Efficiently checks if the tags block contains the required QueryTag
				return false;
			if ((idx = query.LastIndexOf(orderBy, StringComparison.Ordinal)) < 0)
				// The query doesn't have an ORDER BY statement
				return false;
			/*if (!query.EndsWith(';'))
				// Query is already clean ???
				return false;*/
			// Using StringBuilder to avoid string allocation issues
			// While using early versions of .NET Framework there would be buffer allocation exceptions so it's
			// necessary to remove part of the pre-allocated string before appending (or specify capacity explicitly)
			var cmd = new StringBuilder(query);
			// Identify first SELECT field
			var start = query.IndexOf(select, StringComparison.Ordinal);
			if (start >= 0) {
				var nextIdx = query.IndexOf(",", start, StringComparison.Ordinal);
				var fromIdx = query.IndexOf("FROM", start, StringComparison.Ordinal);
				// Support both selection with only one value and multi value selection
				var end = nextIdx < 0 ? fromIdx : Math.Min(nextIdx, fromIdx);
				var from = start + select.Length;
				// Assemble first selected field query
				var firstField = cmd.ToString(from, end - from);
				// Check if the ORDER BY starts with a different field than the first selected field and, in such
				// case, identifies the index where the "redundant" ORDER BY begins
				var orderStart = query.IndexOf(firstField, idx, StringComparison.Ordinal);
				if (orderStart > idx + orderBy.Length + 1) {
					var orderEnd = orderStart;
					do { // Identify end of ORDER BY block to avoid cutting out stuff like OFFSET and LIMIT
						orderEnd = query.IndexOf(' ', orderEnd + 1);
					} while (orderEnd >= 0 && query[orderEnd - 1] == ',');

					if (orderEnd > 0 && orderEnd > orderStart)
						endIdx = orderEnd;
// Console.WriteLine(query[orderStart..]);
					// idx = orderStart - 2 /* Subtract 2 characters to take account of the trailing comma */;
					idx = orderStart + firstField.Length /* - 2*/
						/* Cut out only next order bys */
						/* Subtract 2 characters to take account of the trailing comma */;
				}
			}

			// Cut ORDER BY statement or remove it entirely
			command.CommandText = cmd
				.Remove(idx, endIdx > 0 ? endIdx - idx + 1 : query.Length - idx)
				.Append(';')
				.ToString();
			// Console.WriteLine(command.CommandText[idx..]);
			return true;
		}
	}
}