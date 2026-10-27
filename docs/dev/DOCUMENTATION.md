# Maintaining the documentation site

The [user documentation](https://docs.sevenpens.com/opentabletartist) is a GitBook
site, like the [OpenTabletDriver docs](https://docs.sevenpens.com/opentabletdriver).
Its pages live in the [OpenTabletArtistDocs](https://github.com/TheSevenPens/OpenTabletArtistDocs)
repository, which GitBook Git Sync keeps in step with the GitBook space in both
directions. Edit pages in the GitBook editor, or edit the Markdown in that repository;
either way the other side follows. Documentation is revised and published
independently of an application release.

## Audience and location

| Location | Audience | Published? |
| --- | --- | --- |
| OpenTabletArtistDocs repo / GitBook | People installing, using, or troubleshooting OTA | Yes, on GitBook |
| `docs/dev/` | Contributors building, testing, or maintaining OTA | No |
| `docs/design/` | Contributors investigating and designing changes | No |
| `docs/release-notes/` | Release-note sources and release preparation | No |
| `docs/README.md` | Repository index for both audiences | No |
| `docs/THIRD_PARTY_NOTICES.md` | Third-party attribution and licenses | No |

Keep contributor material in this repository. Advanced features that help people
use OTA still belong in the user documentation.

## Links the app depends on

`AboutViewModel` links to the documentation home (`UserManualUrl`) and the
Getting help page (`HelpUrl`, `get-help/help`). Renaming or moving either page in
GitBook breaks those links in every released build, so add a GitBook redirect
if you do. Page URLs include the `SUMMARY.md` group heading (`get-started/`,
`use-ota/`, `get-help/`), so renaming a group moves every page under it.

## Old URLs

The user guide used to be an MkDocs site at
`https://thesevenpens.github.io/OpenTabletArtist/`, and older app builds and
release notes still link there. `docs/site-redirects/` holds one small HTML page
per former URL that sends the reader to the matching GitBook page (and a
`404.html` that sends anything else to the home page). The `documentation`
workflow publishes that folder to GitHub Pages when it changes on `master`.
Keep it published; it is not built from anything.

`docs/user/USERMANUAL.md` is an even older compatibility link: the first app
releases opened that file on GitHub. It points to the documentation home.
