#!/usr/bin/env python3
"""
Cross-platform CI/CD bootstrapping script for Homecooked Games Unity projects.

Usage:
    python setup.py <GameName>

Run from the root of your Unity project.
"""

import json
import os
import platform
import re
import shutil
import subprocess
import sys
from pathlib import Path

CI_SERVICE_ACCOUNT = "ci-distribution@hcgamesfirebase.iam.gserviceaccount.com"


def run(cmd, capture=False, check=True):
    """Run a shell command, optionally capturing output."""
    if capture:
        result = subprocess.run(cmd, shell=True, capture_output=True, text=True)
        if check and result.returncode != 0:
            return None
        return result.stdout.strip()
    else:
        subprocess.run(cmd, shell=True, check=check)


def _ensure_path():
    """Ensure Homebrew paths take priority (newer tools over system installs)."""
    priority = ["/opt/homebrew/bin", "/opt/homebrew/sbin"]
    extra = ["/usr/local/bin", os.path.expanduser("~/.npm-global/bin")]
    current = os.environ.get("PATH", "")
    parts = current.split(os.pathsep)
    # Remove priority dirs from current position, then prepend them
    parts = [p for p in parts if p not in priority]
    for p in extra:
        if p not in parts:
            parts.append(p)
    os.environ["PATH"] = os.pathsep.join(priority + parts)

_ensure_path()


def command_exists(name):
    return shutil.which(name) is not None


def check_node_version():
    """Ensure Node.js >= 20 is available (required by Firebase CLI)."""
    node = shutil.which("node")
    if not node:
        return
    version_str = run("node --version", capture=True, check=False)
    if not version_str:
        return
    m = re.match(r"v(\d+)", version_str)
    if not m:
        return
    major = int(m.group(1))
    if major >= 20:
        return
    print(f"Node.js {version_str} is too old for Firebase CLI (need >= v20).")
    if command_exists("brew"):
        print("Upgrading Node.js via Homebrew...")
        run("brew upgrade node || brew install node", check=False)
        _ensure_path()
    else:
        print("Error: Please upgrade Node.js to >= v20.")
        sys.exit(1)


def check_prerequisites():
    """Ensure Firebase CLI is installed and user is logged in."""
    check_node_version()

    # Firebase CLI
    if not command_exists("firebase"):
        if command_exists("npm"):
            print("Installing Firebase CLI via npm...")
            run("npm install -g firebase-tools")
        elif platform.system() == "Darwin" and command_exists("brew"):
            print("Installing Firebase CLI via Homebrew...")
            run("brew install firebase-cli")
        else:
            print("Error: Install Firebase CLI manually:")
            print("  npm install -g firebase-tools")
            print("  https://firebase.google.com/docs/cli")
            sys.exit(1)

    # Firebase login check
    result = run("firebase projects:list", capture=True, check=False)
    if result is None:
        print("You need to log in to Firebase.")
        run("firebase login", check=False)


def read_bundle_ids():
    """Read iOS and Android bundle IDs from ProjectSettings.asset."""
    settings_path = Path("ProjectSettings") / "ProjectSettings.asset"
    if not settings_path.exists():
        print(f"Error: {settings_path} not found. Run this from a Unity project root.")
        sys.exit(1)

    content = settings_path.read_text(encoding="utf-8")

    ios_id = None
    android_id = None

    # Try regex first
    m = re.search(r"applicationIdentifier:.*?iPhone:\s*([\w.]+)", content, re.DOTALL)
    if m:
        ios_id = m.group(1).strip()

    m = re.search(r"applicationIdentifier:.*?Android:\s*([\w.]+)", content, re.DOTALL)
    if m:
        android_id = m.group(1).strip()

    if not ios_id or not android_id:
        print(f"Warning: Could not parse all bundle IDs from {settings_path}")
        print(f"  iOS: {ios_id or 'NOT FOUND'}")
        print(f"  Android: {android_id or 'NOT FOUND'}")

    return ios_id, android_id


def setup_firebase(game_name, project_id, ios_bundle_id, android_bundle_id):
    """Create Firebase project, register apps, download config files."""
    print(f"\nCreating Firebase project: {project_id}...")
    run(f'firebase projects:create "{project_id}" --display-name "{game_name}"', check=False)

    # Register iOS app
    if ios_bundle_id:
        print(f"Registering iOS app ({ios_bundle_id})...")
        run(
            f'firebase apps:create ios --bundle-id "{ios_bundle_id}" --project "{project_id}"',
            check=False,
        )

    # Register Android app
    if android_bundle_id:
        print(f"Registering Android app ({android_bundle_id})...")
        run(
            f'firebase apps:create android --package-name "{android_bundle_id}" --project "{project_id}"',
            check=False,
        )

    # Download config files
    settings_dir = Path("Assets") / "Settings"
    settings_dir.mkdir(parents=True, exist_ok=True)

    print("Downloading GoogleService-Info.plist...")
    run(
        f'firebase apps:sdkconfig ios --project "{project_id}" '
        f'--out "{settings_dir / "GoogleService-Info.plist"}"',
        check=False,
    )

    print("Downloading google-services.json...")
    run(
        f'firebase apps:sdkconfig android --project "{project_id}" '
        f'--out "{settings_dir / "google-services.json"}"',
        check=False,
    )

    # Add CI service account
    print("Adding CI service account to Firebase project...")
    if command_exists("gcloud"):
        run(
            f"gcloud projects add-iam-policy-binding {project_id} "
            f'--member="serviceAccount:{CI_SERVICE_ACCOUNT}" '
            f'--role="roles/firebaseappdistro.admin" --quiet',
            check=False,
        )
    else:
        print(
            f"Warning: gcloud not found. Add {CI_SERVICE_ACCOUNT} manually "
            f"in Firebase Console with App Distribution Admin role."
        )


def write_file(path, content):
    """Write a file, creating parent directories as needed."""
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(content, encoding="utf-8")
    print(f"  Created {path}")


def generate_boilerplate(game_name):
    """Generate all CI/CD boilerplate files."""
    # .github/workflows/build.yml
    build_yml = f"""name: {game_name} Build

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
  group: ${{{{ github.workflow }}}}-${{{{ github.ref }}}}
  cancel-in-progress: true

jobs:
  build-ios:
    if: >-
      inputs.buildTarget == 'iOS' ||
      inputs.buildTarget == 'Both'
    uses: Homecooked-Games-Git/devops-toolkit/.github/workflows/unity-build.yml@main
    with:
      game_name: "{game_name}"
      build_target: "iOS"
      distribution: ${{{{ inputs.distribution }}}}
      script_defines: ${{{{ inputs.scriptDefines }}}}
    secrets: inherit

  build-android:
    if: >-
      inputs.buildTarget == 'Android' ||
      inputs.buildTarget == 'Both'
    uses: Homecooked-Games-Git/devops-toolkit/.github/workflows/unity-build.yml@main
    with:
      game_name: "{game_name}"
      build_target: "Android"
      distribution: ${{{{ inputs.distribution }}}}
      script_defines: ${{{{ inputs.scriptDefines }}}}
    secrets: inherit
"""
    write_file(".github/workflows/build.yml", build_yml)

    # fastlane/Fastfile
    fastfile = """import_from_git(
  url: "https://github.com/Homecooked-Games-Git/devops-toolkit.git",
  branch: ENV["FL_DEVOPS_TOOLKIT_REF"] || "main",
  path: "fastlane/Fastfile"
)
"""
    write_file("fastlane/Fastfile", fastfile)

    # fastlane/Matchfile
    matchfile = """git_url("https://github.com/oguztecimer/ios-certificates.git")
storage_mode("git")
type("appstore")
"""
    write_file("fastlane/Matchfile", matchfile)

    # Gemfile
    gemfile = """source "https://rubygems.org"

gem "fastlane"
gem "cocoapods"
gem "fastlane-plugin-firebase_app_distribution"
"""
    write_file("Gemfile", gemfile)


def generate_gemfile_lock():
    """Generate Gemfile.lock if bundler is available."""
    if command_exists("bundle"):
        print("Generating Gemfile.lock...")
        run("bundle lock", check=False)
    else:
        print("Warning: bundler not found. Run 'bundle lock' manually to generate Gemfile.lock.")


def main():
    if len(sys.argv) < 2:
        print("Usage: python setup.py <GameName>")
        print("Run from the root of your Unity project.")
        sys.exit(1)

    game_name = sys.argv[1]
    project_id = "hcg-" + game_name.lower().replace(" ", "-")

    print(f"Setting up CI/CD for {game_name}...\n")

    # Prerequisites
    check_prerequisites()

    # Read bundle IDs
    ios_bundle_id, android_bundle_id = read_bundle_ids()
    print(f"iOS bundle ID: {ios_bundle_id}")
    print(f"Android bundle ID: {android_bundle_id}")

    # Firebase setup
    setup_firebase(game_name, project_id, ios_bundle_id, android_bundle_id)

    # Generate boilerplate
    print("\nGenerating CI/CD files...")
    generate_boilerplate(game_name)

    # Gemfile.lock
    generate_gemfile_lock()

    # Summary
    print(f"""
Done! Files created:
  .github/workflows/build.yml
  fastlane/Fastfile
  fastlane/Matchfile
  Gemfile
  Assets/Settings/GoogleService-Info.plist  (if Firebase succeeded)
  Assets/Settings/google-services.json      (if Firebase succeeded)

Remaining manual steps:
  1. Ensure these GitHub secrets are set (org-level or repo-level):
     - UNITY_LICENSE
     - MATCH_PASSWORD, MATCH_KEYCHAIN_PASSWORD, MATCH_GIT_BASIC_AUTHORIZATION
     - APP_STORE_CONNECT_API_KEY_KEY_ID, APP_STORE_CONNECT_API_KEY_ISSUER_ID, APP_STORE_CONNECT_API_KEY_KEY
     - ANDROID_KEYSTORE_NAME, ANDROID_KEYSTORE_BASE64, ANDROID_KEYSTORE_PASS, ANDROID_KEYALIAS_NAME, ANDROID_KEYALIAS_PASS
     - FIREBASE_SERVICE_ACCOUNT_JSON
  2. If Firebase config download failed, download manually from Firebase Console.
  3. If service account wasn't added, add {CI_SERVICE_ACCOUNT}
     with "Firebase App Distribution Admin" role in Google Cloud Console IAM.
""")


if __name__ == "__main__":
    main()
