# UX & navigation terminology

> Status: **canonical.** This is the agreed vocabulary for the app's navigation and page structure.
> Use these terms in code, comments, and docs. Where today's code uses a different name, see
> [Current code → target term](#current-code--target-term) — those renames are a planned follow-up.

## The shape

The app is **two levels deep, and no deeper** — navigation on the left, content on the right, at each
level. The second level only exists inside a *tabbed page*.

```
Window
├─ Left pane ── Page navigation bar        ← primary navigation, HIERARCHICAL (nodes)
│    ├─ leaf node     → opens a page
│    ├─ group node    → groups child nodes, opens nothing   (e.g. ADVANCED)
│    └─ parent node   → has child nodes                     (e.g. Tablets → one child per tablet)
│
└─ Right pane ─ Page  (always has a title)
     │
     ├─ Simple page   =  title + content
     │
     └─ Tabbed page   =  complex header
          ├─ Left ──  Subpage navigation   ← secondary navigation, FLAT (tabs)
          │             └─ tab → opens a subpage
          └─ Right ─  Subpage  =  title + content   (the same component as a simple page)
```

## Glossary

| Term | Meaning |
|---|---|
| **Left pane / right pane** | The app's two top-level regions. |
| **Page navigation bar** | The left-pane navigator. **Hierarchical.** |
| **Node** | An entry in the page navigation bar. |
| &nbsp;&nbsp;• **leaf node** | Opens a page (e.g. HOME, SCRIBBLE). |
| &nbsp;&nbsp;• **group node** | Groups child nodes and opens nothing (e.g. ADVANCED). |
| &nbsp;&nbsp;• **parent node** | Has child nodes (e.g. Tablets → one child node per tablet). |
| **Page** | A surface in the right pane. Always has a **title**. Either a *simple page* or a *tabbed page*. |
| **Simple page** | `title + content`. The leaf surface. |
| **Tabbed page** | `complex header + subpage navigation + subpage`. A page whose content is split into tabs. |
| **Subpage navigation** | The navigator inside a tabbed page. **Flat** — no hierarchy. |
| **Tab** | An entry in the subpage navigation; opens a subpage. |
| **Subpage** | `title + content`. The **same component** as a simple page — "subpage" is simply its name when it is hosted inside a tabbed page. |
| **Title** | Universal: every page and subpage has one. On a simple page / subpage it *is* the whole header. |
| **Complex header** | The tabbed page's own header component. Always contains a title; may add custom, page-specific content; persists while you switch subpages. |

## Invariants

These are what make the model coherent — hold them when adding or renaming UI:

1. **Two levels, no recursion.** A subpage is always a leaf (`title + content`); it never contains its
   own navigation. If something seems to need a third level, revisit the design rather than nesting.
2. **The leaf is one component.** A **simple page** and a **subpage** are the same thing; the only
   difference is whether the neighbour that selects it is the page navigation bar or a subpage
   navigation.
3. **Two distinct navigators.** The **page navigation bar** is hierarchical (nodes, with grouping and
   children); the **subpage navigation** is flat (tabs). They share a role, not a type — even if a
   future implementation reuses one control, they are styled and scoped differently in context.
4. **Two distinct headers, one shared atom.** A leaf's header is *just a title*; a tabbed page has a
   distinct **complex header**. Both always contain a **title** — the title is the shared atom.

**Subpage titles on a tabbed page.** The active subpage's title may be surfaced by the *complex header*
as a breadcrumb (`tabbed page › subpage`) instead of being repeated in the subpage body — this is how the
**OpenTabletDriver** page works (`OPENTABLETDRIVER › DAEMON`), which keeps one title in a consistent spot
and reclaims the vertical space. The **tablet** page is a deliberate exception: it keeps its rich complex
header, and its subpages use small section labels.

## Current code → target term

The vocabulary above is the target. Today's code differs in places; these are the renames to make in
the follow-up (tracked separately):

| Concept | Today | Target |
|---|---|---|
| Tabbed page | "hub" / hub page (`OpenTabletDriverView`, and `TabletDetailView` implicitly) | **tabbed page** |
| Page-nav entry | `NavButton`, `TabletNavItemViewModel` | **node** (leaf / group / parent) |
| Group node | `NavGroupHeader` (the ADVANCED toggle) | **group node** |
| Subpage-nav entry | `TabRadioButton`, `OtdHubTab` | **tab** (already aligned) |
| Complex header | **done** — the shared `Controls/ComplexHeader` control, used by both tabbed pages | a named, shared **complex header** |

Done so far (phases 1–2):
- The **OpenTabletDriver** tabbed page now has a **complex header**, shown as a breadcrumb (`OPENTABLETDRIVER › <subpage>`); its subpages no longer carry their own title.
- The **tablet** tabbed page's header (name + Refresh + Forget) is the rich end of the *complex header* spectrum and is now built with the shared `ComplexHeader` control.

Still pending: the page-nav **node** vocabulary and the "hub" → **tabbed page** rename (later phases).
