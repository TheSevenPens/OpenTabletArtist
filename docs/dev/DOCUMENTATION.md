# Maintaining the documentation site

The [published documentation](https://thesevenpens.github.io/OpenTabletArtist/)
is built only from the Markdown files in `docs/user/`. Edit those files directly;
there is no second copy of the user guide to maintain. Documentation can be revised
and published independently of an application release.

## Audience and location

| Location | Audience | Published to GitHub Pages? |
| --- | --- | --- |
| `docs/user/` | People installing, using, or troubleshooting OTA | Yes |
| `docs/dev/` | Contributors building, testing, or maintaining OTA | No |
| `docs/design/` | Contributors investigating and designing changes | No |
| `docs/release-notes/` | Release-note sources and release preparation | No |
| `docs/README.md` | Repository index for both audiences | No |

The site's home page is `docs/user/README.md`. The `docs_dir` setting in
`mkdocs.yml` defines the publication boundary: files outside `docs/user/` are
absent from the generated site and its search index. Keep contributor material
outside that folder. Advanced features that help people use OTA still belong in
the user documentation.

## Publishing

The `documentation` GitHub Actions workflow builds the site when `docs/user/`,
`mkdocs.yml`, `scripts/docs/`, or the documentation workflow itself changes.
Pull requests build and
check links without publishing. Changes on `master` publish to GitHub Pages after
the documentation build succeeds. The workflow can also be run manually from
the repository's Actions tab.

The application's `build` workflow skips changes confined to `docs/`, the root
`README.md`, and those documentation configuration and dependency files. Editing
developer or design notes alone triggers neither workflow. A change that also
touches application code or its build workflow still runs the Windows, Linux,
and macOS checks. Documentation deployment does not wait for those checks.
These are GitHub Actions [path filters](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax#onpushpull_requestpull_request_targetpathspaths-ignore)
on both push and pull-request events.

GitHub Pages uses **GitHub Actions** as its publishing source, with the
`github-pages` deployment environment. There is no generated-site branch and no
personal access token in the workflow. The build has read-only repository access;
only the deployment job can publish Pages.

## Preview locally

Use Python 3.12 and an isolated environment. On Windows:

```powershell
python -m venv .venv-docs
.venv-docs\Scripts\python -m pip install -r scripts/docs/requirements.txt
.venv-docs\Scripts\python -m mkdocs serve
```

On macOS or Linux:

```bash
python3 -m venv .venv-docs
.venv-docs/bin/python -m pip install -r scripts/docs/requirements.txt
.venv-docs/bin/python -m mkdocs serve
```

Open the local URL printed by MkDocs. To run the same check as CI, replace
`serve` with `build --strict`. Generated files in `site/` and the local Python
environment are ignored by Git.

## Pages and links

- `docs/user/README.md` is the site's home page; `docs/README.md` stays in the repo.
- `mkdocs.yml` defines the sidebar. Add new user guides there so readers can find them.
- Link to other documentation using relative Markdown paths, such as
  `[Pen](PEN.md)`. MkDocs converts these to site URLs, while GitHub keeps
  them readable in the repository.
- Keep links to building, contributing, design notes, and site maintenance in
  the repository documentation. User guides should link to user-facing help.
- The site's Releases link opens GitHub Releases, where users can download OTA
  and read about changes. Release-note preparation instructions stay in the repo.
- A strict build checks that user guides appear in the sidebar, plus document
  links and heading anchors. It does not verify
  the availability of external websites.

## Dependencies

The site uses MkDocs and Material for MkDocs. `scripts/docs/requirements.in`
contains the direct requirements; `scripts/docs/requirements.txt` pins the full
resolved dependency set for local builds and CI.

To update the lock file with [uv](https://docs.astral.sh/uv/):

```bash
uv pip compile --universal --python-version 3.12 scripts/docs/requirements.in --output-file scripts/docs/requirements.txt
```

Preview and run a strict build before committing an updated lock file.
