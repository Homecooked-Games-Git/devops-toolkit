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
    }
}
