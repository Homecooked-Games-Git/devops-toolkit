using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace HomecookedGames.DevOps.Editor
{
    public class EssentialsTab
    {
        const string PluginsRepoSsh = "git@github.com:Homecooked-Games-Git/unity-plugins.git";

        struct PluginInfo
        {
            public string DisplayName;
            public string PackageName;
            public string SubPath; // subfolder in the monorepo (null if using custom git URL)
            public string AssetsFolderPath; // fallback detection path under Assets/ (null if UPM-only)
            public string CustomGitUrl; // override git URL for public packages (null = use private repo)
            public bool IsPrivateRepo => CustomGitUrl == null;

            public string GitUrl => CustomGitUrl ?? $"{PluginsRepoSsh}?path=/{SubPath}#main";
        }

        static readonly PluginInfo[] Plugins =
        {
            new() { DisplayName = "SRDebugger", PackageName = "com.stompyrobot.srdebugger", SubPath = "SRDebugger", AssetsFolderPath = "Assets/Plugins/StompyRobot" },
            new() { DisplayName = "Odin Inspector", PackageName = "com.sirenix.odin-inspector", SubPath = "OdinInspector", AssetsFolderPath = "Assets/Plugins/Sirenix" },
            new() { DisplayName = "DoTween", PackageName = "com.demigiant.dotween", SubPath = "DoTween", AssetsFolderPath = "Assets/Plugins/Demigiant" },
            new() { DisplayName = "Easy Save 3", PackageName = "com.moodkie.easysave3", SubPath = "EasySave3", AssetsFolderPath = "Assets/Plugins/Easy Save 3" },
            new() { DisplayName = "HotReload", PackageName = "com.singularitygroup.hotreload", SubPath = "HotReload", AssetsFolderPath = null },
            new() { DisplayName = "Unity MCP", PackageName = "com.coplaydev.unity-mcp", CustomGitUrl = "https://github.com/CoplayDev/unity-mcp.git?path=/MCPForUnity#main" },
        };

        enum InstallSource { None, UPM, AssetsFolder }

        struct InstalledInfo
        {
            public bool Installed;
            public string Version;
            public InstallSource Source;
        }

        struct RemoteInfo
        {
            public bool Fetched;
            public string Version;
        }

        readonly Dictionary<string, InstalledInfo> _installed = new();
        readonly Dictionary<string, RemoteInfo> _remoteVersions = new();

        ListRequest _listRequest;
        AddRequest _addRequest;
        RemoveRequest _removeRequest;
        string _pendingAction;
        bool _isCheckingUpdates;
        Process _updateCheckProcess;
        string _updateCheckTmpDir;

        // SSH auth state
        bool _sshChecked;
        bool _sshAvailable;
        bool _sshChecking;
        Process _sshProcess;

        Action _repaintCallback;

        public void SetRepaintCallback(Action repaint)
        {
            _repaintCallback = repaint;
        }

        public void OnEnable()
        {
            RefreshInstalledPackages();
            CheckSshAccess();
        }

        public void OnGUI()
        {
            EditorGUILayout.LabelField("Essential Plugins", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            if (_sshChecked && !_sshAvailable)
            {
                EditorGUILayout.HelpBox(
                    "SSH access to private plugin repo not available.\n" +
                    "Run in terminal: ssh -T git@github.com\n" +
                    "If that fails, set up SSH keys: https://docs.github.com/en/authentication/connecting-to-github-with-ssh",
                    MessageType.Warning);
                if (GUILayout.Button("Retry SSH Check"))
                    CheckSshAccess();
                EditorGUILayout.Space(4);
            }

            // Check for Updates button
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUI.enabled = !IsBusy;
            if (GUILayout.Button("Check for Updates", GUILayout.Width(140)))
                CheckForUpdates();
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);

            foreach (var plugin in Plugins)
            {
                DrawPluginRow(plugin);
            }

            if (IsBusy)
            {
                EditorGUILayout.Space(8);
                EditorGUILayout.HelpBox("Package operation in progress...", MessageType.Info);
            }
        }

        void DrawPluginRow(PluginInfo plugin)
        {
            _installed.TryGetValue(plugin.PackageName, out var info);
            _remoteVersions.TryGetValue(plugin.PackageName, out var remote);

            EditorGUILayout.BeginHorizontal();

            // Status icon
            var icon = info.Installed
                ? EditorGUIUtility.IconContent("TestPassed")
                : EditorGUIUtility.IconContent("TestFailed");
            GUILayout.Label(icon, GUILayout.Width(20), GUILayout.Height(20));

            // Plugin name
            GUILayout.Label(plugin.DisplayName, GUILayout.Width(120));

            // Version
            if (info.Installed)
                GUILayout.Label($"v{info.Version}", EditorStyles.miniLabel, GUILayout.Width(80));
            else
                GUILayout.Label("", GUILayout.Width(80));

            // Update available indicator
            bool updateAvailable = false;
            if (info.Installed && remote.Fetched && !string.IsNullOrEmpty(remote.Version))
            {
                updateAvailable = IsNewerVersion(remote.Version, info.Version);
            }

            if (updateAvailable)
            {
                var style = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = new Color(0.2f, 0.7f, 1f) } };
                GUILayout.Label($"v{remote.Version} available", style, GUILayout.Width(110));
            }
            else
            {
                GUILayout.Label("", GUILayout.Width(110));
            }

            GUILayout.FlexibleSpace();

            // Action buttons — disable private repo buttons if SSH not available
            bool canInstallPrivate = !plugin.IsPrivateRepo || _sshAvailable;
            GUI.enabled = !IsBusy && canInstallPrivate;

            if (info.Installed)
            {
                if (info.Source == InstallSource.AssetsFolder)
                {
                    if (GUILayout.Button("Migrate to UPM", GUILayout.Width(100)))
                    {
                        if (EditorUtility.DisplayDialog("Migrate to UPM",
                                $"This will install {plugin.DisplayName} via Package Manager.\n\n" +
                                $"After verifying it works, manually delete:\n{plugin.AssetsFolderPath}",
                                "Install", "Cancel"))
                            AddPackage(plugin);
                    }
                }
                else
                {
                    if (updateAvailable && GUILayout.Button("Update", GUILayout.Width(60)))
                        AddPackage(plugin);
                    if (GUILayout.Button("Remove", GUILayout.Width(60)))
                        RemovePackage(plugin);
                }
            }
            else
            {
                if (GUILayout.Button("Add", GUILayout.Width(60)))
                    AddPackage(plugin);
            }
            GUI.enabled = true;

            EditorGUILayout.EndHorizontal();
        }

        // ── SSH Auth Check ──

        void CheckSshAccess()
        {
            _sshChecking = true;
            _sshChecked = false;

            try
            {
                _sshProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "git",
                        Arguments = $"ls-remote {PluginsRepoSsh}",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };
                _sshProcess.Start();
                _sshProcess.BeginOutputReadLine();
                _sshProcess.BeginErrorReadLine();

                EditorApplication.update += PollSshCheck;
            }
            catch
            {
                _sshChecked = true;
                _sshAvailable = false;
                _sshChecking = false;
            }
        }

        void PollSshCheck()
        {
            if (_sshProcess == null || !_sshProcess.HasExited) return;

            EditorApplication.update -= PollSshCheck;
            _sshAvailable = _sshProcess.ExitCode == 0;
            _sshChecked = true;
            _sshChecking = false;
            _sshProcess.Dispose();
            _sshProcess = null;
            _repaintCallback?.Invoke();
        }

        // ── Package Operations ──

        void RefreshInstalledPackages()
        {
            _listRequest = Client.List(true);
            EditorApplication.update += PollListRequest;
        }

        void PollListRequest()
        {
            if (!_listRequest.IsCompleted) return;
            EditorApplication.update -= PollListRequest;

            _installed.Clear();

            // Check UPM packages
            if (_listRequest.Status == StatusCode.Success)
            {
                var packageNames = new HashSet<string>(Plugins.Select(p => p.PackageName));
                foreach (var pkg in _listRequest.Result)
                {
                    if (packageNames.Contains(pkg.name))
                    {
                        _installed[pkg.name] = new InstalledInfo
                        {
                            Installed = true,
                            Version = pkg.version,
                            Source = InstallSource.UPM
                        };
                    }
                }
            }

            // Fallback: check Assets/Plugins/ folders for non-UPM installs
            var projectRoot = Path.GetDirectoryName(Application.dataPath);
            foreach (var plugin in Plugins)
            {
                if (_installed.ContainsKey(plugin.PackageName)) continue;
                if (string.IsNullOrEmpty(plugin.AssetsFolderPath)) continue;

                var fullPath = Path.Combine(projectRoot, plugin.AssetsFolderPath);
                if (Directory.Exists(fullPath))
                {
                    _installed[plugin.PackageName] = new InstalledInfo
                    {
                        Installed = true,
                        Version = "Asset Store",
                        Source = InstallSource.AssetsFolder
                    };
                }
            }

            _repaintCallback?.Invoke();
        }

        void AddPackage(PluginInfo plugin)
        {
            _pendingAction = plugin.PackageName;
            _addRequest = Client.Add(plugin.GitUrl);
            EditorApplication.update += PollAddRequest;
        }

        void PollAddRequest()
        {
            if (!_addRequest.IsCompleted) return;
            EditorApplication.update -= PollAddRequest;

            if (_addRequest.Status == StatusCode.Failure)
                Debug.LogError($"Failed to add package: {_addRequest.Error.message}");

            _pendingAction = null;
            RefreshInstalledPackages();
        }

        void RemovePackage(PluginInfo plugin)
        {
            _pendingAction = plugin.PackageName;
            _removeRequest = Client.Remove(plugin.PackageName);
            EditorApplication.update += PollRemoveRequest;
        }

        void PollRemoveRequest()
        {
            if (!_removeRequest.IsCompleted) return;
            EditorApplication.update -= PollRemoveRequest;

            if (_removeRequest.Status == StatusCode.Failure)
                Debug.LogError($"Failed to remove package: {_removeRequest.Error.message}");

            _pendingAction = null;
            RefreshInstalledPackages();
        }

        void CheckForUpdates()
        {
            if (!_sshAvailable)
            {
                Debug.LogWarning("Cannot check for updates: SSH access not available.");
                return;
            }

            _isCheckingUpdates = true;
            _remoteVersions.Clear();

            // Shallow clone the private repo to a temp dir, then read package.json files
            _updateCheckTmpDir = Path.Combine(Path.GetTempPath(), "hc-plugin-update-check");
            if (Directory.Exists(_updateCheckTmpDir))
                Directory.Delete(_updateCheckTmpDir, true);

            _updateCheckProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = $"clone --depth 1 {PluginsRepoSsh} \"{_updateCheckTmpDir}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            try
            {
                _updateCheckProcess.Start();
                _updateCheckProcess.BeginOutputReadLine();
                _updateCheckProcess.BeginErrorReadLine();
                EditorApplication.update += PollUpdateCheck;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to check for updates: {ex.Message}");
                _isCheckingUpdates = false;
            }
        }

        void PollUpdateCheck()
        {
            if (_updateCheckProcess == null || !_updateCheckProcess.HasExited) return;

            EditorApplication.update -= PollUpdateCheck;
            var success = _updateCheckProcess.ExitCode == 0;
            _updateCheckProcess.Dispose();
            _updateCheckProcess = null;

            if (success && Directory.Exists(_updateCheckTmpDir))
            {
                // Read package.json from each plugin subfolder
                foreach (var plugin in Plugins)
                {
                    if (string.IsNullOrEmpty(plugin.SubPath))
                    {
                        _remoteVersions[plugin.PackageName] = new RemoteInfo { Fetched = true, Version = null };
                        continue;
                    }

                    var pkgJsonPath = Path.Combine(_updateCheckTmpDir, plugin.SubPath, "package.json");
                    if (File.Exists(pkgJsonPath))
                    {
                        var json = File.ReadAllText(pkgJsonPath);
                        var version = ExtractVersionFromJson(json);
                        _remoteVersions[plugin.PackageName] = new RemoteInfo { Fetched = true, Version = version };
                    }
                    else
                    {
                        _remoteVersions[plugin.PackageName] = new RemoteInfo { Fetched = true, Version = null };
                    }
                }

                // Clean up temp dir
                try { Directory.Delete(_updateCheckTmpDir, true); } catch { }
            }
            else
            {
                Debug.LogWarning("Failed to fetch remote plugin versions.");
                foreach (var plugin in Plugins)
                    _remoteVersions[plugin.PackageName] = new RemoteInfo { Fetched = true, Version = null };
            }

            _isCheckingUpdates = false;
            _repaintCallback?.Invoke();
        }

        static string ExtractVersionFromJson(string json)
        {
            var m = System.Text.RegularExpressions.Regex.Match(json, @"""version""\s*:\s*""([^""]+)""");
            return m.Success ? m.Groups[1].Value : null;
        }

        static bool IsNewerVersion(string remote, string local)
        {
            if (string.IsNullOrEmpty(remote) || string.IsNullOrEmpty(local)) return false;
            try
            {
                var r = new Version(remote);
                var l = new Version(local);
                return r > l;
            }
            catch
            {
                return remote != local;
            }
        }

        bool IsBusy => _pendingAction != null || _isCheckingUpdates || _sshChecking;
    }
}
