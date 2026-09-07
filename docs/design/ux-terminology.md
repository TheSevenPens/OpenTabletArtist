# UX & navigation terminology

> Status: **canonical.** This is the agreed vocabulary for the app's navigation and page structure.
> Use these terms in code, comments, and docs. The user manual's
> [Using the Interface](../user/USERMANUAL.md#using-the-interface) says the same thing in the user's
> words — if the two ever disagree, the manual describes what shipped and this file is the one that's
> wrong.

## The shape

Three levels, all horizontal-first: **page → tab → subtab**. Navigation runs across the top of the
window rather than down a left pane, and the third level exists only where a tab had to divide again.

```
Window
├─ Page menu ─────────────── across the top, FLAT
│    home · tablet · pen · scribble · settings · advanced
│    (a tablet switcher + Refresh sit at its right, on tablet and pen)
│
├─ Tab menu ──────────────── under the page menu, in larger type, FLAT
│    that page's tabs; pages with nothing to divide have none
│
└─ Content
     └─ Subtabs ─────────── a vertical rail down the LEFT of the content, FLAT
          only where a tab divides again
```

## Glossary

| Term | Meaning |
|---|---|
| **Page** | A top-level destination. There are six, and the set is fixed. |
| **Page menu** | The row of page names across the top of the window. **Flat** — no grouping, no children. |
| **Tab** | A division of one page. |
| **Tab menu** | The row of tab names under the page menu, set in larger type than the page menu. **Flat.** A page with nothing to divide has no tab menu at all. |
| **Subtab** | A division of one tab — the third and last level. |
| **Subtab rail** | The vertical list of subtabs down the left of the content area. **Flat.** Width is the `SubtabRailWidth` token. |
| **Switcher** | The tablet dropdown at the right of the page menu, with its **Refresh**. Scopes the page to one tablet. |
| **Section** | A titled or untitled grouping of related settings *within* a tab or subtab. Not navigation — it selects nothing. |
| **Entity** | One THING in a list (a preset, a plugin, a config, a tablet). A row, not a card. |

## What has what

| Page | Tabs | Subtabs |
|---|---|---|
| **home** | — | — |
| **tablet** | about · mapping · calibration · buttons · wheels *(+ filters · json when enabled)* | — |
| **pen** | basics · pressure | — |
| **scribble** | — | — |
| **settings** | presets · hotkeys · theme · system · drivers · dev | **theme** → theme · backdrop · colors<br>**dev** → warnings · config errors · tablets · interface · screenshots |
| **advanced** | daemon · console · configs · diagnostics · plugins | — |

Gated entries stay in their list and hide themselves rather than being removed: **per-app presets**
while the feature is disabled, **drivers** off Windows, **system** on macOS, **filters** / **json**
unless turned on under settings → dev.

## Invariants

Hold these when adding or renaming UI:

1. **Three levels, and the third is rare.** A subtab is always a leaf. If something seems to need a
   fourth level, the tab is doing too much — split the tab, don't nest.
2. **Every navigator is flat.** None of the three levels groups, nests, or has children. A "section
   label" inside a list is a heading, not a nav level.
3. **The type ramp encodes the level.** Page menu at `TypeNavSize`, tab menu at `TypePivotSize` (larger
   — it names where you are within the page you already chose), section headings inside a tab at
   `TypeSectionSize`. A heading must never out-type the menu that contains it.
4. **Selection is colour and weight, never an underline.** Accent plus a heavier weight in both menus; a
   selected subtab takes an accent bar down its left edge instead.
5. **A page earns a tab menu by having something to divide.** Home and Scribble have none, and that is
   the normal case for a page with one job — not an omission to fix.

## Historical

The app used to be **two levels deep, and no deeper**: a hierarchical **page navigation bar** down a
**left pane**, with *leaf* / *group* / *parent* **nodes**, opening a **simple page** or a **tabbed page**
with its own **complex header** and **subpage navigation**. The Zune redesign replaced that whole model —
the left pane became the page menu across the top, the node hierarchy flattened, and the third level
(subtabs) arrived for Theme and Dev, which the old "two levels, no recursion" invariant explicitly
forbade.

Those terms — *left pane*, *right pane*, *node*, *group node*, *parent node*, *simple page*, *tabbed
page*, *subpage*, *subpage navigation*, *complex header* — are **historical**. They survive in some code
identifiers and comments (`NavNode`, `ComplexHeader`, `AdvancedTab`), which is why they are recorded
here; do not use them in new code, comments, or docs.
