// Copyright (c) Files Community
// Licensed under the MIT License.

namespace Files.App.Data.Enums
{
	/// <summary>
	/// Defines constants that specify which search engine Files should use.
	/// </summary>
	public enum PreferredSearchEngine
	{
		/// <summary>
		/// Windows Search (default). Always available.
		/// </summary>
		Windows = 0,

		/// <summary>
		/// Everything (voidtools). Used when installed and running; otherwise falls back to Windows Search.
		/// </summary>
		Everything = 1,
	}
}
