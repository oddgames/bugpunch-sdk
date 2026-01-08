Deploy the UITest package - version bump, tag, and push.

## Steps

1. **Increment version in package.json**
   - Read current version from package.json
   - Increment patch version (e.g., 1.0.7 -> 1.0.8)
   - Update package.json with new version

2. **Update documentation**
   - Add new version entry to CHANGELOG.md with:
     - Fixed: bug fixes
     - Added: new features/methods
     - Changed: modifications to existing behavior
     - Removed: deprecated features
   - Update README.md if new methods were added to the API

3. **Commit and push changes**
   - Stage all pending changes including version bump and docs
   - Commit with message: "v{version} - {brief description of changes}"
   - Push to origin

4. **Create and push git tag**
   - Create annotated tag: `git tag -a v{version} -m "Release v{version}"`
   - Push tag to origin: `git push origin v{version}`

5. **Report deployment status**
   - Show the new package version
   - Show the git tag for referencing in manifests
   - Show example manifest entry:
     ```json
     "com.oddgames.uitest": "https://github.com/nickhudson4/tool_ui_automation.git?path=package#v{version}"
     ```
