# Developer tools

*(The **Dev** tab under [Settings](SETTINGS.md).)*

If you're just using OpenTabletArtist to draw, you will never need to come to the developer page. This page is for people working on OpenTabletArtist itself — a collection of developer tools for collecting data and testing the app. The features here may change significantly from release to release, and there's no guarantee that this page will always exist.

The page is divided into **subtabs**, listed down the left-hand side:

- **Warnings** — force any health warning into Home's *Needs attention* list. These are synthetic: nothing on disk changes, and clearing the toggle removes the card again. The left column covers app- and system-level warnings, the right column the per-tablet ones.
- **Config errors** — the opposite. These write a real, saved change to the active tablet — pushing its area off-screen, rotating it oddly, breaking the pen for drawing — so the warning appears for genuine bad state rather than being asserted. Undo each from the tablet's own tabs.
- **Tablets** — add a saved (remembered, not-detected) profile for any tablet OpenTabletDriver supports, so its row on **Home** can be exercised without the hardware. It's a real profile: remove it from its row's **⋯** menu on Home (**Forget this tablet**), or **Clear** here.
- **Interface** — reveal the normally-hidden **filters** and **json** tabs and a couple of rarely-needed controls on a tablet's page, and force the window to an exact pixel size.
- **Screenshots** — visit every page in turn and save an image of each into a timestamped folder, or add a **Capture page** button for shooting one page at a time.

Because these are developer tools, the subtabs and what's on them change from release to release.
