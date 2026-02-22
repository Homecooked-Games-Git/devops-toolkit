using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace HomecookedGames.DevOps.Editor
{
    public class EssentialsTab
    {
        enum InstallMethod { AssetStore, UPM }

        struct PluginInfo
        {
            public string DisplayName;
            public InstallMethod Method;

            // Asset Store plugins
            public string AssetsFolderPath; // detection path under Assets/
            public string AssetStoreUrl;    // link to open for installation

            // UPM plugins
            public string PackageName;      // UPM package name
            public string UpmUrl;           // git URL or registry identifier
        }

        static readonly PluginInfo[] Plugins =
        {
            new()
            {
                DisplayName = "SRDebugger",
                Method = InstallMethod.AssetStore,
                AssetsFolderPath = "Assets/Plugins/StompyRobot",
                AssetStoreUrl = "https://assetstore.unity.com/packages/tools/gui/srdebugger-console-tools-on-device-27688"
            },
            new()
            {
                DisplayName = "Odin Inspector",
                Method = InstallMethod.AssetStore,
                AssetsFolderPath = "Assets/Plugins/Sirenix",
                AssetStoreUrl = "https://assetstore.unity.com/packages/tools/utilities/odin-inspector-and-serializer-89041"
            },
            new()
            {
                DisplayName = "DoTween Pro",
                Method = InstallMethod.AssetStore,
                AssetsFolderPath = "Assets/Plugins/Demigiant",
                AssetStoreUrl = "https://assetstore.unity.com/packages/tools/visual-scripting/dotween-pro-32416"
            },
            new()
            {
                DisplayName = "Easy Save 3",
                Method = InstallMethod.AssetStore,
                AssetsFolderPath = "Assets/Plugins/Easy Save 3",
                AssetStoreUrl = "https://assetstore.unity.com/packages/tools/utilities/easy-save-the-complete-save-data-serializer-system-768"
            },
            new()
            {
                DisplayName = "HotReload",
                Method = InstallMethod.UPM,
                PackageName = "com.singularitygroup.hotreload",
                UpmUrl = "https://hotreload.net"
            },
        };

        struct InstalledInfo
        {
            public bool Installed;
            public string Version;
        }

        readonly Dictionary<string, InstalledInfo> _installed = new();

        ListRequest _listRequest;
        RemoveRequest _removeRequest;
        string _pendingAction;

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
            EditorGUILayout.Space(8);

            foreach (var plugin in Plugins)
            {
                DrawPluginRow(plugin);
            }

            if (_pendingAction != null)
            {
                EditorGUILayout.Space(8);
                EditorGUILayout.HelpBox("Package operation in progress...", MessageType.Info);
            }
        }

        void DrawPluginRow(PluginInfo plugin)
        {
            var key = plugin.Method == InstallMethod.UPM ? plugin.PackageName : plugin.AssetsFolderPath;
            _installed.TryGetValue(key, out var info);

            EditorGUILayout.BeginHorizontal();

            // Status icon
            var icon = info.Installed
                ? EditorGUIUtility.IconContent("TestPassed")
                : EditorGUIUtility.IconContent("TestFailed");
            GUILayout.Label(icon, GUILayout.Width(20), GUILayout.Height(20));

            // Plugin name
            GUILayout.Label(plugin.DisplayName, GUILayout.Width(120));

            // Version / source
            if (info.Installed)
            {
                GUILayout.Label(info.Version, EditorStyles.miniLabel, GUILayout.Width(100));
            }
            else
            {
                GUILayout.Label("Not installed", EditorStyles.miniLabel, GUILayout.Width(100));
            }

            GUILayout.FlexibleSpace();

            // Action buttons
            GUI.enabled = _pendingAction == null;

            if (info.Installed)
            {
                if (plugin.Method == InstallMethod.AssetStore)
                {
                    if (GUILayout.Button("Asset Store", GUILayout.Width(80)))
                        Application.OpenURL(plugin.AssetStoreUrl);
                }
                else
                {
                    if (GUILayout.Button("Remove", GUILayout.Width(60)))
                        RemoveUpmPackage(plugin);
                }
            }
            else
            {
                if (plugin.Method == InstallMethod.AssetStore)
                {
                    if (GUILayout.Button("Open Asset Store", GUILayout.Width(110)))
                        Application.OpenURL(plugin.AssetStoreUrl);
                }
                else
                {
                    if (GUILayout.Button("Website", GUILayout.Width(70)))
                        Application.OpenURL(plugin.UpmUrl);
                }
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

            // Check UPM packages
            if (_listRequest.Status == StatusCode.Success)
            {
                var upmPlugins = Plugins.Where(p => p.Method == InstallMethod.UPM).ToArray();
                var packageNames = new HashSet<string>(upmPlugins.Select(p => p.PackageName));
                foreach (var pkg in _listRequest.Result)
                {
                    if (packageNames.Contains(pkg.name))
                    {
                        _installed[pkg.name] = new InstalledInfo
                        {
                            Installed = true,
                            Version = $"v{pkg.version}"
                        };
                    }
                }
            }

            // Check Assets/Plugins/ folders for Asset Store plugins
            var projectRoot = Path.GetDirectoryName(Application.dataPath);
            foreach (var plugin in Plugins)
            {
                if (plugin.Method != InstallMethod.AssetStore) continue;

                var fullPath = Path.Combine(projectRoot, plugin.AssetsFolderPath);
                _installed[plugin.AssetsFolderPath] = new InstalledInfo
                {
                    Installed = Directory.Exists(fullPath),
                    Version = Directory.Exists(fullPath) ? "Installed" : null
                };
            }

            _repaintCallback?.Invoke();
        }

        void RemoveUpmPackage(PluginInfo plugin)
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
    }
}
