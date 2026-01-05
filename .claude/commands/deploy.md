Deploy the UITest package - version bump, changelog, and push.

## Package Path

C:\Workspaces\ui-automation

## Steps

1. **Version and changelog update**
   - Read current version from package.json
   - Ask user what type of changes were made (Added, Changed, Fixed, Removed)
   - Ask user to describe the changes briefly
   - Increment version (patch by default, or as user specifies)
   - Update CHANGELOG.md with new version entry and today's date
   - Update package.json with new version

2. **Commit and push changes**
   - Stage all changes including version bump
   - Commit with message like "v1.0.X - brief description"
   - Push to origin
   - Get and display the latest commit hash

3. **Report deployment status**
   - Show package version from package.json
   - Show commit hash (user will use this to update project manifests manually)
