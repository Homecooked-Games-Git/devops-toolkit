using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace HomecookedGames.DevOps.Editor
{
    public class BuildTab
    {
        struct RunInfo
        {
            public long Id;
            public string Title;
            public string Status;
            public string Conclusion;
            public string Branch;
            public string CreatedAt;
        }

        static readonly string[] BuildTargets = { "iOS", "Android", "Both" };
        static readonly string[] Distributions = { "None", "TestFlight", "Firebase" };

        readonly StatusChecker _checker;
        readonly ProcessRunner _runner;
        readonly ProcessRunner _bgRunner = new();

        string _repoSlug = "";
        string _branch = "main";
        int _buildTarget;
        int _distribution;
        string _scriptDefines = "";
        bool _cleanBuild;
        bool _repoDetected;

        readonly List<RunInfo> _runs = new();
        bool _fetchingRuns;

        Action _repaintCallback;

        public BuildTab(StatusChecker checker, ProcessRunner runner)
        {
            _checker = checker;
            _runner = runner;
        }

        public void SetRepaintCallback(Action repaint)
        {
            _repaintCallback = repaint;
            _bgRunner.SetRepaintCallback(repaint);
        }

        public void OnEnable()
        {
            if (_repoDetected) return;
            DetectRepo();
        }

        public void OnGUI()
        {
            DrawBuildParams();
            EditorGUILayout.Space(12);
            DrawRecentRuns();
        }

        void DrawBuildParams()
        {
            DrawSectionHeader("Build");
            EditorGUI.indentLevel++;

            // Repo with loading indicator
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("Repo");
            if (string.IsNullOrEmpty(_repoSlug))
            {
                var spinner = EditorGUIUtility.IconContent("Loading");
                GUILayout.Label(spinner, GUILayout.Width(20), GUILayout.Height(18));
                GUILayout.Label("detecting...", EditorStyles.miniLabel);
            }
            else
            {
                EditorGUILayout.SelectableLabel(_repoSlug, EditorStyles.label, GUILayout.Height(18));
            }
            EditorGUILayout.EndHorizontal();

            _branch = EditorGUILayout.TextField("Branch", _branch);

            EditorGUILayout.Space(4);
            _buildTarget = EditorGUILayout.Popup("Platform", _buildTarget, BuildTargets);
            _distribution = EditorGUILayout.Popup("Distribution", _distribution, Distributions);
            _scriptDefines = EditorGUILayout.TextField("Script Defines", _scriptDefines);
            _cleanBuild = EditorGUILayout.Toggle("Clean Build", _cleanBuild);

            EditorGUI.indentLevel--;
            EditorGUILayout.Space(4);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(16);
            GUI.enabled = !_runner.IsRunning && !string.IsNullOrEmpty(_repoSlug);
            var btnContent = string.IsNullOrEmpty(_repoSlug)
                ? new GUIContent("Start Build", "Waiting for repo detection")
                : new GUIContent("Start Build");
            if (GUILayout.Button(btnContent, GUILayout.Width(100)))
                StartBuild();
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();
        }

        void DrawRecentRuns()
        {
            EditorGUILayout.BeginHorizontal();
            DrawSectionHeader("Recent Runs");
            GUILayout.FlexibleSpace();
            GUI.enabled = !_fetchingRuns && !string.IsNullOrEmpty(_repoSlug);
            if (_fetchingRuns)
            {
                var spinner = EditorGUIUtility.IconContent("Loading");
                GUILayout.Label(spinner, GUILayout.Width(20), GUILayout.Height(20));
            }
            if (GUILayout.Button("Refresh", EditorStyles.miniButton, GUILayout.Width(60)))
                FetchRecentRuns();
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            if (_runs.Count == 0)
            {
                EditorGUI.indentLevel++;
                if (_fetchingRuns)
                    EditorGUILayout.LabelField("Loading runs...", EditorStyles.miniLabel);
                else if (string.IsNullOrEmpty(_repoSlug))
                    EditorGUILayout.LabelField("Detecting repository...", EditorStyles.miniLabel);
                else
                    EditorGUILayout.LabelField("No workflow runs found. Start a build to see runs here.", EditorStyles.miniLabel);
                EditorGUI.indentLevel--;
                return;
            }

            EditorGUI.indentLevel++;
            foreach (var run in _runs)
            {
                EditorGUILayout.BeginHorizontal();

                // Status icon
                var icon = run.Conclusion switch
                {
                    "success" => EditorGUIUtility.IconContent("TestPassed"),
                    "failure" => EditorGUIUtility.IconContent("TestFailed"),
                    _ => run.Status == "in_progress"
                        ? EditorGUIUtility.IconContent("Loading")
                        : EditorGUIUtility.IconContent("TestNormal")
                };
                GUILayout.Label(icon, GUILayout.Width(20), GUILayout.Height(20));

                // Title + branch + time
                GUILayout.Label(run.Title, GUILayout.MinWidth(120));
                GUILayout.Label(run.Branch, EditorStyles.miniLabel, GUILayout.Width(80));
                GUILayout.Label(FormatTime(run.CreatedAt), EditorStyles.miniLabel, GUILayout.Width(60));

                if (GUILayout.Button("View", EditorStyles.miniButton, GUILayout.Width(40)))
                    ViewRun(run.Id);

                EditorGUILayout.EndHorizontal();
            }
            EditorGUI.indentLevel--;
        }

        // ── Actions ──

        void DetectRepo()
        {
            _bgRunner.ClearOutput();
            _bgRunner.Run("git", "remote get-url origin", _checker.ProjectRoot, () =>
            {
                var output = _bgRunner.Output.Trim();
                _repoSlug = ParseRepoSlug(output);

                _bgRunner.ClearOutput();
                _bgRunner.Run("git", "branch --show-current", _checker.ProjectRoot, () =>
                {
                    // Output contains the command echo line and the branch name
                    var lines = _bgRunner.Output.Trim().Split('\n');
                    foreach (var line in lines)
                    {
                        var trimmed = line.Trim();
                        if (!string.IsNullOrEmpty(trimmed) && !trimmed.StartsWith(">"))
                        {
                            _branch = trimmed;
                            break;
                        }
                    }

                    _repoDetected = true;
                    _repaintCallback?.Invoke();
                    FetchRecentRuns();
                });
            });
        }

        void StartBuild()
        {
            var args = $"workflow run build.yml --repo \"{_repoSlug}\" --ref \"{_branch}\"" +
                       $" -f buildTarget={BuildTargets[_buildTarget]}" +
                       $" -f distribution={Distributions[_distribution]}" +
                       $" -f cleanBuild={(_cleanBuild ? "true" : "false")}";

            if (!string.IsNullOrEmpty(_scriptDefines))
                args += $" -f scriptDefines=\"{_scriptDefines}\"";

            _runner.ClearOutput();
            _runner.Run("gh", args, _checker.ProjectRoot, () =>
            {
                FetchRecentRuns();
            });
        }

        void FetchRecentRuns()
        {
            if (string.IsNullOrEmpty(_repoSlug)) return;

            _fetchingRuns = true;
            _bgRunner.ClearOutput();
            _bgRunner.Run("gh",
                $"run list --workflow=build.yml --repo \"{_repoSlug}\" --limit 5 --json databaseId,displayTitle,status,conclusion,createdAt,headBranch",
                _checker.ProjectRoot, () =>
                {
                    ParseRuns(_bgRunner.Output);
                    _fetchingRuns = false;
                    _repaintCallback?.Invoke();
                });
        }

        void ViewRun(long runId)
        {
            Application.OpenURL($"https://github.com/{_repoSlug}/actions/runs/{runId}");
        }

        // ── Parsing ──

        static string ParseRepoSlug(string output)
        {
            // Output has the command echo line "> git remote ..." then the URL
            foreach (var line in output.Split('\n'))
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith(">"))
                    continue;

                // git@github.com:Owner/Repo.git
                var m = Regex.Match(trimmed, @"github\.com[:/](.+?)(?:\.git)?$");
                if (m.Success) return m.Groups[1].Value;
            }
            return "";
        }

        void ParseRuns(string output)
        {
            _runs.Clear();

            // Extract JSON array from output (skip "> gh run list ..." echo line)
            var json = "";
            foreach (var line in output.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("["))
                {
                    // Collect from here to end
                    json = output.Substring(output.IndexOf(trimmed));
                    break;
                }
            }

            if (string.IsNullOrEmpty(json)) return;

            // Minimal JSON parsing — extract objects from array
            // Each object has: databaseId, displayTitle, status, conclusion, createdAt, headBranch
            var matches = Regex.Matches(json, @"\{[^}]+\}");
            foreach (Match match in matches)
            {
                var obj = match.Value;
                _runs.Add(new RunInfo
                {
                    Id = ExtractLong(obj, "databaseId"),
                    Title = ExtractString(obj, "displayTitle"),
                    Status = ExtractString(obj, "status"),
                    Conclusion = ExtractString(obj, "conclusion"),
                    CreatedAt = ExtractString(obj, "createdAt"),
                    Branch = ExtractString(obj, "headBranch")
                });
            }
        }

        static string ExtractString(string json, string key)
        {
            var m = Regex.Match(json, $@"""{Regex.Escape(key)}""\s*:\s*""([^""]*)""");
            return m.Success ? m.Groups[1].Value : "";
        }

        static long ExtractLong(string json, string key)
        {
            var m = Regex.Match(json, $@"""{Regex.Escape(key)}""\s*:\s*(\d+)");
            return m.Success && long.TryParse(m.Groups[1].Value, out var v) ? v : 0;
        }

        static string FormatTime(string iso8601)
        {
            if (string.IsNullOrEmpty(iso8601)) return "";
            if (!DateTime.TryParse(iso8601, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
                return iso8601;

            var elapsed = DateTime.UtcNow - dt.ToUniversalTime();
            if (elapsed.TotalMinutes < 1) return "just now";
            if (elapsed.TotalMinutes < 60) return $"{(int)elapsed.TotalMinutes}m ago";
            if (elapsed.TotalHours < 24) return $"{(int)elapsed.TotalHours}h ago";
            return $"{(int)elapsed.TotalDays}d ago";
        }

        static void DrawSectionHeader(string title)
        {
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            var rect = GUILayoutUtility.GetRect(0, 1, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, new Color(0.3f, 0.3f, 0.3f, 0.5f));
            EditorGUILayout.Space(4);
        }
    }
}
