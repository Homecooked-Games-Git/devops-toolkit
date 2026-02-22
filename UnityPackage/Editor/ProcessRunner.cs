using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace HomecookedGames.DevOps.Editor
{
    public class ProcessRunner
    {
        public bool IsRunning { get; private set; }
        public string Output => _outputBuilder.ToString();
        public string Error => _errorBuilder.ToString();

        static readonly string[] ExtraSearchPaths = { "/opt/homebrew/bin", "/opt/homebrew/sbin", "/usr/local/bin" };

        readonly StringBuilder _outputBuilder = new();
        readonly StringBuilder _errorBuilder = new();

        Process _process;
        Action _onComplete;
        Action _repaintCallback;
        readonly Queue<(string fileName, string arguments, Action onComplete)> _commandQueue = new();

        public void SetRepaintCallback(Action repaint)
        {
            _repaintCallback = repaint;
        }

        /// <summary>Resolve a command name to its full path, checking extra directories Unity might miss.</summary>
        static string ResolveCommand(string fileName)
        {
            // Already an absolute path
            if (fileName.Contains("/") || fileName.Contains("\\"))
                return fileName;

            if (Application.platform == RuntimePlatform.WindowsEditor)
                return fileName;

            foreach (var dir in ExtraSearchPaths)
            {
                var candidate = Path.Combine(dir, fileName);
                if (File.Exists(candidate))
                    return candidate;
            }

            return fileName;
        }

        public void Run(string fileName, string arguments, string workingDirectory, Action onComplete = null)
        {
            if (IsRunning)
            {
                _commandQueue.Enqueue((fileName, arguments, onComplete));
                return;
            }

            IsRunning = true;
            _onComplete = onComplete;
            _outputBuilder.AppendLine($"> {fileName} {arguments}");

            var resolvedFileName = ResolveCommand(fileName);

            var startInfo = new ProcessStartInfo
            {
                FileName = resolvedFileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            // Ensure child processes see Homebrew paths first.
            // IMPORTANT: Use Environment (not EnvironmentVariables) — the older
            // StringDictionary lowercases keys, turning "PATH" into "path" which
            // Unix ignores. This broke #!/usr/bin/env shebangs (e.g. firebase's
            // #!/usr/bin/env node would find the wrong Node.js version).
            if (Application.platform != RuntimePlatform.WindowsEditor)
            {
                var env = startInfo.Environment;
                if (!env.TryGetValue("PATH", out var path))
                    path = "";

                // Only prepend Homebrew dirs — /usr/local/bin is kept for
                // ResolveCommand() fallback but should NOT take priority.
                var priority = new[] { "/opt/homebrew/bin", "/opt/homebrew/sbin" };
                for (var i = priority.Length - 1; i >= 0; i--)
                {
                    if (!path.Contains(priority[i]))
                        path = priority[i] + ":" + path;
                }

                env["PATH"] = path;
            }

            // On Windows, .cmd files need shell execution workaround
            if (Application.platform == RuntimePlatform.WindowsEditor && fileName == "firebase")
            {
                startInfo.FileName = "cmd.exe";
                startInfo.Arguments = $"/c firebase {arguments}";
            }

            _process = new Process { StartInfo = startInfo };
            _process.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null) _outputBuilder.AppendLine(e.Data);
            };
            _process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null) _errorBuilder.AppendLine(e.Data);
            };

            try
            {
                _process.Start();
                _process.BeginOutputReadLine();
                _process.BeginErrorReadLine();
                EditorApplication.update += PollProcess;
            }
            catch (Exception ex)
            {
                _outputBuilder.AppendLine($"Failed to start process: {ex.Message}");
                IsRunning = false;
                _onComplete?.Invoke();
            }
        }

        void PollProcess()
        {
            _repaintCallback?.Invoke();

            if (_process == null || !_process.HasExited) return;

            EditorApplication.update -= PollProcess;
            _process.Dispose();
            _process = null;
            IsRunning = false;

            var callback = _onComplete;
            _onComplete = null;
            callback?.Invoke();

            // Run next queued command
            if (_commandQueue.Count > 0)
            {
                var (fileName, arguments, onComplete) = _commandQueue.Dequeue();
                Run(fileName, arguments, "", onComplete);
            }
        }

        public void ClearOutput()
        {
            _outputBuilder.Clear();
            _errorBuilder.Clear();
        }

        public static string PythonCommand =>
            Application.platform == RuntimePlatform.WindowsEditor ? "python" : "python3";

        public static string FirebaseCommand => "firebase";
    }
}
