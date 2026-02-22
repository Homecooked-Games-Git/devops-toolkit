using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace HomecookedGames.DevOps.Editor
{
    public class CICDTab
    {
        readonly StatusChecker _checker;
        readonly ProcessRunner _runner;

        string _gameName;
        Vector2 _cliScrollPos;
        bool _showCliOutput;

        public CICDTab(StatusChecker checker, ProcessRunner runner)
        {
            _checker = checker;
            _runner = runner;
        }

        public void OnEnable()
        {
            _checker.Refresh();
            _gameName = _checker.Project.ProductName?.Replace(" ", "") ?? "MyGame";
        }

        public void OnGUI()
        {
            DrawProjectInfo();
            EditorGUILayout.Space(10);
            DrawFirebaseSection();
            EditorGUILayout.Space(10);
            DrawBoilerplateSection();
            EditorGUILayout.Space(10);
            DrawFullSetupSection();

            if (_showCliOutput || _runner.IsRunning)
                DrawCliOutput();
        }

        void DrawProjectInfo()
        {
            EditorGUILayout.LabelField("Project Info", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            EditorGUILayout.LabelField("Product Name", _checker.Project.ProductName ?? "—");
            EditorGUILayout.LabelField("iOS Bundle ID", _checker.Project.IOSBundleId ?? "—");
            EditorGUILayout.LabelField("Android Bundle ID", _checker.Project.AndroidBundleId ?? "—");
            _gameName = EditorGUILayout.TextField("Game Name", _gameName);
            EditorGUI.indentLevel--;
        }

        void DrawFirebaseSection()
        {
            EditorGUILayout.LabelField("Firebase", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;

            // iOS config
            var ios = _checker.FirebaseIOS;
            DrawStatusLine(ios.Status, "iOS Config",
                ios.Status == ComponentStatus.Present ? $"project: {ios.ProjectId}" : null);

            // Android config
            var android = _checker.FirebaseAndroid;
            DrawStatusLine(android.Status, "Android Config",
                android.Status == ComponentStatus.Present ? $"project: {android.ProjectId}" : null);

            EditorGUI.indentLevel--;
            EditorGUILayout.Space(4);

            // Buttons
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(16);
            GUI.enabled = !_runner.IsRunning;

            if (GUILayout.Button("Create Firebase Project", GUILayout.Width(170)))
                CreateFirebaseProject();
            if (GUILayout.Button("Download Configs", GUILayout.Width(130)))
                DownloadFirebaseConfigs();
            if (GUILayout.Button("Open Console", GUILayout.Width(100)))
                OpenFirebaseConsole();

            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();
        }

        void DrawBoilerplateSection()
        {
            EditorGUILayout.LabelField("CI/CD Boilerplate", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;

            var wf = _checker.Workflow;
            DrawStatusLine(wf.Status, "build.yml",
                wf.Status == ComponentStatus.Present ? $"game_name: {wf.GameName}" : null);

            DrawStatusLine(_checker.Fastfile.Status, "Fastfile");

            var mf = _checker.Matchfile;
            DrawStatusLine(mf.Status, "Matchfile",
                mf.Status == ComponentStatus.Present ? $"repo: {ShortenUrl(mf.CertRepoUrl)}" : null);

            var gf = _checker.Gemfile;
            DrawStatusLine(gf.Status, "Gemfile",
                gf.Status == ComponentStatus.Present ? (gf.HasLockFile ? "(lock: present)" : "(lock: missing)") : null);

            EditorGUI.indentLevel--;
            EditorGUILayout.Space(4);

            // Buttons
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(16);
            GUI.enabled = !_runner.IsRunning;

            if (GUILayout.Button("Generate All", GUILayout.Width(100)))
                GenerateBoilerplate();
            if (GUILayout.Button("Remove All", GUILayout.Width(100)))
                RemoveBoilerplate();

            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();
        }

        void DrawFullSetupSection()
        {
            EditorGUILayout.LabelField("Full Setup", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(16);
            GUI.enabled = !_runner.IsRunning;

            if (GUILayout.Button("Run setup.py", GUILayout.Width(100)))
                RunSetupPy();

            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();
        }

        void DrawCliOutput()
        {
            _showCliOutput = true;
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("CLI Output", EditorStyles.boldLabel);

            var output = _runner.Output;
            if (!string.IsNullOrEmpty(_runner.Error))
                output += "\n" + _runner.Error;

            _cliScrollPos = EditorGUILayout.BeginScrollView(_cliScrollPos, GUILayout.Height(150));
            EditorGUILayout.TextArea(output, EditorStyles.helpBox, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();

            if (!_runner.IsRunning)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Clear", GUILayout.Width(60)))
                {
                    _runner.ClearOutput();
                    _showCliOutput = false;
                }
                EditorGUILayout.EndHorizontal();
            }
        }

        // ── Actions ──

        void CreateFirebaseProject()
        {
            var projectId = _gameName.ToLower().Replace(" ", "-");
            var iosBundleId = _checker.Project.IOSBundleId;
            var androidBundleId = _checker.Project.AndroidBundleId;

            _runner.ClearOutput();
            _runner.Run(ProcessRunner.FirebaseCommand,
                $"projects:create \"{projectId}\" --display-name \"{_gameName}\"",
                _checker.ProjectRoot, () =>
                {
                    if (!string.IsNullOrEmpty(iosBundleId))
                        _runner.Run(ProcessRunner.FirebaseCommand,
                            $"apps:create ios --bundle-id \"{iosBundleId}\" --project \"{projectId}\"",
                            _checker.ProjectRoot, () =>
                            {
                                if (!string.IsNullOrEmpty(androidBundleId))
                                    _runner.Run(ProcessRunner.FirebaseCommand,
                                        $"apps:create android --package-name \"{androidBundleId}\" --project \"{projectId}\"",
                                        _checker.ProjectRoot, () => _checker.Refresh());
                                else
                                    _checker.Refresh();
                            });
                    else
                        _checker.Refresh();
                });
        }

        void DownloadFirebaseConfigs()
        {
            var projectId = GetFirebaseProjectId();
            if (string.IsNullOrEmpty(projectId))
            {
                projectId = _gameName.ToLower().Replace(" ", "-");
            }

            var settingsDir = Path.Combine(_checker.ProjectRoot, "Assets", "Settings");
            Directory.CreateDirectory(settingsDir);

            _runner.ClearOutput();
            _runner.Run(ProcessRunner.FirebaseCommand,
                $"apps:sdkconfig ios --project \"{projectId}\" --out \"{Path.Combine(settingsDir, "GoogleService-Info.plist")}\"",
                _checker.ProjectRoot, () =>
                {
                    _runner.Run(ProcessRunner.FirebaseCommand,
                        $"apps:sdkconfig android --project \"{projectId}\" --out \"{Path.Combine(settingsDir, "google-services.json")}\"",
                        _checker.ProjectRoot, () =>
                        {
                            AssetDatabase.Refresh();
                            _checker.Refresh();
                        });
                });
        }

        void OpenFirebaseConsole()
        {
            var projectId = GetFirebaseProjectId();
            if (string.IsNullOrEmpty(projectId))
                projectId = _gameName.ToLower().Replace(" ", "-");
            Application.OpenURL($"https://console.firebase.google.com/project/{projectId}");
        }

        void GenerateBoilerplate()
        {
            var root = _checker.ProjectRoot;

            WriteIfMissing(Path.Combine(root, ".github", "workflows", "build.yml"), Templates.BuildYml(_gameName));
            WriteIfMissing(Path.Combine(root, "fastlane", "Fastfile"), Templates.Fastfile());
            WriteIfMissing(Path.Combine(root, "fastlane", "Matchfile"), Templates.Matchfile());
            WriteIfMissing(Path.Combine(root, "Gemfile"), Templates.Gemfile());

            _checker.Refresh();
            Debug.Log("CI/CD boilerplate generated.");
        }

        void RemoveBoilerplate()
        {
            if (!EditorUtility.DisplayDialog("Remove CI/CD Boilerplate",
                    "This will delete build.yml, Fastfile, Matchfile, and Gemfile. Continue?",
                    "Delete", "Cancel"))
                return;

            var root = _checker.ProjectRoot;
            DeleteIfExists(Path.Combine(root, ".github", "workflows", "build.yml"));
            DeleteIfExists(Path.Combine(root, "fastlane", "Fastfile"));
            DeleteIfExists(Path.Combine(root, "fastlane", "Matchfile"));
            DeleteIfExists(Path.Combine(root, "Gemfile"));
            DeleteIfExists(Path.Combine(root, "Gemfile.lock"));

            _checker.Refresh();
            Debug.Log("CI/CD boilerplate removed.");
        }

        void RunSetupPy()
        {
            // Look for setup.py relative to this package's location or use a known path
            var setupPyPath = FindSetupPy();
            if (setupPyPath == null)
            {
                Debug.LogError("setup.py not found. Ensure devops-toolkit is accessible.");
                return;
            }

            _runner.ClearOutput();
            _runner.Run(ProcessRunner.PythonCommand,
                $"\"{setupPyPath}\" \"{_gameName}\"",
                _checker.ProjectRoot, () =>
                {
                    AssetDatabase.Refresh();
                    _checker.Refresh();
                });
        }

        // ── Helpers ──

        string GetFirebaseProjectId()
        {
            if (_checker.FirebaseIOS.Status == ComponentStatus.Present)
                return _checker.FirebaseIOS.ProjectId;
            if (_checker.FirebaseAndroid.Status == ComponentStatus.Present)
                return _checker.FirebaseAndroid.ProjectId;
            return null;
        }

        static string FindSetupPy()
        {
            // Check known locations
            var candidates = new[]
            {
                // Sibling to the Unity project
                Path.Combine(Application.dataPath, "..", "..", "devops-toolkit", "setup.py"),
                // In the package cache (when installed via git)
                Path.Combine(GetPackagePath(), "..", "setup.py"),
            };

            foreach (var path in candidates)
            {
                var full = Path.GetFullPath(path);
                if (File.Exists(full)) return full;
            }

            return null;
        }

        static string GetPackagePath()
        {
            // Find the package path via the asmdef
            var guids = AssetDatabase.FindAssets("HomecookedGames.DevOps.Editor t:asmdef");
            if (guids.Length > 0)
                return Path.GetDirectoryName(AssetDatabase.GUIDToAssetPath(guids[0]));
            return Application.dataPath;
        }

        static void WriteIfMissing(string path, string content)
        {
            if (File.Exists(path)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        static void DeleteIfExists(string path)
        {
            if (File.Exists(path)) File.Delete(path);
        }

        static string ShortenUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return "—";
            return url.Replace("https://github.com/", "");
        }

        static void DrawStatusLine(ComponentStatus status, string label, string detail = null)
        {
            EditorGUILayout.BeginHorizontal();

            var icon = status == ComponentStatus.Present
                ? EditorGUIUtility.IconContent("TestPassed")
                : EditorGUIUtility.IconContent("TestFailed");
            GUILayout.Label(icon, GUILayout.Width(20), GUILayout.Height(20));
            GUILayout.Label(label, GUILayout.Width(100));

            if (!string.IsNullOrEmpty(detail))
                GUILayout.Label(detail, EditorStyles.miniLabel);

            EditorGUILayout.EndHorizontal();
        }
    }
}
