using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;
using UnityEngine.Networking;

namespace HomecookedGames.DevOps.Editor
{
    public class DevOpsDashboardWindow : EditorWindow
    {
        const string PackageName = "com.homecookedgames.devops";
        const string RemotePackageJsonUrl = "https://raw.githubusercontent.com/Homecooked-Games-Git/devops-toolkit/main/UnityPackage/package.json";
        const string GitUrl = "https://github.com/Homecooked-Games-Git/devops-toolkit.git?path=/UnityPackage#main";

        [SerializeField] int selectedTab;

        StatusChecker _checker;
        ProcessRunner _runner;
        CICDTab _cicdTab;
        BuildTab _buildTab;
        EssentialsTab _essentialsTab;
        Vector2 _scrollPos;

        // Self-update state
        string _currentVersion;
        string _remoteVersion;
        bool _updateChecked;
        bool _isUpdating;
        AddRequest _updateRequest;

        static readonly string[] TabNames = { "CI/CD", "Build", "Essentials" };

        [MenuItem("HCTools/DevOps Dashboard")]
        static void ShowWindow()
        {
            var window = GetWindow<DevOpsDashboardWindow>();
            window.titleContent = new GUIContent("DevOps Dashboard");
            window.minSize = new Vector2(480f, 400f);
            window.Show();
        }

        void OnEnable()
        {
            _checker = new StatusChecker();
            _runner = new ProcessRunner();
            _runner.SetRepaintCallback(Repaint);

            _cicdTab = new CICDTab(_checker, _runner);
            _cicdTab.OnEnable();

            _buildTab = new BuildTab(_checker, _runner);
            _buildTab.SetRepaintCallback(Repaint);
            _buildTab.OnEnable();

            _essentialsTab = new EssentialsTab();
            _essentialsTab.SetRepaintCallback(Repaint);
            _essentialsTab.OnEnable();

            LoadCurrentVersion();
            CheckForSelfUpdate();
        }

        void OnGUI()
        {
            // Header toolbar
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            selectedTab = GUILayout.Toolbar(selectedTab, TabNames, EditorStyles.toolbarButton);

            GUILayout.FlexibleSpace();

            // Version label
            if (!string.IsNullOrEmpty(_currentVersion))
                GUILayout.Label($"v{_currentVersion}", EditorStyles.miniLabel);

            // Update button
            if (_updateChecked && !string.IsNullOrEmpty(_remoteVersion) && IsNewer(_remoteVersion, _currentVersion))
            {
                GUI.enabled = !_isUpdating;
                if (GUILayout.Button($"Update to v{_remoteVersion}", EditorStyles.toolbarButton))
                    UpdateSelf();
                GUI.enabled = true;
            }

            if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(60)))
            {
                _checker.Refresh();
                _essentialsTab.OnEnable();
                CheckForSelfUpdate();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(8);

            // Tab content
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            switch (selectedTab)
            {
                case 0:
                    _cicdTab.OnGUI();
                    break;
                case 1:
                    _buildTab.OnGUI();
                    break;
                case 2:
                    _essentialsTab.OnGUI();
                    break;
            }

            EditorGUILayout.EndScrollView();
        }

        void LoadCurrentVersion()
        {
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(GetType().Assembly);
            _currentVersion = info?.version;
        }

        void CheckForSelfUpdate()
        {
            _updateChecked = false;
            _remoteVersion = null;

            var request = UnityWebRequest.Get(RemotePackageJsonUrl);
            var op = request.SendWebRequest();
            op.completed += _ =>
            {
                if (request.result == UnityWebRequest.Result.Success)
                {
                    var m = Regex.Match(request.downloadHandler.text, @"""version""\s*:\s*""([^""]+)""");
                    if (m.Success) _remoteVersion = m.Groups[1].Value;
                }
                request.Dispose();
                _updateChecked = true;
                Repaint();
            };
        }

        void UpdateSelf()
        {
            _isUpdating = true;
            _updateRequest = Client.Add(GitUrl);
            EditorApplication.update += PollUpdateRequest;
        }

        void PollUpdateRequest()
        {
            if (!_updateRequest.IsCompleted) return;
            EditorApplication.update -= PollUpdateRequest;

            if (_updateRequest.Status == StatusCode.Failure)
                Debug.LogError($"Failed to update DevOps Dashboard: {_updateRequest.Error.message}");
            else
                Debug.Log($"DevOps Dashboard updated to v{_remoteVersion}");

            _isUpdating = false;
            Repaint();
        }

        static bool IsNewer(string remote, string local)
        {
            if (string.IsNullOrEmpty(remote) || string.IsNullOrEmpty(local)) return false;
            try { return new System.Version(remote) > new System.Version(local); }
            catch { return remote != local; }
        }
    }
}
