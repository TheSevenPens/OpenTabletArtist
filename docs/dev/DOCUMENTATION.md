# Maintaining the documentation site

The [published documentation](https://thesevenpens.github.io/OpenTabletArtist/)
is built from the Markdown files in `docs/`. Edit those files in the same pull
request as a feature change; there is no second copy of the user guide to maintain.

## Publishing

The `documentation` GitHub Actions workflow builds the site when documentation,
site configuration, or documentation dependencies change. Pull requests build and
check links without publishing. Changes on `master` publish to GitHub Pages after
the documentation build succeeds. The workflow can also be run manually from
the repository's Actions tab.

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

- `docs/README.md` is the site's home page.
- `mkdocs.yml` defines the sidebar. Add new user guides there so readers can find them.
- Link to other documentation using relative Markdown paths, such as
  `[Pen](../user/PEN.md)`. MkDocs converts these to site URLs, while GitHub keeps
  them readable in the repository.
- Link to source files outside `docs/` using their full GitHub URL.
- Design notes and older releases are published and searchable, but are listed
  through their indexes instead of crowding the main sidebar. Historical design
  notes describe decisions at the time they were written, not necessarily the
  behavior of the current release.
- A strict build checks document links and heading anchors. It does not verify
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
