// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Services.Search;
using Microsoft.Extensions.Logging;
using System.IO;
using Windows.Storage;

namespace Files.App.Utils.Storage.Search
{
	/// <summary>
	/// Search engine backed by Everything's index via <see cref="IEverythingClient"/>. Falls
	/// back to <see cref="WindowsSearchEngineService"/> when Everything is unavailable.
	/// </summary>
	public sealed class EverythingSearchEngineService : ISearchEngineService
	{
		// Roughly the cadence FolderSearch uses to drive UI ticks.
		private const int TickEveryN = 64;
		private const int TickFirstAt = 32;

		// FILE_ATTRIBUTE_HIDDEN
		private const uint AttrHidden = 0x2;

		private readonly IEverythingClient _client;
		private readonly WindowsSearchEngineService _fallback;
		private readonly ILogger<EverythingSearchEngineService> _logger;

		public EverythingSearchEngineService(
			IEverythingClient client,
			WindowsSearchEngineService fallback,
			ILogger<EverythingSearchEngineService> logger)
		{
			_client = client;
			_fallback = fallback;
			_logger = logger;
		}

		public PreferredSearchEngine Engine => PreferredSearchEngine.Everything;

		public bool IsAvailable => _client.IsAvailable;

		public async Task SearchAsync(FolderSearch query, IList<ListedItem> results, CancellationToken cancellationToken)
		{
			if (!_client.IsAvailable)
			{
				_logger.LogDebug("Everything unavailable; delegating to Windows Search");
				await _fallback.SearchAsync(query, results, cancellationToken);
				return;
			}

			var raw = await _client.SearchAsync(
				query.Query ?? string.Empty,
				ScopeFor(query.Folder),
				query.MaxItemCount,
				cancellationToken);

			for (int i = 0; i < raw.Count; i++)
			{
				cancellationToken.ThrowIfCancellationRequested();

				var item = ToListedItem(raw[i], loadIcon: query.MaxItemCount > 0);
				if (item is null)
					continue;

				results.Add(item);

				if (results.Count == TickFirstAt || results.Count % 300 == 0)
					query.SearchTick?.Invoke(query, EventArgs.Empty);
			}
		}

		public async Task<IReadOnlyList<ListedItem>> SuggestAsync(FolderSearch query, CancellationToken cancellationToken)
		{
			if (!_client.IsAvailable)
				return await _fallback.SuggestAsync(query, cancellationToken);

			var raw = await _client.SearchAsync(
				query.Query ?? string.Empty,
				ScopeFor(query.Folder),
				query.MaxItemCount == 0 ? 10 : query.MaxItemCount,
				cancellationToken);

			var list = new List<ListedItem>(raw.Count);
			foreach (var r in raw)
			{
				var item = ToListedItem(r, loadIcon: true);
				if (item is not null)
					list.Add(item);
			}
			return list;
		}

		private static string? ScopeFor(string? folder)
		{
			// FolderSearch uses sentinel "Home" for global; treat null/empty/Home all as no scope.
			if (string.IsNullOrEmpty(folder) || folder == "Home")
				return null;
			return folder;
		}

		private static ListedItem? ToListedItem(EverythingResult r, bool loadIcon)
		{
			if (string.IsNullOrEmpty(r.FullPath) || string.IsNullOrEmpty(r.FileName))
				return null;

			var isHidden = (r.Attributes & AttrHidden) != 0;
			var modified = r.DateModifiedFileTime is long m && m > 0 ? DateTime.FromFileTimeUtc(m).ToLocalTime() : default;
			var created = r.DateCreatedFileTime is long c && c > 0 ? DateTime.FromFileTimeUtc(c).ToLocalTime() : default;

			ListedItem item;
			if (r.IsFolder)
			{
				item = new ListedItem(null)
				{
					PrimaryItemAttribute = StorageItemTypes.Folder,
					ItemNameRaw = r.FileName,
					ItemPath = r.FullPath,
					ItemDateModifiedReal = modified,
					ItemDateCreatedReal = created,
					IsHiddenItem = isHidden,
					LoadFileIcon = false,
					Opacity = isHidden ? Constants.UI.DimItemOpacity : 1,
				};
			}
			else
			{
				string? ext = null;
				string? type = null;
				if (r.FileName.Contains('.', StringComparison.Ordinal))
				{
					ext = Path.GetExtension(r.FullPath);
					type = ext.Trim('.');
				}

				var size = r.Size ?? 0;
				item = new ListedItem(null)
				{
					PrimaryItemAttribute = StorageItemTypes.File,
					ItemNameRaw = r.FileName,
					ItemPath = r.FullPath,
					ItemDateModifiedReal = modified,
					ItemDateCreatedReal = created,
					IsHiddenItem = isHidden,
					LoadFileIcon = false,
					FileExtension = ext,
					ItemType = type,
					Opacity = isHidden ? Constants.UI.DimItemOpacity : 1,
					FileSize = size.ToSizeString(),
					FileSizeBytes = size,
				};
			}

			if (loadIcon)
			{
				_ = FileThumbnailHelper.GetIconAsync(
					item.ItemPath,
					Constants.ShellIconSizes.Small,
					r.IsFolder,
					IconOptions.ReturnIconOnly | IconOptions.UseCurrentScale)
					.ContinueWith(t =>
					{
						if (t.IsCompletedSuccessfully && t.Result is not null)
						{
							_ = FilesystemTasks.Wrap(() => MainWindow.Instance.DispatcherQueue.EnqueueOrInvokeAsync(async () =>
							{
								var bmp = await t.Result.ToBitmapAsync();
								if (bmp is not null)
									item.FileImage = bmp;
							}, Microsoft.UI.Dispatching.DispatcherQueuePriority.Low));
						}
					});
			}

			return item;
		}
	}
}
