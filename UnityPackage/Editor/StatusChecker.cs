using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;

namespace HomecookedGames.DevOps.Editor
{
    public enum ComponentStatus { Missing, Present }

    public struct FirebaseIOSInfo
    {
        public ComponentStatus Status;
        public string ProjectId;
        public string BundleId;
    }

    public struct FirebaseAndroidInfo
    {
        public ComponentStatus Status;
        public string ProjectId;
        public string PackageName;
    }

    public struct WorkflowInfo
    {
        public ComponentStatus Status;
        public string GameName;
    }

    public struct FastfileInfo
    {
        public ComponentStatus Status;
    }

    public struct MatchfileInfo
    {
        public ComponentStatus Status;
        public string CertRepoUrl;
    }

    public struct GemfileInfo
    {
        public ComponentStatus Status;
        public bool HasLockFile;
    }

    public struct ProjectInfo
    {
        public string ProductName;
        public string CompanyName;
        public string IOSBundleId;
        public string AndroidBundleId;
    }

    public class StatusChecker
    {
        public string ProjectRoot { get; }

        public ProjectInfo Project { get; private set; }
        public FirebaseIOSInfo FirebaseIOS { get; private set; }
        public FirebaseAndroidInfo FirebaseAndroid { get; private set; }
        public WorkflowInfo Workflow { get; private set; }
        public FastfileInfo Fastfile { get; private set; }
        public MatchfileInfo Matchfile { get; private set; }
        public GemfileInfo Gemfile { get; private set; }
        public ComponentStatus GitIgnore { get; private set; }

        public StatusChecker()
        {
            ProjectRoot = Path.GetDirectoryName(UnityEngine.Application.dataPath);
        }

        public void Refresh()
        {
            RefreshProjectInfo();
            RefreshFirebaseIOS();
            RefreshFirebaseAndroid();
            RefreshWorkflow();
            RefreshFastfile();
            RefreshMatchfile();
            RefreshGemfile();
            RefreshGitIgnore();
        }

        void RefreshProjectInfo()
        {
            Project = new ProjectInfo
            {
                ProductName = PlayerSettings.productName,
                CompanyName = PlayerSettings.companyName,
                IOSBundleId = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.iOS),
                AndroidBundleId = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android)
            };
        }

        void RefreshFirebaseIOS()
        {
            var path = Path.Combine(ProjectRoot, "Assets", "Settings", "GoogleService-Info.plist");
            if (!File.Exists(path))
            {
                FirebaseIOS = new FirebaseIOSInfo { Status = ComponentStatus.Missing };
                return;
            }

            var content = File.ReadAllText(path);
            FirebaseIOS = new FirebaseIOSInfo
            {
                Status = ComponentStatus.Present,
                ProjectId = ExtractPlistValue(content, "PROJECT_ID"),
                BundleId = ExtractPlistValue(content, "BUNDLE_ID")
            };
        }

        void RefreshFirebaseAndroid()
        {
            var path = Path.Combine(ProjectRoot, "Assets", "Settings", "google-services.json");
            if (!File.Exists(path))
            {
                FirebaseAndroid = new FirebaseAndroidInfo { Status = ComponentStatus.Missing };
                return;
            }

            var content = File.ReadAllText(path);
            FirebaseAndroid = new FirebaseAndroidInfo
            {
                Status = ComponentStatus.Present,
                ProjectId = ExtractJsonValue(content, "project_id"),
                PackageName = ExtractJsonValue(content, "package_name")
            };
        }

        void RefreshWorkflow()
        {
            var path = Path.Combine(ProjectRoot, ".github", "workflows", "build.yml");
            if (!File.Exists(path))
            {
                Workflow = new WorkflowInfo { Status = ComponentStatus.Missing };
                return;
            }

            var content = File.ReadAllText(path);
            var m = Regex.Match(content, @"game_name:\s*""([^""]+)""");
            Workflow = new WorkflowInfo
            {
                Status = ComponentStatus.Present,
                GameName = m.Success ? m.Groups[1].Value : null
            };
        }

        void RefreshFastfile()
        {
            var path = Path.Combine(ProjectRoot, "fastlane", "Fastfile");
            Fastfile = new FastfileInfo
            {
                Status = File.Exists(path) ? ComponentStatus.Present : ComponentStatus.Missing
            };
        }

        void RefreshMatchfile()
        {
            var path = Path.Combine(ProjectRoot, "fastlane", "Matchfile");
            if (!File.Exists(path))
            {
                Matchfile = new MatchfileInfo { Status = ComponentStatus.Missing };
                return;
            }

            var content = File.ReadAllText(path);
            var m = Regex.Match(content, @"git_url\(""([^""]+)""\)");
            Matchfile = new MatchfileInfo
            {
                Status = ComponentStatus.Present,
                CertRepoUrl = m.Success ? m.Groups[1].Value : null
            };
        }

        void RefreshGemfile()
        {
            var gemfilePath = Path.Combine(ProjectRoot, "Gemfile");
            var lockPath = Path.Combine(ProjectRoot, "Gemfile.lock");
            Gemfile = new GemfileInfo
            {
                Status = File.Exists(gemfilePath) ? ComponentStatus.Present : ComponentStatus.Missing,
                HasLockFile = File.Exists(lockPath)
            };
        }

        void RefreshGitIgnore()
        {
            var path = Path.Combine(ProjectRoot, ".gitignore");
            GitIgnore = File.Exists(path) ? ComponentStatus.Present : ComponentStatus.Missing;
        }

        static string ExtractPlistValue(string content, string key)
        {
            var m = Regex.Match(content, $@"<key>{Regex.Escape(key)}</key>\s*<string>([^<]+)</string>");
            return m.Success ? m.Groups[1].Value : null;
        }

        static string ExtractJsonValue(string content, string key)
        {
            var m = Regex.Match(content, $@"""{Regex.Escape(key)}""\s*:\s*""([^""]+)""");
            return m.Success ? m.Groups[1].Value : null;
        }
    }
}
