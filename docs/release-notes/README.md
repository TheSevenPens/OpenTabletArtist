# Release notes

One file per tag: `v0.76.0.md`. The release workflow publishes it as the top of the
release page, with the commit list folded into a `<details>` block underneath.

**Write it before tagging.** If the file is missing the release still publishes — just
the commit list — because a missing summary must never be what fails a release after the
build has already run. But then nobody gets told what changed.

## What goes in one

Write for someone who downloaded the zip to draw with a tablet, not for someone who will
read the diff. That means:

- **What they can now do, or what stopped going wrong.** Not which class changed.
- **Say the symptom, not the cause.** "A save that fails now says so, and retries" — not
  "truthful apply/persist outcomes".
- **Leave out anything invisible from outside**: test fixes, dependency bumps, refactors,
  release plumbing, internal review findings. They are in the commit list already.
- **Skip the issue numbers.** Anyone who wants them has the commit list and the compare
  link.
- **macOS and Linux are work in progress and unreleased** — only the Windows zip ships.
  Mention that work at the end, if at all, so it doesn't read as something to download.

Most releases are a handful of bullets. v0.74.0 is long because it was an interface
redesign; v0.75.0 is three paragraphs because it was one change.

The detail is not lost — the commit messages carry it, which is what they are for.
