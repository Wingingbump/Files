// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Services.Search
{
	/// <summary>
	/// Thin client over the Everything SDK shim DLL. Talks via WM_COPYDATA to the user's
	/// running Everything service. Bundled DLL is just an IPC wrapper — it does not host an
	/// indexer, so no admin or service lifecycle concerns.
	/// </summary>
	public interface IEverythingClient
	{
		/// <summary>
		/// Whether Everything is installed, running, and its DB is loaded. Cheap, cached;
		/// transitions are detected lazily on the next <see cref="SearchAsync"/> call.
		/// </summary>
		bool IsAvailable { get; }

		/// <summary>
		/// Search Everything's index. Cancellation honored at boundaries; in-flight native
		/// queries are not aborted (they're fast — Everything is in-memory).
		/// </summary>
		/// <param name="query">Everything query string (supports its native query syntax).</param>
		/// <param name="scope">Optional path scope. When set, results are restricted to this subtree.</param>
		/// <param name="maxResults">0 = no limit.</param>
		Task<IReadOnlyList<EverythingResult>> SearchAsync(string query, string? scope, uint maxResults, CancellationToken cancellationToken);

		/// <summary>
		/// Calculate total size of a folder using Everything's size index. Returns 0 if the
		/// folder isn't indexed by Everything (e.g. excluded path).
		/// </summary>
		Task<ulong> GetFolderSizeAsync(string folderPath, CancellationToken cancellationToken);
	}

	/// <summary>
	/// A single result from Everything. Times are FILETIME ticks; convert with <see cref="DateTime.FromFileTime"/>.
	/// </summary>
	public sealed record EverythingResult(
		string FullPath,
		string FileName,
		bool IsFolder,
		long? Size,
		long? DateModifiedFileTime,
		long? DateCreatedFileTime,
		uint Attributes);
}
