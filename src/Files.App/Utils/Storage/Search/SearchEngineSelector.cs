// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Utils.Storage.Search
{
	/// <summary>
	/// Picks the active <see cref="ISearchEngineService"/> from settings, with availability-aware
	/// fallback. Cheap to call repeatedly — no allocations on the hot path.
	/// </summary>
	public sealed class SearchEngineSelector : ISearchEngineSelector
	{
		private readonly IGeneralSettingsService _settings;
		private readonly WindowsSearchEngineService _windows;
		private readonly EverythingSearchEngineService _everything;

		public SearchEngineSelector(
			IGeneralSettingsService settings,
			WindowsSearchEngineService windows,
			EverythingSearchEngineService everything)
		{
			_settings = settings;
			_windows = windows;
			_everything = everything;
		}

		public ISearchEngineService Preferred => _settings.PreferredSearchEngine switch
		{
			PreferredSearchEngine.Everything => _everything,
			_ => _windows,
		};

		public ISearchEngineService Current
		{
			get
			{
				var preferred = Preferred;
				return preferred.IsAvailable ? preferred : _windows;
			}
		}

		public bool IsFallback => !Preferred.IsAvailable;
	}
}
