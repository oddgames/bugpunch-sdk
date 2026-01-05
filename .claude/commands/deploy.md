Deploy the UITest package - version bump and push.

## Steps

1. **Increment version in package.json**
   - Read current version from package.json
   - Increment patch version (e.g., 1.0.7 -> 1.0.8)
   - Update package.json with new version

2. **Commit and push changes**
   - Stage all pending changes including version bump
   - Commit with message: "v{version} - {brief description of changes}"
   - Push to origin
   - Get the full commit hash

3. **Report deployment status**
   - Show the new package version
   - Show the full commit hash for updating project manifests
