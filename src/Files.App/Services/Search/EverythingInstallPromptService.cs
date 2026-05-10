// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.App.Utils.Storage.Search;

namespace Files.App.Services.Search
{
	/// <summary>
	/// Decides whether to surface the "Everything not running" install prompt and persists
	/// dismissal. Does not own any UI — callers are responsible for showing the TeachingTip.
	/// </summary>
	public sealed class EverythingInstallPromptService
	{
		private readonly ISearchEngineSelector _selector;
		private readonly IGeneralSettingsService _settings;

		public EverythingInstallPromptService(ISearchEngineSelector selector, IGeneralSettingsService settings)
		{
			_selector = selector;
			_settings = settings;
		}

		/// <summary>
		/// True when the user wants Everything but it isn't available and hasn't dismissed the prompt.
		/// </summary>
		public bool ShouldShow =>
			_selector.IsFallback && !_settings.EverythingInstallPromptDismissed;

		public void Dismiss()
		{
			_settings.EverythingInstallPromptDismissed = true;
		}
	}
}
