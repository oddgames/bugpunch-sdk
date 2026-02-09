Deploy the UITest package - version bump, tag, and push.

## Steps

1. **Update wiki (if wiki_temp exists)**
   - Check if `wiki_temp/` directory exists
   - Update existing wiki pages to document any new features added in this release
   - Update `_Sidebar.md` with any new pages
   - Update `Home.md` with any new pages in appropriate sections
   - Commit and push wiki changes: `cd wiki_temp && git add -A && git commit -m "..." && git push`

2. **Increment version in package.json**
   - Read current version from package.json
   - Increment patch version (e.g., 1.0.7 -> 1.0.8)
   - Update package.json with new version

3. **Update documentation**
   - Add new version entry to CHANGELOG.md with:
     - Fixed: bug fixes
     - Added: new features/methods
     - Changed: modifications to existing behavior
     - Removed: deprecated features
   - Update README.md if new methods were added to the API
   - Clear the "Current Local Changes" section in CLAUDE.md

4. **Commit and push changes**
   - **CRITICAL**: Run `git status` first to see ALL modified files
   - **CRITICAL**: Stage ALL modified files in `package/` directory - do NOT selectively commit
   - Use `git add package/ CLAUDE.md README.md` to ensure all package code is included
   - Verify with `git status` that all package changes are staged before committing
   - Commit with message: "v{version} - {brief description of changes}"
   - Push to origin

5. **Create and push git tag**
   - Create annotated tag: `git tag -a v{version} -m "Release v{version}"`
   - Push tag to origin: `git push origin v{version}`

6. **Report deployment status**
   - Show the new package version
   - Show the git tag for referencing in manifests
   - Show example manifest entry:
     ```json
     "au.com.oddgames.uiautomation": "https://github.com/oddgames/ui-automation.git?path=package#v{version}"
     ```
