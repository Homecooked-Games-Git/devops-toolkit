namespace HomecookedGames.DevOps.Editor
{
    public static class Templates
    {
        public static string BuildYml(string gameName) => $@"name: {gameName} Build

on:
  workflow_dispatch:
    inputs:
      buildTarget:
        description: ""Platform to build""
        required: true
        default: ""iOS""
        type: choice
        options:
          - Android
          - iOS
          - Both
      distribution:
        description: ""Distribution target""
        required: true
        default: ""None""
        type: choice
        options:
          - None
          - TestFlight
          - Firebase
      scriptDefines:
        description: ""Extra script defines (semicolon-separated, e.g. DEV_MODE;EXTRA_LOGGING)""
        required: false
        type: string
        default: """"

concurrency:
  group: ${{{{ github.workflow }}}}-${{{{ github.ref }}}}
  cancel-in-progress: true

jobs:
  build-ios:
    if: >-
      inputs.buildTarget == 'iOS' ||
      inputs.buildTarget == 'Both'
    uses: Homecooked-Games-Git/devops-toolkit/.github/workflows/unity-build.yml@main
    with:
      game_name: ""{gameName}""
      build_target: ""iOS""
      distribution: ${{{{ inputs.distribution }}}}
      script_defines: ${{{{ inputs.scriptDefines }}}}
    secrets: inherit

  build-android:
    if: >-
      inputs.buildTarget == 'Android' ||
      inputs.buildTarget == 'Both'
    uses: Homecooked-Games-Git/devops-toolkit/.github/workflows/unity-build.yml@main
    with:
      game_name: ""{gameName}""
      build_target: ""Android""
      distribution: ${{{{ inputs.distribution }}}}
      script_defines: ${{{{ inputs.scriptDefines }}}}
    secrets: inherit
";

        public static string Fastfile() => @"import_from_git(
  url: ""https://github.com/Homecooked-Games-Git/devops-toolkit.git"",
  branch: ENV[""FL_DEVOPS_TOOLKIT_REF""] || ""main"",
  path: ""fastlane/Fastfile""
)
";

        public static string Matchfile() => @"git_url(""https://github.com/oguztecimer/ios-certificates.git"")
storage_mode(""git"")
type(""appstore"")
";

        public static string Gemfile() => @"source ""https://rubygems.org""

gem ""fastlane""
gem ""cocoapods""
gem ""fastlane-plugin-firebase_app_distribution""
";
        public static string GitIgnore() => @"# =========================
# Unity Auto-Generated Folders
# =========================
/[Ll]ibrary/
/[Tt]emp/
/[Oo]bj/
/[Bb]uild/
/[Bb]uilds/
/[Ll]ogs/
/[Uu]ser[Ss]ettings/

# Memory captures and profiler logs
/[Mm]emoryCaptures/
*.apk
*.aab
*.unitypackage
*.app
*.ipa

# =========================
# User-Specific / Local Settings
# =========================
**/ProjectSettings/RiderScriptEditorPersistedState.asset
*.csproj
*.unityproj
*.sln
*.suo
*.tmp
*.user
*.userprefs
*.pidb
*.booproj
*.svd
*.pdb
*.mdb
*.opendb
*.VC.db

# =========================
# OS Generated
# =========================
.DS_Store
.DS_Store?
._*
.Spotlight-V100
.Trashes
ehthumbs.db
Thumbs.db
$RECYCLE.BIN/
Desktop.ini

# =========================
# IDE & Local Settings
# =========================
.idea/
/.vs/

# =========================
# Cross-Platform / System Artifacts
# =========================
[Nn]ul
[Nn]ul.ext

# --- Unity Builder Action ---
/[Aa]ssets/Editor/UnityBuilderAction/
/[Aa]ssets/Editor/UnityBuilderAction.meta

# --- Mobile/Platform Specific ---
/[Aa]ssets/Plugins/Android/mainTemplate.gradle
/[Aa]ssets/Plugins/Android/launcherTemplate.gradle
/[Aa]ssets/Plugins/Android/gradleTemplate.properties

# --- Fastlane Secrets ---
fastlane/api_key.json
fastlane/AuthKey*.p8
fastlane/report.xml
";
    }
}
