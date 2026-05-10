// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Utils.Storage.Search
{
	/// <summary>
	/// Resolves the active <see cref="ISearchEngineService"/> from user settings, with fallback
	/// to a guaranteed-available engine (Windows Search) when the preferred one is unavailable.
	/// </summary>
	public interface ISearchEngineSelector
	{
		/// <summary>
		/// The engine to use right now. Re-evaluated on each access; cheap.
		/// </summary>
		ISearchEngineService Current { get; }

		/// <summary>
		/// The user's preferred engine, regardless of availability. Useful for UI (e.g. the
		/// install-prompt knows the user wanted Everything even though we're falling back).
		/// </summary>
		ISearchEngineService Preferred { get; }

		/// <summary>
		/// True when <see cref="Current"/> differs from <see cref="Preferred"/> due to unavailability.
		/// </summary>
		bool IsFallback { get; }
	}
}
