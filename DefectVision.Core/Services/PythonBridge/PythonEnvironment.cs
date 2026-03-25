using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Python.Runtime;

namespace DefectVision.Core.Services.PythonBridge
{
    public sealed class PythonEnvironment
    {
        private static readonly Lazy<PythonEnvironment> _instance =
            new Lazy<PythonEnvironment>(() => new PythonEnvironment());

        public static PythonEnvironment Instance => _instance.Value;

        /// <summary>
        /// Python DLL path (for other modules to find python.exe)
        /// </summary>
        public string DllPath { get; private set; } = "";

        public bool IsInitialized => _initialized;

        private bool _initialized;
        private readonly object _lock = new object();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetDllDirectory(string lpPathName);

        private PythonEnvironment() { }

        public void Initialize(string pythonDllPath)
        {
            DllPath = pythonDllPath; // Always save

            lock (_lock)
            {
                if (_initialized) return;

                if (!File.Exists(pythonDllPath))
                    throw new FileNotFoundException($"Python DLL not found: {pythonDllPath}");

                string pythonHome = Path.GetDirectoryName(pythonDllPath);
                SetupEnvironmentPaths(pythonHome);

                IntPtr handle = LoadLibrary(pythonDllPath);
                if (handle == IntPtr.Zero)
                    throw new DllNotFoundException(
                        $"Cannot load: {pythonDllPath} (error: {Marshal.GetLastWin32Error()})");

                Runtime.PythonDLL = pythonDllPath;
                PythonEngine.Initialize();
                PythonEngine.BeginAllowThreads();

                EnsureSysPath(pythonHome);

                _initialized = true;
            }
        }

        public T Execute<T>(Func<T> action)
        {
            if (!_initialized)
                throw new InvalidOperationException("Python not initialized");

            using (Py.GIL())
            {
                return action();
            }
        }

        public void Execute(Action action)
        {
            if (!_initialized)
                throw new InvalidOperationException("Python not initialized");

            using (Py.GIL())
            {
                action();
            }
        }

        private void SetupEnvironmentPaths(string pythonHome)
        {
            var dirs = new[]
            {
                pythonHome,
                Path.Combine(pythonHome, "DLLs"),
                Path.Combine(pythonHome, "Library", "bin"),
                Path.Combine(pythonHome, "Library", "lib"),
                Path.Combine(pythonHome, "Scripts"),
            };

            string currentPath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Process) ?? "";
            var toAdd = dirs.Where(d => Directory.Exists(d) && !currentPath.Contains(d));
            if (toAdd.Any())
            {
                Environment.SetEnvironmentVariable("PATH",
                    string.Join(";", toAdd) + ";" + currentPath,
                    EnvironmentVariableTarget.Process);
            }

            SetDllDirectory(pythonHome);
        }

        private string BuildPythonPath(string pythonHome)
        {
            var candidates = new List<string>
            {
                pythonHome,
                Path.Combine(pythonHome, "Lib"),
                Path.Combine(pythonHome, "Lib", "site-packages"),
                Path.Combine(pythonHome, "DLLs"),
            };

            try { candidates.AddRange(Directory.GetFiles(pythonHome, "python*.zip")); } catch { }

            string sp = Path.Combine(pythonHome, "Lib", "site-packages");
            if (Directory.Exists(sp))
            {
                try
                {
                    foreach (var pth in Directory.GetFiles(sp, "*.pth"))
                        foreach (var line in File.ReadAllLines(pth))
                        {
                            var t = line.Trim();
                            if (string.IsNullOrEmpty(t) || t.StartsWith("#") || t.StartsWith("import")) continue;
                            var full = Path.IsPathRooted(t) ? t : Path.GetFullPath(Path.Combine(sp, t));
                            if (Directory.Exists(full)) candidates.Add(full);
                        }
                }
                catch { }
            }

            return string.Join(";", candidates.Where(p => Directory.Exists(p) || File.Exists(p)).Distinct(StringComparer.OrdinalIgnoreCase));
        }

        private void EnsureSysPath(string pythonHome)
        {
            try
            {
                using (Py.GIL())
                {
                    dynamic sys = Py.Import("sys");
                    var paths = new[]
                    {
                        pythonHome,
                        Path.Combine(pythonHome, "Lib"),
                        Path.Combine(pythonHome, "Lib", "site-packages"),
                        Path.Combine(pythonHome, "DLLs"),
                    };
                    foreach (var p in paths.Where(Directory.Exists))
                    {
                        bool exists = false;
                        foreach (var e in sys.path)
                            if (string.Equals(e.ToString(), p, StringComparison.OrdinalIgnoreCase)) { exists = true; break; }
                        if (!exists) sys.path.append(p);
                    }
                }
            }
            catch { }
        }
    }
}