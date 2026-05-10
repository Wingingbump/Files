// Copyright (c) Files Community
// Licensed under the MIT License.

using Microsoft.Extensions.Logging;
using System.IO;
using System.Runtime.InteropServices;

namespace Files.App.Services.Search
{
	/// <summary>
	/// Single-process client for the Everything SDK. The native API is stateful (SetSearch →
	/// Query → GetResult*) so all access is serialized through <see cref="_apiLock"/>.
	/// </summary>
	public sealed class EverythingClient : IEverythingClient
	{
		// Request flags — match Everything SDK headers.
		private const uint REQUEST_FILE_NAME = 0x00000001;
		private const uint REQUEST_PATH = 0x00000002;
		private const uint REQUEST_SIZE = 0x00000010;
		private const uint REQUEST_DATE_CREATED = 0x00000020;
		private const uint REQUEST_DATE_MODIFIED = 0x00000040;
		private const uint REQUEST_ATTRIBUTES = 0x00000100;

		private const uint REQUEST_DEFAULT =
			REQUEST_FILE_NAME |
			REQUEST_PATH |
			REQUEST_SIZE |
			REQUEST_DATE_MODIFIED |
			REQUEST_DATE_CREATED |
			REQUEST_ATTRIBUTES;

		// Logical name resolved by SetDllImportResolver to the architecture-specific bundled DLL.
		private const string DllName = "Everything";

		[DllImport(DllName, EntryPoint = "Everything_SetSearchW", CharSet = CharSet.Unicode)] private static extern uint Everything_SetSearchW(string lpSearchString);
		[DllImport(DllName, EntryPoint = "Everything_SetRequestFlags")] private static extern void Everything_SetRequestFlags(uint dwRequestFlags);
		[DllImport(DllName, EntryPoint = "Everything_SetMax")] private static extern void Everything_SetMax(uint dwMax);
		[DllImport(DllName, EntryPoint = "Everything_SetMatchPath")] private static extern void Everything_SetMatchPath(bool bEnable);
		[DllImport(DllName, EntryPoint = "Everything_QueryW")] private static extern bool Everything_QueryW(bool bWait);
		[DllImport(DllName, EntryPoint = "Everything_Reset")] private static extern void Everything_Reset();
		[DllImport(DllName, EntryPoint = "Everything_GetNumResults")] private static extern uint Everything_GetNumResults();
		[DllImport(DllName, EntryPoint = "Everything_IsFolderResult")] private static extern bool Everything_IsFolderResult(uint nIndex);
		[DllImport(DllName, EntryPoint = "Everything_GetResultPath", CharSet = CharSet.Unicode)] private static extern IntPtr Everything_GetResultPath(uint nIndex);
		[DllImport(DllName, EntryPoint = "Everything_GetResultFileName", CharSet = CharSet.Unicode)] private static extern IntPtr Everything_GetResultFileName(uint nIndex);
		[DllImport(DllName, EntryPoint = "Everything_GetResultSize")] private static extern bool Everything_GetResultSize(uint nIndex, out long lpFileSize);
		[DllImport(DllName, EntryPoint = "Everything_GetResultDateModified")] private static extern bool Everything_GetResultDateModified(uint nIndex, out long lpFileTime);
		[DllImport(DllName, EntryPoint = "Everything_GetResultDateCreated")] private static extern bool Everything_GetResultDateCreated(uint nIndex, out long lpFileTime);
		[DllImport(DllName, EntryPoint = "Everything_GetResultAttributes")] private static extern uint Everything_GetResultAttributes(uint nIndex);
		[DllImport(DllName, EntryPoint = "Everything_IsDBLoaded")] private static extern bool Everything_IsDBLoaded();
		[DllImport(DllName, EntryPoint = "Everything_GetLastError")] private static extern uint Everything_GetLastError();

		// We deliberately do NOT P/Invoke Everything_CleanUp. The SDK examples don't call it, and
		// the reference branch (PR #17336) reported access violations after the resolver hot-path.
		// Reset() between queries is sufficient to release internal result lists.

		private readonly ILogger<EverythingClient> _logger;
		private readonly SemaphoreSlim _apiLock = new(1, 1);
		private bool _resolverRegistered;
		private bool _availabilityCached;
		private bool _availabilityCacheValid;
		private DateTime _availabilityCachedAt;
		private static readonly TimeSpan AvailabilityCacheTtl = TimeSpan.FromSeconds(5);

		public EverythingClient(ILogger<EverythingClient> logger)
		{
			_logger = logger;
			RegisterResolver();
		}

		public bool IsAvailable
		{
			get
			{
				if (_availabilityCacheValid && (DateTime.UtcNow - _availabilityCachedAt) < AvailabilityCacheTtl)
					return _availabilityCached;

				_availabilityCached = ProbeAvailability();
				_availabilityCacheValid = true;
				_availabilityCachedAt = DateTime.UtcNow;
				return _availabilityCached;
			}
		}

		public async Task<IReadOnlyList<EverythingResult>> SearchAsync(string query, string? scope, uint maxResults, CancellationToken cancellationToken)
		{
			if (string.IsNullOrEmpty(query))
				return Array.Empty<EverythingResult>();

			cancellationToken.ThrowIfCancellationRequested();

			var effectiveQuery = BuildScopedQuery(query, scope);

			return await Task.Run(() =>
			{
				_apiLock.Wait(cancellationToken);
				try
				{
					if (!ProbeAvailability())
						return (IReadOnlyList<EverythingResult>)Array.Empty<EverythingResult>();

					Everything_Reset();
					Everything_SetSearchW(effectiveQuery);
					Everything_SetRequestFlags(REQUEST_DEFAULT);
					Everything_SetMatchPath(scope is not null);
					Everything_SetMax(maxResults == 0 ? uint.MaxValue : maxResults);

					if (!Everything_QueryW(bWait: true))
					{
						_logger.LogDebug("Everything_QueryW returned false (last error {Err})", Everything_GetLastError());
						return Array.Empty<EverythingResult>();
					}

					cancellationToken.ThrowIfCancellationRequested();

					var count = Everything_GetNumResults();
					var results = new List<EverythingResult>((int)Math.Min(count, 4096));
					for (uint i = 0; i < count; i++)
					{
						if ((i & 0x3F) == 0)
							cancellationToken.ThrowIfCancellationRequested();
						results.Add(ReadResult(i));
					}
					return (IReadOnlyList<EverythingResult>)results;
				}
				finally
				{
					_apiLock.Release();
				}
			}, cancellationToken);
		}

		public async Task<ulong> GetFolderSizeAsync(string folderPath, CancellationToken cancellationToken)
		{
			if (string.IsNullOrEmpty(folderPath))
				return 0;

			cancellationToken.ThrowIfCancellationRequested();

			// Everything's `size:` operator + `path:` filter sums file sizes under a path.
			// Using the path-scoped wildcard match: we ask for everything under the folder and
			// sum the sizes locally. Everything itself doesn't expose a "give me the total" call.
			var query = $"\"{folderPath.TrimEnd('\\')}\\\" !folder:";

			return await Task.Run(() =>
			{
				_apiLock.Wait(cancellationToken);
				try
				{
					if (!ProbeAvailability())
						return 0UL;

					Everything_Reset();
					Everything_SetSearchW(query);
					Everything_SetRequestFlags(REQUEST_SIZE);
					Everything_SetMatchPath(true);
					Everything_SetMax(uint.MaxValue);

					if (!Everything_QueryW(bWait: true))
						return 0UL;

					cancellationToken.ThrowIfCancellationRequested();

					var count = Everything_GetNumResults();
					ulong total = 0;
					for (uint i = 0; i < count; i++)
					{
						if ((i & 0xFF) == 0)
							cancellationToken.ThrowIfCancellationRequested();
						if (Everything_GetResultSize(i, out var size) && size > 0)
							total += (ulong)size;
					}
					return total;
				}
				finally
				{
					_apiLock.Release();
				}
			}, cancellationToken);
		}

		// --- internals ---

		private bool ProbeAvailability()
		{
			try
			{
				return Everything_IsDBLoaded();
			}
			catch (DllNotFoundException)
			{
				_logger.LogTrace("Everything DLL not found — expected at Libraries/Everything*.dll");
				return false;
			}
			catch (Exception ex)
			{
				_logger.LogTrace(ex, "Everything availability probe failed");
				return false;
			}
		}

		private static string BuildScopedQuery(string query, string? scope)
		{
			if (string.IsNullOrEmpty(scope))
				return query;

			// Everything supports `path:"<dir>"` to scope; preserve the user's other operators.
			var scoped = scope.TrimEnd('\\');
			return $"path:\"{scoped}\\\" {query}";
		}

		private static EverythingResult ReadResult(uint i)
		{
			var pathPtr = Everything_GetResultPath(i);
			var namePtr = Everything_GetResultFileName(i);
			var path = pathPtr != IntPtr.Zero ? Marshal.PtrToStringUni(pathPtr) ?? string.Empty : string.Empty;
			var name = namePtr != IntPtr.Zero ? Marshal.PtrToStringUni(namePtr) ?? string.Empty : string.Empty;
			var fullPath = string.IsNullOrEmpty(path) ? name : Path.Combine(path, name);

			long? size = Everything_GetResultSize(i, out var sz) ? sz : (long?)null;
			long? mod = Everything_GetResultDateModified(i, out var m) ? m : (long?)null;
			long? cre = Everything_GetResultDateCreated(i, out var c) ? c : (long?)null;
			var attrs = Everything_GetResultAttributes(i);
			var isFolder = Everything_IsFolderResult(i);

			return new EverythingResult(fullPath, name, isFolder, size, mod, cre, attrs);
		}

		private void RegisterResolver()
		{
			if (_resolverRegistered)
				return;

			NativeLibrary.SetDllImportResolver(typeof(EverythingClient).Assembly, (name, asm, path) =>
			{
				if (!string.Equals(name, DllName, StringComparison.Ordinal))
					return IntPtr.Zero;

				var arch = RuntimeInformation.ProcessArchitecture switch
				{
					Architecture.Arm64 => "EverythingARM64.dll",
					Architecture.X64 => "Everything64.dll",
					Architecture.X86 => "Everything32.dll",
					_ => "Everything64.dll",
				};

				// DLLs are copied to Libraries\ in the output directory.
				var assemblyDir = Path.GetDirectoryName(asm.Location) ?? string.Empty;
				var fullPath = Path.Combine(assemblyDir, "Libraries", arch);
				if (NativeLibrary.TryLoad(fullPath, out var handle))
					return handle;

				return IntPtr.Zero;
			});
			_resolverRegistered = true;
		}
	}
}
