// Pre-release gate for /deploy-sdk. Catches the bug families that have
// historically slipped through to consumers (URP/HDRP, runtime UnityEditor
// imports, stale AARs, drifted CHANGELOG/version, missing .meta files).
//
// Add new checks here rather than as separate scripts — extra processes
// add cold-start cost and another tool turn from the model.

const fs = require("fs");
const path = require("path");

const sdkRoot = path.resolve(__dirname, "..");                 // sdk/
const pkgRoot = path.join(sdkRoot, "package");                 // sdk/package
const androidSrc = path.join(sdkRoot, "android-src");          // sdk/android-src
const aarPath = path.join(pkgRoot, "Plugins/Android/BugpunchPlugin.aar");

const failures = [];
const fail = (check, msg) => failures.push({ check, msg });

// Files in the package that are runtime (shipped to consumers' players).
// Excludes Editor folders, Tests, Samples (Samples may include Editor too).
function isRuntimeCsFile(absPath) {
  const rel = path.relative(pkgRoot, absPath).replace(/\\/g, "/");
  if (!rel.endsWith(".cs")) return false;
  if (rel.split("/").includes("Editor")) return false;
  if (rel.split("/").includes("Tests")) return false;
  if (rel.startsWith("Samples/")) return false;
  return true;
}

function walkFiles(dir, onFile) {
  if (!fs.existsSync(dir)) return;
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, e.name);
    if (e.isDirectory()) walkFiles(full, onFile);
    else if (e.isFile()) onFile(full);
  }
}

// ---------------------------------------------------------------------------
// Render-pipeline anti-patterns. These break URP/HDRP consumers.
// ---------------------------------------------------------------------------
function checkRenderPipelineApis() {
  const banned = [
    { re: /\bSetReplacementShader\s*\(/, label: "Camera.SetReplacementShader (Built-in only)" },
    { re: /\bCamera\.onPreRender\b/,     label: "Camera.onPreRender (Built-in only)" },
    { re: /\bCamera\.onPostRender\b/,    label: "Camera.onPostRender (Built-in only)" },
    { re: /\bOnRenderImage\s*\(/,        label: "OnRenderImage (Built-in only)" },
    { re: /\bShaderUtil\.CreateShaderAsset\s*\(/, label: "ShaderUtil.CreateShaderAsset (Editor-only API in runtime)" },
  ];
  walkFiles(pkgRoot, file => {
    if (!isRuntimeCsFile(file)) return;
    const text = fs.readFileSync(file, "utf8");
    // Opt-out marker for files that legitimately straddle pipelines (e.g. hybrid
    // hooks that subscribe to both RenderPipelineManager.* and Camera.onPreRender).
    if (/preflight-allow:\s*render-pipeline/.test(text)) return;
    const stripped = text.replace(/\/\/[^\n]*|\/\*[\s\S]*?\*\//g, "");
    for (const b of banned) {
      if (b.re.test(stripped)) {
        fail("render-pipeline", `${path.relative(pkgRoot, file)}: ${b.label}`);
      }
    }
  });
}

// ---------------------------------------------------------------------------
// UnityEditor leak into runtime. Runtime files must guard UnityEditor imports
// with #if UNITY_EDITOR (or live in an Editor/ folder).
// ---------------------------------------------------------------------------
function checkUnityEditorLeak() {
  walkFiles(pkgRoot, file => {
    if (!isRuntimeCsFile(file)) return;
    const text = fs.readFileSync(file, "utf8");
    if (!/^\s*using\s+UnityEditor\b/m.test(text)) return;

    // Walk line-by-line tracking #if UNITY_EDITOR guards.
    let depth = 0;
    let guardedDepth = 0;
    for (const line of text.split(/\r?\n/)) {
      const trimmed = line.trim();
      if (/^#if\s+UNITY_EDITOR\b/.test(trimmed)) { depth++; guardedDepth = depth; continue; }
      if (/^#if\b/.test(trimmed)) { depth++; continue; }
      if (/^#endif\b/.test(trimmed)) { if (depth === guardedDepth) guardedDepth = 0; depth--; continue; }
      if (/^using\s+UnityEditor\b/.test(trimmed) && guardedDepth === 0) {
        fail("unity-editor-leak",
          `${path.relative(pkgRoot, file)}: \`using UnityEditor\` in runtime file — wrap in #if UNITY_EDITOR or move to Editor/`);
        return;
      }
    }
  });
}

// ---------------------------------------------------------------------------
// SDK auth header — Android Java + iOS Obj-C++ must send the API key in
// `X-Api-Key`, never `Authorization: Bearer …`. The server treats `Bearer`
// as a JWT and rejects with 401 (this bit the v1.8.0 native chat board).
// SDKs never carry a JWT, so any `Authorization: Bearer` in shipping code
// is a misuse — flag every site.
// ---------------------------------------------------------------------------
function checkSdkAuthHeader() {
  const targets = [
    { dir: androidSrc,                                rel: "android-src" },
    { dir: path.join(pkgRoot, "Plugins/iOS"),         rel: "package/Plugins/iOS" },
  ];
  // Match either Java (`"Authorization", "Bearer "`) or Obj-C
  // (`@"Bearer "` … `@"Authorization"`) literal pairs. We don't try to
  // decide whether the value is a key vs a JWT — the SDK has no JWTs, so
  // every match is a bug.
  const javaRe = /"Authorization"\s*,\s*"Bearer\s/;
  const objcRe = /@"Bearer\s[^"]*"[^;]*@"Authorization"|@"Authorization"[^;]*@"Bearer\s/;
  for (const t of targets) {
    if (!fs.existsSync(t.dir)) continue;
    walkFiles(t.dir, file => {
      if (!/\.(java|mm|m)$/.test(file)) return;
      const text = fs.readFileSync(file, "utf8");
      // Strip comments so the docstring on a fixed file doesn't trip us.
      // Line comments first, otherwise a `/*` substring inside a `//` URL
      // comment (e.g. `/api/v1/chat/*`) gets picked up as a block-comment
      // opener and eats the file.
      const stripped = text
        .replace(/\/\/[^\n]*/g, "")
        .replace(/\/\*[\s\S]*?\*\//g, "");
      const lines = stripped.split(/\r?\n/);
      lines.forEach((line, i) => {
        if (javaRe.test(line) || objcRe.test(line)) {
          fail("sdk-auth-header",
            `${t.rel}/${path.relative(t.dir, file).replace(/\\/g, "/")}:${i + 1}: ` +
            `\`Authorization: Bearer\` — SDK API keys must use \`X-Api-Key\` (Bearer is JWT-only)`);
        }
      });
    });
  }
}

// ---------------------------------------------------------------------------
// AAR freshness — if anything under android-src/ is newer than the shipped
// AAR, the build wasn't run.
// ---------------------------------------------------------------------------
function checkAarFreshness() {
  if (!fs.existsSync(aarPath)) {
    fail("aar", `${path.relative(sdkRoot, aarPath)} is missing — run \`pwsh ./build-android.ps1\``);
    return;
  }
  if (!fs.existsSync(androidSrc)) return;
  const aarMtime = fs.statSync(aarPath).mtimeMs;

  let newest = 0, newestFile = "";
  walkFiles(androidSrc, file => {
    // Ignore build artefacts inside android-src.
    const rel = path.relative(androidSrc, file).replace(/\\/g, "/");
    if (rel.includes("/build/") || rel.startsWith("build/") || rel.includes("/.gradle/")) return;
    const m = fs.statSync(file).mtimeMs;
    if (m > newest) { newest = m; newestFile = file; }
  });

  if (newest > aarMtime + 1000) {
    fail("aar",
      `AAR is older than ${path.relative(sdkRoot, newestFile)} — run \`pwsh ./build-android.ps1\` (or ask David to run the VS Code task)`);
  }
}

// ---------------------------------------------------------------------------
// CHANGELOG ↔ package.json version match. The topmost numbered heading
// (skipping [Unreleased]) must equal package.json version.
// ---------------------------------------------------------------------------
function checkChangelogVersionMatch() {
  const pkg = JSON.parse(fs.readFileSync(path.join(pkgRoot, "package.json"), "utf8"));
  const changelog = fs.readFileSync(path.join(pkgRoot, "CHANGELOG.md"), "utf8");

  let topVersion = null;
  for (const line of changelog.split(/\r?\n/)) {
    const m = line.match(/^##\s+\[([^\]]+)\]/);
    if (!m) continue;
    if (m[1].toLowerCase() === "unreleased") continue;
    topVersion = m[1];
    break;
  }
  if (!topVersion) {
    fail("changelog", `CHANGELOG.md has no versioned section — add \`## [${pkg.version}] - YYYY-MM-DD\``);
    return;
  }
  if (topVersion !== pkg.version) {
    fail("changelog",
      `package.json version is ${pkg.version} but CHANGELOG top entry is [${topVersion}] — they must match`);
  }
}

// ---------------------------------------------------------------------------
// Three-lane mirror. The SDK ships across three platform lanes (Java + NDK,
// Obj-C++, C# Editor + Standalone — see sdk/package/BugpunchPlatform.cs).
// Every cross-lane class is supposed to exist with the same name on each
// lane it ships on, so a feature owner can grep one identifier across all
// three. The manifest below pins the expected files; missing/renamed mirrors
// are the failure mode that hid `BugpunchRuntime.mm` for months.
//
// Each entry: { name, java?, ios?, cs? } — set the platform key to the
// expected file (relative to its lane root), or `null` if the lane doesn't
// host this feature (e.g. `BugpunchClient` is C# only by design).
// ---------------------------------------------------------------------------
const THREE_LANE_MIRRORS = [
  // Always-on coordinators + shared state. Must exist on every lane.
  { name: "BugpunchRuntime",      java: "BugpunchRuntime.java",      ios: "BugpunchRuntime.mm",      cs: "BugpunchRuntime.cs" },
  { name: "BugpunchDebugMode",    java: "BugpunchDebugMode.java",    ios: "BugpunchDebugMode.mm",    cs: "BugpunchDebugMode.cs" },
  { name: "BugpunchPoller",       java: "BugpunchPoller.java",       ios: "BugpunchPoller.mm",       cs: "BugpunchPoller.cs" },
  { name: "BugpunchCrashHandler", java: "BugpunchCrashHandler.java", ios: "BugpunchCrashHandler.mm", cs: "BugpunchCrashHandler.cs" },

  // Native-only mirrors (no Editor / Standalone implementation — the
  // feature is meaningless off-device or the C# lane delegates entirely
  // to BugpunchNative).
  { name: "BugpunchUploader",     java: "BugpunchUploader.java",     ios: "BugpunchUploader.mm",     cs: null },
  { name: "BugpunchTunnel",       java: "BugpunchTunnel.java",       ios: "BugpunchTunnel.mm",       cs: null },
  { name: "BugpunchScreenshot",   java: "BugpunchScreenshot.java",   ios: "BugpunchScreenshot.mm",   cs: null },
];

const javaLaneRoot = path.join(androidSrc,
  "bugpunch/src/main/java/au/com/oddgames/bugpunch");
const iosLaneRoot  = path.join(pkgRoot, "Plugins/iOS");
const csLaneRoot   = pkgRoot;

function checkThreeLaneMirror() {
  for (const m of THREE_LANE_MIRRORS) {
    const checks = [
      { lane: "java", root: javaLaneRoot, name: m.java },
      { lane: "ios",  root: iosLaneRoot,  name: m.ios },
      { lane: "cs",   root: csLaneRoot,   name: m.cs },
    ];
    for (const c of checks) {
      if (c.name === null) continue;
      if (!c.name) {
        fail("three-lane",
          `${m.name}: manifest entry missing \`${c.lane}\` field — set to a filename or null`);
        continue;
      }
      const full = path.join(c.root, c.name);
      if (!fs.existsSync(full)) {
        fail("three-lane",
          `${m.name}: ${c.lane} mirror \`${c.name}\` missing at ${path.relative(sdkRoot, full)}`);
      }
    }
  }
}

// ---------------------------------------------------------------------------
// Cross-lane C# classes (those with a Java/iOS sibling per the manifest
// above) must NOT depend on `BugpunchClient` — the cross-lane rule is that
// shared state lives on `BugpunchRuntime`, which both BugpunchClient AND
// the cross-lane class talk to. A C# `BugpunchClient.X` reference inside
// a mirror file means we've broken that rule and the file no longer
// parallels its Java/iOS sibling.
//
// Comments and `using` directives are ignored. The `BugpunchClient` class
// itself is exempt (the rule is about *other* mirrors leaking into it).
// ---------------------------------------------------------------------------
function checkCrossLaneClientLeak() {
  const csMirrors = THREE_LANE_MIRRORS
    .filter(m => m.cs && m.name !== "BugpunchClient")
    .map(m => path.join(csLaneRoot, m.cs));

  for (const file of csMirrors) {
    if (!fs.existsSync(file)) continue;
    const text = fs.readFileSync(file, "utf8");
    const stripped = text
      .replace(/\/\/[^\n]*/g, "")
      .replace(/\/\*[\s\S]*?\*\//g, "");
    const lines = stripped.split(/\r?\n/);
    lines.forEach((line, i) => {
      // Skip `using` directives — they're declarations, not references.
      if (/^\s*using\b/.test(line)) return;
      if (/\bBugpunchClient\b/.test(line)) {
        fail("cross-lane-client",
          `${path.relative(pkgRoot, file)}:${i + 1}: cross-lane mirror references BugpunchClient — ` +
          `read state from BugpunchRuntime instead (Java/iOS siblings depend on BugpunchRuntime, not the client)`);
      }
    });
  }
}

// ---------------------------------------------------------------------------
// Meta-file companion check. Every asset under sdk/package/ needs a sibling
// `.meta` file or Unity will warn on every consumer import.
// ---------------------------------------------------------------------------
function checkMetaCompanions() {
  const missing = [];
  walkFiles(pkgRoot, file => {
    if (file.endsWith(".meta")) return;
    const rel = path.relative(pkgRoot, file).replace(/\\/g, "/");
    if (rel.split("/").includes("node_modules")) return;
    if (!fs.existsSync(file + ".meta")) missing.push(rel);
  });
  // Also check directories.
  (function walkDirs(dir) {
    for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
      if (!e.isDirectory()) continue;
      if (e.name === "node_modules") continue;
      const full = path.join(dir, e.name);
      const rel = path.relative(pkgRoot, full).replace(/\\/g, "/");
      if (!fs.existsSync(full + ".meta")) missing.push(rel + "/");
      walkDirs(full);
    }
  })(pkgRoot);

  if (missing.length) {
    fail("meta",
      `${missing.length} asset(s) missing .meta:\n    ${missing.slice(0, 10).join("\n    ")}` +
      (missing.length > 10 ? `\n    ...and ${missing.length - 10} more` : ""));
  }
}

checkRenderPipelineApis();
checkUnityEditorLeak();
checkSdkAuthHeader();
checkAarFreshness();
checkChangelogVersionMatch();
checkMetaCompanions();
checkThreeLaneMirror();
checkCrossLaneClientLeak();

if (failures.length) {
  console.error(`Preflight failed (${failures.length}):`);
  for (const f of failures) console.error(`- [${f.check}] ${f.msg}`);
  process.exit(1);
}
console.log("Preflight passed.");
