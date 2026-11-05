using Microsoft.ML.OnnxRuntime;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

/// <summary>
/// Loads the ONNX Runtime native library of the Bonsai environment's packages and, for the CUDA
/// execution provider, puts the CUDA and cuDNN libraries it needs on the native search path.
/// </summary>
/// <remarks>
/// Windows 11 ships an older onnxruntime.dll in System32, which the native search order finds before
/// the package's copy on PATH, and the managed 1.24 assembly then fails with an access violation.
/// Loading the package's copy by full path before the first ONNX Runtime call makes every later load
/// by name resolve to it. The GPU build is preferred when it is installed, since it is a superset of
/// the CPU build. Each ONNX Runtime release builds its CUDA provider for one CUDA major version (1.24 for
/// CUDA 12, 1.31 for CUDA 13), so the CUDA libraries it needs are read from the provider's own imports,
/// and cuDNN 9, which it loads later by name, is checked as well.
/// </remarks>
static class OnnxRuntimeLibrary
{
    const string RuntimeLibrary = "onnxruntime.dll";
    const string CudaProviderLibrary = "onnxruntime_providers_cuda.dll";
    const string ManagedPackagePrefix = "Microsoft.ML.OnnxRuntime.Managed.";
    const uint LoadWithAlteredSearchPath = 0x8;
    const string CudnnLibrary = "cudnn64_9.dll";
    static readonly object gate = new object();

    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr LoadLibraryEx(string fileName, IntPtr reserved, uint flags);

    [DllImport("kernel32", CharSet = CharSet.Unicode)]
    static extern IntPtr GetModuleHandle(string moduleName);

    [DllImport("kernel32", CharSet = CharSet.Unicode)]
    static extern uint GetModuleFileName(IntPtr module, StringBuilder fileName, uint size);

    /// <summary>
    /// Loads the package's onnxruntime.dll unless one is already loaded, and returns the path of the
    /// loaded copy, or null when no copy was found and the default search order will apply.
    /// </summary>
    public static string EnsureLoaded()
    {
        lock (gate)
        {
            var loaded = LoadedPath();
            if (loaded == null)
            {
                var candidate = FindRuntime();
                if (candidate == null) return null;
                if (LoadLibraryEx(candidate, IntPtr.Zero, LoadWithAlteredSearchPath) == IntPtr.Zero)
                {
                    throw new InvalidOperationException(string.Format(
                        "The ONNX Runtime library '{0}' could not be loaded (error {1}).", candidate, Marshal.GetLastWin32Error()));
                }
                loaded = LoadedPath() ?? candidate;
            }

            if (loaded.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(string.Format(
                    "Windows' own ONNX Runtime library '{0}' was loaded before the package's, which the package's ONNX Runtime assembly cannot use. " +
                    "Restart Bonsai so that the identity classifier loads first.", loaded));
            }
            return loaded;
        }
    }

    /// <summary>
    /// Checks that the GPU build of ONNX Runtime is loaded and that the CUDA libraries its provider imports
    /// and cuDNN 9 can be found, adding the given folders to the native search path first.
    /// </summary>
    /// <param name="libraryPath">Folders holding the CUDA and cuDNN libraries, separated by semicolons, or empty to use PATH only.</param>
    public static void EnsureCuda(string libraryPath)
    {
        lock (gate)
        {
            var runtime = EnsureLoaded();
            if (runtime == null || !File.Exists(Path.Combine(Path.GetDirectoryName(runtime), CudaProviderLibrary)))
            {
                throw new InvalidOperationException(
                    "The CUDA execution provider needs the GPU build of ONNX Runtime; install the Bonsai.MultiAnimalTracking.Gpu package.");
            }

            var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            var entries = path.Split(Path.PathSeparator).Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
            // Relative folders are taken from the working directory, which is the workflow's folder in Bonsai, and put on
            // PATH in full: cuDNN loads its libraries later, by name, and a relative PATH entry would not find them.
            var added = (libraryPath ?? string.Empty).Split(';').Select(p => p.Trim()).Where(p => p.Length > 0).Select(p => Path.GetFullPath(p)).ToArray();
            foreach (var folder in Enumerable.Reverse(added))
            {
                if (!Directory.Exists(folder))
                {
                    throw new InvalidOperationException("The CUDA library folder '" + folder + "' does not exist.");
                }
                if (!entries.Contains(folder, StringComparer.OrdinalIgnoreCase)) entries.Insert(0, folder);
            }
            if (added.Length > 0) Environment.SetEnvironmentVariable("PATH", string.Join(Path.PathSeparator.ToString(), entries.ToArray()));

            var provider = Path.Combine(Path.GetDirectoryName(runtime), CudaProviderLibrary);
            var needed = ImportedLibraries(provider).Where(name => name.StartsWith("cu", StringComparison.OrdinalIgnoreCase)).Concat(new[] { CudnnLibrary });
            var missing = needed.Distinct(StringComparer.OrdinalIgnoreCase).Where(library => !entries.Any(folder => SafeExists(folder, library))).ToArray();
            if (missing.Length > 0)
            {
                throw new InvalidOperationException(
                    "The CUDA execution provider of the installed ONNX Runtime needs " + string.Join(", ", missing) + ", which could not be found " +
                    "in CudaLibraryPath or on PATH. Install the CUDA version these names give and cuDNN 9, or NVIDIA's matching nvidia-*-cu12 " +
                    "or -cu13 wheels, and list the folders holding their DLLs in CudaLibraryPath.");
            }
        }
    }

    // The names of the DLLs a PE image imports, from its import directory.
    static string[] ImportedLibraries(string path)
    {
        var names = new List<string>();
        using (var reader = new BinaryReader(File.OpenRead(path)))
        {
            var stream = reader.BaseStream;
            stream.Position = 0x3C;
            stream.Position = reader.ReadInt32() + 4;
            reader.ReadUInt16();
            var sections = reader.ReadUInt16();
            stream.Position += 12;
            var optionalSize = reader.ReadUInt16();
            stream.Position += 2;
            var optional = stream.Position;
            var magic = reader.ReadUInt16();
            stream.Position = optional + (magic == 0x20B ? 120 : 104);
            var importRva = reader.ReadUInt32();
            stream.Position = optional + optionalSize;
            var table = new List<uint[]>();
            for (int i = 0; i < sections; i++)
            {
                stream.Position += 8;
                var virtualSize = reader.ReadUInt32();
                var virtualAddress = reader.ReadUInt32();
                var rawSize = reader.ReadUInt32();
                var rawPointer = reader.ReadUInt32();
                stream.Position += 16;
                table.Add(new[] { virtualAddress, Math.Max(virtualSize, rawSize), rawPointer });
            }
            Func<uint, long> offset = rva =>
            {
                foreach (var section in table)
                {
                    if (rva >= section[0] && rva < section[0] + section[1]) return rva - section[0] + section[2];
                }
                return -1;
            };
            var descriptor = offset(importRva);
            while (descriptor >= 0)
            {
                stream.Position = descriptor + 12;
                var nameRva = reader.ReadUInt32();
                if (nameRva == 0) break;
                var nameOffset = offset(nameRva);
                if (nameOffset >= 0)
                {
                    stream.Position = nameOffset;
                    var name = new StringBuilder();
                    int c;
                    while ((c = reader.ReadByte()) != 0) name.Append((char)c);
                    names.Add(name.ToString());
                }
                descriptor += 20;
            }
        }
        return names.ToArray();
    }

    static bool SafeExists(string folder, string file)
    {
        try { return File.Exists(Path.Combine(folder, file)); }
        catch (ArgumentException) { return false; }
    }

    static string LoadedPath()
    {
        var module = GetModuleHandle(RuntimeLibrary);
        if (module == IntPtr.Zero) return null;
        var name = new StringBuilder(1024);
        return GetModuleFileName(module, name, (uint)name.Capacity) > 0 ? name.ToString() : null;
    }

    // A copy next to the managed assembly is the one P/Invoke would load first, so it is taken as is.
    // Otherwise the native folders of the packages next to the managed one's (the GPU build first), then
    // PATH without the Windows folders, preferring a folder with the CUDA provider.
    static string FindRuntime()
    {
        var folders = new List<string>();
        var managed = typeof(InferenceSession).Assembly.Location;
        if (!string.IsNullOrEmpty(managed))
        {
            var folder = Path.GetDirectoryName(managed);
            if (SafeExists(folder, RuntimeLibrary)) return Path.Combine(folder, RuntimeLibrary);
            var package = Directory.GetParent(folder) == null ? null : Directory.GetParent(folder).Parent;
            if (package != null && package.Parent != null && package.Name.StartsWith(ManagedPackagePrefix, StringComparison.OrdinalIgnoreCase))
            {
                var version = package.Name.Substring(ManagedPackagePrefix.Length);
                var packages = package.Parent.FullName;
                folders.Add(Path.Combine(packages, "Microsoft.ML.OnnxRuntime.Gpu.Windows." + version, "runtimes", "win-x64", "native"));
                folders.Add(Path.Combine(packages, "Microsoft.ML.OnnxRuntime." + version, "runtimes", "win-x64", "native"));
            }
        }

        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        folders.AddRange(path.Split(Path.PathSeparator).Select(p => p.Trim())
            .Where(p => p.Length > 0 && !p.StartsWith(windows, StringComparison.OrdinalIgnoreCase)));

        var runtimes = folders.Where(f => SafeExists(f, RuntimeLibrary)).ToArray();
        var gpu = runtimes.FirstOrDefault(f => SafeExists(f, CudaProviderLibrary));
        var chosen = gpu ?? runtimes.FirstOrDefault();
        return chosen == null ? null : Path.Combine(chosen, RuntimeLibrary);
    }
}
