// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Utils.Storage.Search
{
	/// <summary>
	/// A search engine implementation. Multiple impls coexist; the active one is chosen by
	/// <see cref="ISearchEngineSelector"/> based on user settings and runtime availability.
	/// </summary>
	public interface ISearchEngineService
	{
		/// <summary>
		/// Stable identifier — matches <see cref="Files.App.Data.Enums.PreferredSearchEngine"/>.
		/// </summary>
		PreferredSearchEngine Engine { get; }

		/// <summary>
		/// Whether the engine can currently service a query. Implementations should make this cheap
		/// (no blocking IPC on the hot path); cache and refresh in the background if needed.
		/// </summary>
		bool IsAvailable { get; }

		/// <summary>
		/// Streaming search. Pushes <see cref="ListedItem"/>s into <paramref name="results"/> as
		/// they're discovered and raises <see cref="FolderSearch.SearchTick"/> periodically so the
		/// UI can re-render. Returns when the search is complete or cancelled.
		/// </summary>
		Task SearchAsync(FolderSearch query, IList<ListedItem> results, CancellationToken cancellationToken);

		/// <summary>
		/// One-shot bounded search used for omnibar suggestions. Honors <see cref="FolderSearch.MaxItemCount"/>.
		/// </summary>
		Task<IReadOnlyList<ListedItem>> SuggestAsync(FolderSearch query, CancellationToken cancellationToken);
	}
}
