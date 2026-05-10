// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Utils.Storage.Search
{
	/// <summary>
	/// Wraps the existing <see cref="FolderSearch"/> implementation. Always available — Windows
	/// Search is the safety net every other engine falls back to.
	/// </summary>
	public sealed class WindowsSearchEngineService : ISearchEngineService
	{
		public PreferredSearchEngine Engine => PreferredSearchEngine.Windows;

		public bool IsAvailable => true;

		public Task SearchAsync(FolderSearch query, IList<ListedItem> results, CancellationToken cancellationToken)
		{
			// FolderSearch already implements the streaming + SearchTick pattern.
			return query.SearchAsync(results, cancellationToken);
		}

		public async Task<IReadOnlyList<ListedItem>> SuggestAsync(FolderSearch query, CancellationToken cancellationToken)
		{
			var results = await query.SearchAsync();
			return results;
		}
	}
}
