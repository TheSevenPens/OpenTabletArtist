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

## Published release notes

- [v0.77.0](v0.77.0.md)
- [v0.76.0](v0.76.0.md)
- [v0.75.0](v0.75.0.md)
- [v0.74.0](v0.74.0.md)
- [v0.73.0](v0.73.0.md)
- [v0.72.0](v0.72.0.md)
- [v0.71.0](v0.71.0.md)
- [v0.70.0](v0.70.0.md)
- [v0.69.0](v0.69.0.md)
- [v0.68.0](v0.68.0.md)
- [v0.67.0](v0.67.0.md)
- [v0.66.0](v0.66.0.md)
- [v0.65.0](v0.65.0.md)
- [v0.64.0](v0.64.0.md)
- [v0.63.0](v0.63.0.md)
- [v0.62.0](v0.62.0.md)
- [v0.61.0](v0.61.0.md)
- [v0.60.0](v0.60.0.md)
- [v0.59.0](v0.59.0.md)
- [v0.58.0](v0.58.0.md)
- [v0.57.0](v0.57.0.md)
- [v0.56.0](v0.56.0.md)
- [v0.55.0](v0.55.0.md)
- [v0.54.0](v0.54.0.md)
- [v0.53.0](v0.53.0.md)
- [v0.52.0](v0.52.0.md)
- [v0.51.0](v0.51.0.md)
- [v0.50.0](v0.50.0.md)
- [v0.47.0](v0.47.0.md)
- [v0.46.0](v0.46.0.md)
- [v0.45.0](v0.45.0.md)
- [v0.44.0](v0.44.0.md)
- [v0.43.0](v0.43.0.md)
- [v0.42.0](v0.42.0.md)
- [v0.41.0](v0.41.0.md)
- [v0.40.0](v0.40.0.md)
- [v0.39.0](v0.39.0.md)
- [v0.38.0](v0.38.0.md)
- [v0.37.0](v0.37.0.md)
- [v0.36.0](v0.36.0.md)
- [v0.35.0](v0.35.0.md)
- [v0.34.0](v0.34.0.md)
- [v0.33.0](v0.33.0.md)
- [v0.32.0](v0.32.0.md)
- [v0.31.0](v0.31.0.md)
- [v0.30.0](v0.30.0.md)
- [v0.29.0](v0.29.0.md)
- [v0.28.0](v0.28.0.md)
- [v0.27.0](v0.27.0.md)
- [v0.26.0](v0.26.0.md)
- [v0.25.0](v0.25.0.md)
- [v0.24.0](v0.24.0.md)
- [v0.23.0](v0.23.0.md)
- [v0.22.0](v0.22.0.md)
- [v0.21.0](v0.21.0.md)
- [v0.20.7](v0.20.7.md)
- [v0.20.6](v0.20.6.md)
- [v0.20.4](v0.20.4.md)
- [v0.20.3](v0.20.3.md)
