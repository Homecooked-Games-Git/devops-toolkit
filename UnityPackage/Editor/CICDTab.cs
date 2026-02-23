using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace HomecookedGames.DevOps.Editor
{
    public class CICDTab
    {
        enum StepStatus { Pending, InProgress, Done, Failed }

        readonly StatusChecker _checker;
        readonly ProcessRunner _runner;

        string _gameName;
        Vector2 _cliScrollPos;
        bool _cliFoldout;

        // Wizard step statuses
        StepStatus _step1Status; // Generate Boilerplate
        StepStatus _step2Status; // Commit & Push
        StepStatus _step3Status; // Firebase Setup (Remote)
        StepStatus _step4Status; // Pull Configs

        static readonly string[] StepDescriptions =
        {
            "Creates workflow files, Fastfile, Matchfile, Gemfile, and .gitignore",
            "Commits generated files and pushes to remote",
            "Triggers Firebase project creation via GitHub Actions",
            "Pulls Firebase config files committed by the workflow"
        };

        public CICDTab(StatusChecker checker, ProcessRunner runner)
        {
            _checker = checker;
            _runner = runner;
        }

        public void OnEnable()
        {
            _checker.Refresh();
            _gameName = _checker.Project.ProductName?.Replace(" ", "") ?? "MyGame";
            RefreshStepStatuses();
        }

        public void OnGUI()
        {
            DrawProjectInfo();
            EditorGUILayout.Space(12);
            DrawSetupWizard();
            EditorGUILayout.Space(12);
            DrawFirebaseSection();
            EditorGUILayout.Space(12);
            DrawBoilerplateSection();
            EditorGUILayout.Space(12);
            DrawCliOutput();
        }

        void RefreshStepStatuses()
        {
            // Step 1: Done if all boilerplate files exist
            bool allBoilerplate = _checker.Workflow.Status == ComponentStatus.Present
                && _checker.Fastfile.Status == ComponentStatus.Present
                && _checker.Matchfile.Status == ComponentStatus.Present
                && _checker.Gemfile.Status == ComponentStatus.Present
                && _checker.GitIgnore == ComponentStatus.Present
                && _checker.SetupYml == ComponentStatus.Present;
            if (allBoilerplate && _step1Status != StepStatus.InProgress)
                _step1Status = StepStatus.Done;

            // Step 2: Done if boilerplate files are tracked by git
            if (allBoilerplate && _checker.BoilerplateTracked
                && _step2Status != StepStatus.InProgress)
                _step2Status = StepStatus.Done;

            // Step 3: Done if Firebase configs exist (implies remote setup succeeded)
            bool hasFirebaseConfigs = _checker.FirebaseIOS.Status == ComponentStatus.Present
                && _checker.FirebaseAndroid.Status == ComponentStatus.Present;
            if (_step2Status == StepStatus.Done && hasFirebaseConfigs
                && _step3Status != StepStatus.InProgress)
                _step3Status = StepStatus.Done;

            // Step 4: Done if Firebase configs exist
            if (hasFirebaseConfigs && _step4Status != StepStatus.InProgress)
                _step4Status = StepStatus.Done;
        }

        // ── UI Sections ──

        void DrawProjectInfo()
        {
            DrawSectionHeader("Project Info");
            EditorGUI.indentLevel++;
            EditorGUILayout.LabelField("Product Name", _checker.Project.ProductName ?? "—");
            EditorGUILayout.LabelField("iOS Bundle ID", _checker.Project.IOSBundleId ?? "—");
            EditorGUILayout.LabelField("Android Bundle ID", _checker.Project.AndroidBundleId ?? "—");
            _gameName = EditorGUILayout.TextField("Game Name", _gameName);
            EditorGUI.indentLevel--;
        }

        void DrawSetupWizard()
        {
            DrawSectionHeader("Project Setup");

            var branch = _checker.CurrentBranch;
            if (!string.IsNullOrEmpty(branch) && branch != "main" && branch != "master")
            {
                EditorGUILayout.HelpBox(
                    $"Current branch is \"{branch}\". Must be on the default branch (main/master) for setup to work.",
                    MessageType.Warning);
                EditorGUILayout.Space(4);
            }

            EditorGUI.indentLevel++;

            // Step 1: Generate Boilerplate
            DrawWizardStep(0, "Generate Boilerplate", _step1Status,
                enabled: _step1Status != StepStatus.InProgress && _step1Status != StepStatus.Done,
                buttonLabel: "Generate",
                disabledReason: _step1Status == StepStatus.Done ? "Already generated" : null,
                onClick: WizardGenerate);

            // Step 2: Commit & Push
            DrawWizardStep(1, "Commit & Push", _step2Status,
                enabled: _step1Status == StepStatus.Done
                    && _step2Status != StepStatus.InProgress
                    && _step2Status != StepStatus.Done,
                buttonLabel: "Commit & Push",
                disabledReason: _step1Status != StepStatus.Done ? "Complete Step 1 first"
                    : _step2Status == StepStatus.Done ? "Already pushed" : null,
                onClick: WizardCommitPush);

            // Step 3: Firebase Setup (Remote)
            DrawWizardStep(2, "Firebase Setup (Remote)", _step3Status,
                enabled: _step2Status == StepStatus.Done
                    && _step3Status != StepStatus.InProgress
                    && _step3Status != StepStatus.Done,
                buttonLabel: "Run Setup",
                disabledReason: _step2Status != StepStatus.Done ? "Complete Step 2 first"
                    : _step3Status == StepStatus.Done ? "Already configured" : null,
                onClick: WizardFirebaseSetup);

            // Step 4: Pull Configs
            DrawWizardStep(3, "Pull Configs", _step4Status,
                enabled: _step3Status == StepStatus.Done
                    && _step4Status != StepStatus.InProgress
                    && _step4Status != StepStatus.Done,
                buttonLabel: "Pull",
                disabledReason: _step3Status != StepStatus.Done ? "Complete Step 3 first"
                    : _step4Status == StepStatus.Done ? "Configs present" : null,
                onClick: WizardPull);

            EditorGUI.indentLevel--;
        }

        void DrawWizardStep(int index, string label, StepStatus status, bool enabled, string buttonLabel, string disabledReason, Action onClick)
        {
            EditorGUILayout.BeginHorizontal();

            var icon = status switch
            {
                StepStatus.Done => EditorGUIUtility.IconContent("TestPassed"),
                StepStatus.Failed => EditorGUIUtility.IconContent("TestFailed"),
                StepStatus.InProgress => EditorGUIUtility.IconContent("Loading"),
                _ => EditorGUIUtility.IconContent("TestNormal")
            };
            GUILayout.Label(icon, GUILayout.Width(20), GUILayout.Height(20));

            EditorGUILayout.BeginVertical();
            GUILayout.Label($"Step {index + 1}: {label}");
            GUILayout.Label(StepDescriptions[index], EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();

            GUILayout.FlexibleSpace();

            GUI.enabled = enabled && !_runner.IsRunning;
            var buttonContent = !string.IsNullOrEmpty(disabledReason) && !enabled
                ? new GUIContent(buttonLabel, disabledReason)
                : new GUIContent(buttonLabel);
            if (GUILayout.Button(buttonContent, GUILayout.Width(100)))
                onClick();
            GUI.enabled = true;

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(2);
        }

        void DrawFirebaseSection()
        {
            DrawSectionHeader("Firebase");
            EditorGUI.indentLevel++;

            // iOS config
            var ios = _checker.FirebaseIOS;
            DrawStatusLine(ios.Status, "iOS Config",
                ios.Status == ComponentStatus.Present ? $"project: {ios.ProjectId}" : null);

            // Android config
            var android = _checker.FirebaseAndroid;
            DrawStatusLine(android.Status, "Android Config",
                android.Status == ComponentStatus.Present ? $"project: {android.ProjectId}" : null);

            // Service account
            var sa = _checker.ServiceAccount;
            EditorGUILayout.BeginHorizontal();
            {
                var icon = !sa.Checked
                    ? EditorGUIUtility.IconContent("TestNormal")
                    : sa.Status == ComponentStatus.Present
                        ? EditorGUIUtility.IconContent("TestPassed")
                        : EditorGUIUtility.IconContent("TestFailed");
                GUILayout.Label(icon, GUILayout.Width(20), GUILayout.Height(20));
                GUILayout.Label("Service Acct", GUILayout.Width(100));

                var detail = !sa.Checked ? "not checked" : sa.Status == ComponentStatus.Present ? "configured" : "not added";
                GUILayout.Label(detail, EditorStyles.miniLabel);

                GUILayout.FlexibleSpace();

                GUI.enabled = !_runner.IsRunning;
                if (GUILayout.Button("Check", GUILayout.Width(50)))
                    CheckServiceAccount();
                if (sa.Checked && sa.Status == ComponentStatus.Missing && !string.IsNullOrEmpty(sa.ProjectId))
                {
                    if (GUILayout.Button("Add", GUILayout.Width(40)))
                        AddServiceAccount(sa.ProjectId);
                }
                GUI.enabled = true;
            }
            EditorGUILayout.EndHorizontal();

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
            DrawSectionHeader("CI/CD Boilerplate");
            EditorGUI.indentLevel++;

            var wf = _checker.Workflow;
            DrawStatusLine(wf.Status, "build.yml",
                wf.Status == ComponentStatus.Present ? $"game_name: {wf.GameName}" : null);

            DrawStatusLine(_checker.SetupYml, "setup.yml");

            DrawStatusLine(_checker.Fastfile.Status, "Fastfile");

            var mf = _checker.Matchfile;
            DrawStatusLine(mf.Status, "Matchfile",
                mf.Status == ComponentStatus.Present ? $"repo: {ShortenUrl(mf.CertRepoUrl)}" : null);

            var gf = _checker.Gemfile;
            DrawStatusLine(gf.Status, "Gemfile",
                gf.Status == ComponentStatus.Present ? (gf.HasLockFile ? "(lock: present)" : "(lock: missing)") : null);

            DrawStatusLine(_checker.GitIgnore, ".gitignore");

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
            if (GUILayout.Button("Update .gitignore", GUILayout.Width(120)))
                UpdateGitIgnore();

            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();
        }

        void DrawCliOutput()
        {
            var hasOutput = !string.IsNullOrEmpty(_runner.Output) || !string.IsNullOrEmpty(_runner.Error);

            // Auto-open when running, keep user's choice otherwise
            if (_runner.IsRunning)
                _cliFoldout = true;

            if (!hasOutput && !_runner.IsRunning)
                return;

            // Header with foldout and optional spinner
            EditorGUILayout.BeginHorizontal();
            _cliFoldout = EditorGUILayout.Foldout(_cliFoldout, "CLI Output", true, EditorStyles.foldoutHeader);
            if (_runner.IsRunning)
            {
                var spinner = EditorGUIUtility.IconContent("Loading");
                GUILayout.Label(spinner, GUILayout.Width(20), GUILayout.Height(20));
            }
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Clear", EditorStyles.miniButton, GUILayout.Width(50)))
            {
                _runner.ClearOutput();
                _cliFoldout = false;
            }
            EditorGUILayout.EndHorizontal();

            if (!_cliFoldout)
                return;

            var output = _runner.Output;
            if (!string.IsNullOrEmpty(_runner.Error))
                output += "\n" + _runner.Error;

            // Auto-scroll to bottom
            _cliScrollPos = new Vector2(0, float.MaxValue);
            _cliScrollPos = EditorGUILayout.BeginScrollView(_cliScrollPos, GUILayout.Height(150));
            EditorGUILayout.TextArea(output, EditorStyles.helpBox, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        // ── Wizard Actions ──

        void WizardGenerate()
        {
            _step1Status = StepStatus.InProgress;
            GenerateBoilerplate();
            _step1Status = StepStatus.Done;
        }

        void WizardCommitPush()
        {
            _step2Status = StepStatus.InProgress;
            _runner.ClearOutput();

            var scriptPath = Path.Combine(Path.GetTempPath(), "devops_commit_push.sh");
            File.WriteAllText(scriptPath,
                "#!/bin/bash\nset -e\n" +
                "git add .github/workflows/build.yml .github/workflows/setup.yml " +
                "fastlane/Fastfile fastlane/Matchfile Gemfile Gemfile.lock .gitignore\n" +
                "git commit -m \"Add CI/CD boilerplate\"\n" +
                "git push\n");

            _runner.Run("/bin/bash", scriptPath,
                _checker.ProjectRoot, () =>
                {
                    if (string.IsNullOrEmpty(_runner.Error) || !_runner.Error.Contains("fatal"))
                        _step2Status = StepStatus.Done;
                    else
                        _step2Status = StepStatus.Failed;
                });
        }

        void WizardFirebaseSetup()
        {
            _step3Status = StepStatus.InProgress;
            _runner.ClearOutput();

            var scriptPath = Path.Combine(Path.GetTempPath(), "devops_firebase_setup.sh");
            File.WriteAllText(scriptPath, @"#!/bin/bash
set -e

# Wait for GitHub to recognise the workflow (can take up to ~60s after push)
echo ""Waiting for GitHub to detect setup.yml workflow...""
for i in $(seq 1 12); do
  if gh workflow view setup.yml &>/dev/null; then
    echo ""Workflow found.""
    break
  fi
  if [ $i -eq 12 ]; then
    echo ""ERROR: setup.yml not found on the default branch after 60s.""
    echo ""Make sure Step 2 (Commit & Push) completed and setup.yml is on the default branch.""
    exit 1
  fi
  sleep 5
done

gh workflow run setup.yml
sleep 5
RUN_ID=$(gh run list --workflow=setup.yml --limit=1 --json databaseId -q '.[0].databaseId')
echo ""Watching workflow run $RUN_ID...""
gh run watch $RUN_ID --exit-status
");

            _runner.Run("/bin/bash", scriptPath,
                _checker.ProjectRoot, () =>
                {
                    if (_runner.Output.Contains("completed") && !_runner.Output.Contains("failed"))
                        _step3Status = StepStatus.Done;
                    else
                        _step3Status = StepStatus.Failed;
                });
        }

        void WizardPull()
        {
            _step4Status = StepStatus.InProgress;
            _runner.ClearOutput();
            _runner.Run("git", "pull",
                _checker.ProjectRoot, () =>
                {
                    AssetDatabase.Refresh();
                    _checker.Refresh();
                    RefreshStepStatuses();

                    if (_checker.FirebaseIOS.Status == ComponentStatus.Present
                        && _checker.FirebaseAndroid.Status == ComponentStatus.Present)
                        _step4Status = StepStatus.Done;
                    else
                        _step4Status = StepStatus.Failed;
                });
        }

        // ── Firebase Actions ──

        static string ToFirebaseProjectId(string gameName) =>
            "hcg-" + gameName.ToLower().Replace(" ", "-");

        void CreateFirebaseProject()
        {
            var projectId = ToFirebaseProjectId(_gameName);
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
                projectId = ToFirebaseProjectId(_gameName);
            }

            var settingsDir = Path.Combine(_checker.ProjectRoot, "Assets", "Settings");
            Directory.CreateDirectory(settingsDir);

            // Delete existing configs so firebase CLI doesn't skip the download
            var iosConfig = Path.Combine(settingsDir, "GoogleService-Info.plist");
            var androidConfig = Path.Combine(settingsDir, "google-services.json");
            if (File.Exists(iosConfig)) File.Delete(iosConfig);
            if (File.Exists(androidConfig)) File.Delete(androidConfig);

            _runner.ClearOutput();
            _runner.Run(ProcessRunner.FirebaseCommand,
                $"apps:sdkconfig ios --project \"{projectId}\" --out \"{iosConfig}\"",
                _checker.ProjectRoot, () =>
                {
                    _runner.Run(ProcessRunner.FirebaseCommand,
                        $"apps:sdkconfig android --project \"{projectId}\" --out \"{androidConfig}\"",
                        _checker.ProjectRoot, () =>
                        {
                            AssetDatabase.Refresh();
                            _checker.Refresh();
                        });
                });
        }

        const string CIServiceAccount = "ci-distribution@hcgamesfirebase.iam.gserviceaccount.com";

        void EnsureGcloud(Action onReady)
        {
            if (File.Exists(ProcessRunner.ResolveCommand("gcloud")))
            {
                onReady();
                return;
            }

            _runner.ClearOutput();
            _runner.Run("brew", "install google-cloud-sdk",
                _checker.ProjectRoot, () =>
                {
                    if (File.Exists(ProcessRunner.ResolveCommand("gcloud")))
                        onReady();
                    else
                        Debug.LogError("Failed to install gcloud. Install manually: brew install google-cloud-sdk");
                });
        }

        void CheckServiceAccount()
        {
            var projectId = GetFirebaseProjectId();
            if (string.IsNullOrEmpty(projectId))
                projectId = ToFirebaseProjectId(_gameName);

            var pid = projectId;
            EnsureGcloud(() =>
            {
                _runner.Run("gcloud",
                    $"projects get-iam-policy {pid} --flatten=\"bindings[].members\" " +
                    $"--filter=\"bindings.members:serviceAccount:{CIServiceAccount}\" " +
                    $"--format=\"value(bindings.role)\"",
                    _checker.ProjectRoot, () =>
                    {
                        var output = _runner.Output;
                        _checker.ServiceAccount = new ServiceAccountInfo
                        {
                            Status = output.Contains("firebaseappdistro") ? ComponentStatus.Present : ComponentStatus.Missing,
                            ProjectId = pid,
                            Checked = true
                        };
                    });
            });
        }

        void AddServiceAccount(string projectId)
        {
            EnsureGcloud(() =>
            {
                _runner.Run("gcloud",
                    $"projects add-iam-policy-binding {projectId} " +
                    $"--member=\"serviceAccount:{CIServiceAccount}\" " +
                    $"--role=\"roles/firebaseappdistro.admin\" --quiet",
                    _checker.ProjectRoot, () =>
                    {
                        CheckServiceAccount();
                    });
            });
        }

        void OpenFirebaseConsole()
        {
            var projectId = GetFirebaseProjectId();
            if (string.IsNullOrEmpty(projectId))
                projectId = ToFirebaseProjectId(_gameName);
            Application.OpenURL($"https://console.firebase.google.com/project/{projectId}");
        }

        // ── Boilerplate Actions ──

        void GenerateBoilerplate()
        {
            var root = _checker.ProjectRoot;

            WriteIfMissing(Path.Combine(root, ".github", "workflows", "build.yml"), Templates.BuildYml(_gameName));
            WriteIfMissing(Path.Combine(root, ".github", "workflows", "setup.yml"),
                Templates.SetupYml(_gameName, _checker.Project.IOSBundleId, _checker.Project.AndroidBundleId));
            WriteIfMissing(Path.Combine(root, "fastlane", "Fastfile"), Templates.Fastfile());
            WriteIfMissing(Path.Combine(root, "fastlane", "Matchfile"), Templates.Matchfile());
            WriteIfMissing(Path.Combine(root, "Gemfile"), Templates.Gemfile());
            WriteIfMissing(Path.Combine(root, ".gitignore"), Templates.GitIgnore());

            // Copy Gemfile.lock template
            var lockTemplatePath = Path.Combine(GetPackagePath(), "Templates~", "Gemfile.lock");
            if (File.Exists(lockTemplatePath))
                WriteIfMissing(Path.Combine(root, "Gemfile.lock"), File.ReadAllText(lockTemplatePath));

            _checker.Refresh();
            RefreshStepStatuses();
            Debug.Log("CI/CD boilerplate generated.");
        }

        void UpdateGitIgnore()
        {
            var path = Path.Combine(_checker.ProjectRoot, ".gitignore");
            File.WriteAllText(path, Templates.GitIgnore());
            _checker.Refresh();
            Debug.Log(".gitignore updated.");
        }

        void RemoveBoilerplate()
        {
            if (!EditorUtility.DisplayDialog("Remove CI/CD Boilerplate",
                    "This will delete build.yml, setup.yml, Fastfile, Matchfile, and Gemfile. Continue?",
                    "Delete", "Cancel"))
                return;

            var root = _checker.ProjectRoot;
            DeleteIfExists(Path.Combine(root, ".github", "workflows", "build.yml"));
            DeleteIfExists(Path.Combine(root, ".github", "workflows", "setup.yml"));
            DeleteIfExists(Path.Combine(root, "fastlane", "Fastfile"));
            DeleteIfExists(Path.Combine(root, "fastlane", "Matchfile"));
            DeleteIfExists(Path.Combine(root, "Gemfile"));
            DeleteIfExists(Path.Combine(root, "Gemfile.lock"));

            _checker.Refresh();
            RefreshStepStatuses();
            Debug.Log("CI/CD boilerplate removed.");
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

        static void DrawSectionHeader(string title)
        {
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            var rect = GUILayoutUtility.GetRect(0, 1, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, new Color(0.3f, 0.3f, 0.3f, 0.5f));
            EditorGUILayout.Space(4);
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
