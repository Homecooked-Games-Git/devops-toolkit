using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;
using UnityEngine.Networking;

namespace HomecookedGames.DevOps.Editor
{
    public class EssentialsTab
    {
        const string PluginsRepoBase = "https://github.com/Homecooked-Games-Git/unity-plugins.git";
        const string RawRepoBase = "https://raw.githubusercontent.com/Homecooked-Games-Git/unity-plugins/main";

        struct PluginInfo
        {
            public string DisplayName;
            public string PackageName;
            public string SubPath; // subfolder in the monorepo

            public string GitUrl => $"{PluginsRepoBase}?path=/{SubPath}#main";
            public string RemotePackageJsonUrl => $"{RawRepoBase}/{SubPath}/package.json";
        }

        static readonly PluginInfo[] Plugins =
        {
            new() { DisplayName = "SRDebugger", PackageName = "com.stompyrobot.srdebugger", SubPath = "SRDebugger" },
            new() { DisplayName = "Odin Inspector", PackageName = "com.sirenix.odin-inspector", SubPath = "OdinInspector" },
            new() { DisplayName = "DoTween", PackageName = "com.demigiant.dotween", SubPath = "DoTween" },
            new() { DisplayName = "Easy Save 3", PackageName = "com.moodkie.easysave3", SubPath = "EasySave3" },
            new() { DisplayName = "HotReload", PackageName = "com.singularitygroup.hotreload", SubPath = "HotReload" },
        };

        // Installed package state
        struct InstalledInfo
        {
            public bool Installed;
            public string Version;
        }

        // Remote version state
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
        string _pendingAction; // package name being added/removed
        bool _isCheckingUpdates;
        readonly List<UnityWebRequestAsyncOperation> _versionChecks = new();

        Action _repaintCallback;

        public void SetRepaintCallback(Action repaint)
        {
            _repaintCallback = repaint;
        }

        public void OnEnable()
        {
            RefreshInstalledPackages();
        }

        public void OnGUI()
        {
            EditorGUILayout.LabelField("Essential Plugins", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

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

            // Action buttons
            GUI.enabled = !IsBusy;
            if (info.Installed)
            {
                if (updateAvailable && GUILayout.Button("Update", GUILayout.Width(60)))
                    AddPackage(plugin);
                if (GUILayout.Button("Remove", GUILayout.Width(60)))
                    RemovePackage(plugin);
            }
            else
            {
                if (GUILayout.Button("Add", GUILayout.Width(60)))
                    AddPackage(plugin);
            }
            GUI.enabled = true;

            EditorGUILayout.EndHorizontal();
        }

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
                            Version = pkg.version
                        };
                    }
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
            _isCheckingUpdates = true;
            _versionChecks.Clear();
            _remoteVersions.Clear();

            foreach (var plugin in Plugins)
            {
                var url = plugin.RemotePackageJsonUrl;
                var request = UnityWebRequest.Get(url);
                var op = request.SendWebRequest();
                var packageName = plugin.PackageName;
                op.completed += _ =>
                {
                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        var version = ExtractVersionFromJson(request.downloadHandler.text);
                        _remoteVersions[packageName] = new RemoteInfo { Fetched = true, Version = version };
                    }
                    else
                    {
                        _remoteVersions[packageName] = new RemoteInfo { Fetched = true, Version = null };
                    }

                    request.Dispose();
                    CheckUpdatesDone();
                };
                _versionChecks.Add(op);
            }
        }

        void CheckUpdatesDone()
        {
            if (_remoteVersions.Count < Plugins.Length) return;
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

        bool IsBusy => _pendingAction != null || _isCheckingUpdates;
    }
}
