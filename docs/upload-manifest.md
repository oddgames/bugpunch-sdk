# Upload manifest contract

Canonical contract for the on-disk upload queue used by both the Android (`BugpunchUploader.java`) and iOS (`BugpunchUploader.mm`) native uploaders. Server-side ingest validates against this same shape. Any field rename is a coordinated change across all three.

## Storage

- **Location**: `{cacheDir or NSCachesDirectory}/bugpunch_uploads/<uuid>.upload.json` (one file per pending request).
- **Format**: UTF-8 JSON object, one per file.
- **Lifecycle**: enqueued → drained on connectivity / app launch → retried up to `MAX_ATTEMPTS = 10` → on success the listed `cleanupPaths` are deleted and the manifest is removed.

## Single-stage manifest (default)

```jsonc
{
  "url":          "https://.../api/issues/ingest",
  "headers":      { "X-Api-Key": "..." },
  "fields":       { "metadata": "<inner JSON string>" },
  "files": [
    {
      "field":       "screenshot",
      "filename":    "screenshot.jpg",
      "contentType": "image/jpeg",
      "path":        "/abs/path/to/screenshot.jpg",
      "requires":    "screenshot"          // optional — see two-stage flow
    }
  ],
  "cleanupPaths": ["/abs/path/to/screenshot.jpg"],
  "attempts":     0
}
```

## Two-stage manifest (preflight + enrich)

When the server uses budget-gated heavy data (logs, video), the SDK enqueues with `stage = "preflight"`. The first POST is JSON-only (`rawJsonBody`); the response carries `eventId` + `collect[]` listing which heavy fields the server still wants. On success the manifest is rewritten to phase 2 (multipart, URL = `enrichUrlTemplate` with `{id}` substituted, files filtered by `requires ∈ collect`). An empty `collect` skips phase 2 entirely and cleans up immediately. Either phase may fail and retry independently.

```jsonc
{
  "stage":             "preflight",
  "url":               "https://.../api/issues/preflight",
  "enrichUrlTemplate": "https://.../api/issues/{id}/enrich",
  "headers":           { "X-Api-Key": "...", "Content-Type": "application/json" },
  "rawJsonBody":       "{...}",
  "files":             [ /* same shape as above; may carry `requires` */ ],
  "cleanupPaths":      ["..."],
  "attempts":          0
}
```

Expected preflight response:

```jsonc
{
  "eventId": "ev_...",
  "collect": ["logs", "screenshot"],   // [] = skip phase 2
  "matchedDirectives": [ /* optional */ ]
}
```

### `attachmentsAvailable` — required for any field you want server to admit

The phase-1 JSON body MUST include an `attachmentsAvailable` array listing every `requires` key that appears on any `files[]` entry OR any deferred entry. The server's per-fingerprint budget filter (`issues/ingest.ts`: `if (offered.has(f)) collect.push(f)`) **drops any field that isn't in this list** before composing `collect[]`. That filter is silent on the client — a missing key looks like "the dashboard randomly forgot to attach my logs" with no SDK-side warning.

```jsonc
{
  // ... event fields ...
  "attachmentsAvailable": ["logs", "screenshot", "context_screenshot",
                          "video", "attach_saves"]
}
```

The set must be the union of:
- every `requires` on a regular `FileAttachment`, and
- every `requires` on a `DeferredAttachment` (e.g. `video_ring`).

Both Android (`BugpunchUploader.collectAvailableRequires`) and iOS (`BPCollectAvailableRequires`) centralise this in one helper so callers can't omit a list. New attachment kinds added to either list automatically join the advertised set.

Builtin keys understood by the server: `logs`, `screenshot`, `context_screenshot`, `video`, `anr_screenshots`, `traces`. Anything else must match `^attach_[a-z0-9_]{1,56}$` (game-data attachment rules) or it's dropped.

## Field reference

| field             | type            | required | meaning                                                                 |
|-------------------|-----------------|----------|-------------------------------------------------------------------------|
| `url`             | string          | yes      | Phase 1 endpoint. Phase-2 manifests rewrite this to the enrich URL.     |
| `headers`         | object          | yes      | Sent verbatim. Use for `X-Api-Key`, `Content-Type` on JSON stages.      |
| `fields`          | object          | no       | Multipart form fields (string-valued).                                  |
| `rawJsonBody`     | string          | no       | Mutually exclusive with `fields` — when present, request is JSON, not multipart. |
| `files`           | array           | no       | Multipart file parts. Each: `{ field, filename, contentType, path, requires? }`. |
| `cleanupPaths`    | array of string | no       | Absolute paths to delete on success. Always cleaned up after final stage. |
| `attempts`        | int             | yes      | Retry counter. `>= MAX_ATTEMPTS` → manifest dropped + cleanup.           |
| `stage`           | "preflight"     | no       | Marks two-stage flow. Phase-2 manifests omit this after rewrite.        |
| `enrichUrlTemplate` | string        | no       | Required when `stage = "preflight"`. Must contain literal `{id}`.       |

## Constants pinned across implementations

| name                | Java                                | Obj-C++                              | rationale                                                |
|---------------------|-------------------------------------|--------------------------------------|----------------------------------------------------------|
| `MAX_ATTEMPTS`      | `10`                                | `kMaxAttempts = 10`                  | Cap retries across launches; both must agree.            |
| connect timeout     | `15_000` ms                         | `kConnectTimeout = 15.0` s           |                                                          |
| read/resource timeout | `60_000` ms                       | `kResourceTimeout = 90.0` s          | iOS uses resource timeout (full-request); Android uses read. |
| queue dir           | `bugpunch_uploads`                  | `kQueueDir = @"bugpunch_uploads"`    | Same on both — server log inspection is path-blind.      |

If any of these change on one platform, update the other and bump this doc. Server-side validation does not depend on these (they're client-side retry policy), but mismatches make device-by-device debugging confusing.

## When the server changes

A new field on either request or response is a three-place edit:
1. **Server** — `server/src/services/issues/ingest.ts` and route validation.
2. **Android** — `BugpunchUploader.java` (manifest write) and any service that calls `enqueue` / `enqueuePreflight`.
3. **iOS** — `BugpunchUploader.mm` (manifest write) and corresponding callers.

Keep the field names byte-identical. Server JSON parsing is case-sensitive and does not accept aliases.
