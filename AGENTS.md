# Project instructions

## Required feature and bug-fix records

- Every new feature and bug fix must include a corresponding entry in
  `huishoudboek/CHANGELOG.md` as part of the same change.
- Before implementing, ask the user which version the entry belongs to,
  unless they have already explicitly specified it for the current task.
  Never assume the latest version or automatically choose a new version.
- Write concise Dutch entries matching the existing changelog style,
  clearly describing the new feature or corrected behavior.
- Preserve existing release notes.
- Do not bump application versions unless the user requests it.
  When a version bump is requested, keep `version` in
  `huishoudboek/config.yaml` and `io.hass.version` in
  `huishoudboek/Dockerfile` synchronized.
