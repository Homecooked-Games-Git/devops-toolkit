#!/bin/bash
set -euo pipefail

if [ $# -lt 1 ]; then
  echo "Usage: $0 <GameName>"
  echo "Run from the root of your Unity project."
  exit 1
fi

GAME_NAME="$1"

echo "Setting up CI/CD for ${GAME_NAME}..."

# .github/workflows/build.yml
mkdir -p .github/workflows
cat > .github/workflows/build.yml << EOF
name: ${GAME_NAME} Build

on:
  workflow_dispatch:
    inputs:
      buildTarget:
        description: "Platform to build"
        required: true
        default: "iOS"
        type: choice
        options:
          - Android
          - iOS
          - Both
      distribution:
        description: "Distribution target"
        required: true
        default: "None"
        type: choice
        options:
          - None
          - TestFlight
          - Firebase
      scriptDefines:
        description: "Extra script defines (semicolon-separated, e.g. DEV_MODE;EXTRA_LOGGING)"
        required: false
        type: string
        default: ""

concurrency:
  group: \${{ github.workflow }}-\${{ github.ref }}
  cancel-in-progress: true

jobs:
  build-ios:
    if: >-
      inputs.buildTarget == 'iOS' ||
      inputs.buildTarget == 'Both'
    uses: Homecooked-Games-Git/devops-toolkit/.github/workflows/unity-build.yml@main
    with:
      game_name: "${GAME_NAME}"
      build_target: "iOS"
      distribution: \${{ inputs.distribution }}
      script_defines: \${{ inputs.scriptDefines }}
    secrets: inherit

  build-android:
    if: >-
      inputs.buildTarget == 'Android' ||
      inputs.buildTarget == 'Both'
    uses: Homecooked-Games-Git/devops-toolkit/.github/workflows/unity-build.yml@main
    with:
      game_name: "${GAME_NAME}"
      build_target: "Android"
      distribution: \${{ inputs.distribution }}
      script_defines: \${{ inputs.scriptDefines }}
    secrets: inherit
EOF

# fastlane/Fastfile
mkdir -p fastlane
cat > fastlane/Fastfile << 'EOF'
import_from_git(
  url: "https://github.com/Homecooked-Games-Git/devops-toolkit.git",
  branch: ENV["FL_DEVOPS_TOOLKIT_REF"] || "main",
  path: "fastlane/Fastfile"
)
EOF

# fastlane/Matchfile
cat > fastlane/Matchfile << 'EOF'
git_url("https://github.com/oguztecimer/luckyrpg-certificates.git")
storage_mode("git")
type("appstore")
EOF

# Gemfile
cat > Gemfile << 'EOF'
source "https://rubygems.org"

gem "fastlane"
gem "cocoapods"
gem "fastlane-plugin-firebase_app_distribution"
EOF

# Generate Gemfile.lock
if command -v bundle &> /dev/null; then
  echo "Generating Gemfile.lock..."
  bundle lock 2>/dev/null || echo "Warning: bundle lock failed. Run it manually."
fi

echo ""
echo "Done! Files created:"
echo "  .github/workflows/build.yml"
echo "  fastlane/Fastfile"
echo "  fastlane/Matchfile"
echo "  Gemfile"
echo ""
echo "Remaining manual steps:"
echo "  1. Ensure these GitHub secrets are set (org-level or repo-level):"
echo "     - UNITY_LICENSE"
echo "     - MATCH_PASSWORD, MATCH_KEYCHAIN_PASSWORD, MATCH_GIT_BASIC_AUTHORIZATION"
echo "     - APP_STORE_CONNECT_API_KEY_KEY_ID, APP_STORE_CONNECT_API_KEY_ISSUER_ID, APP_STORE_CONNECT_API_KEY_KEY"
echo "     - ANDROID_KEYSTORE_NAME, ANDROID_KEYSTORE_BASE64, ANDROID_KEYSTORE_PASS, ANDROID_KEYALIAS_NAME, ANDROID_KEYALIAS_PASS"
echo "     - FIREBASE_SERVICE_ACCOUNT_JSON"
echo "  2. Add Firebase config files to Assets/Settings/ (google-services.json + GoogleService-Info.plist)"
echo "  3. Add the Firebase service account to your Firebase project (if new project)"
