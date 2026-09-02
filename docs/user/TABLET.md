# Tablet page

*(Part of the [User Manual](USERMANUAL.md).)*

A tablet's settings live on two top-nav pages — **Tablet** and **Pen** — each with a **switcher** at the top (shown when more than one tablet is connected; the Tablet, Pen, and Scribble switchers are linked, so they always show the same tablet) and a **Refresh** in the header that re-reads settings from the daemon (useful after changes in the OTD UX). **Forget** a tablet from its card on Home.

The **Tablet** page's tabs are **about**, **mapping**, **calibration**, **buttons**, and **wheels** (plus **filters** and **json**, hidden unless enabled on **Settings → Developer**). *(Pen dynamics — now the **pressure** tab — moved to the [**Pen** page](PEN.md).)* Each of the main tabs is described below, followed by the **filters** tab.

## about

The about section just gives you some basic information about the tablet.

## mapping


The mapping section controls how the active area of the tablet is mapped to a display. If you click on any display, the active area will immediately be mapped to that display. Note that OpenTabletArtist does not give you the ability to map the active area so that it spans multiple displays. By default, as much as possible of the full active area of the tablet is mapped to the display, and this is done in a way that preserves the correct proportions as you draw. Some of you may like to draw with a smaller active area than is enabled. You can use the size slider on the right column to shrink the active area. If you want to restore it back to its maximum possible size, press the maximize button.

## calibration

For many Pen displays, there was often a slight inaccuracy in mapping the position of the pen to the display. In these cases, performing an initial calibration might help.

The calibration process is pretty simple. It will show you a series of circles, and you'll hold down your pen on the circle until enough data is collected. Once all the circles are complete, the calibration process is done, and you should find that your pen and the pointer are completely in sync.

There are three different calibration options offered: 4 point, 9 point, and 25 point. For most people, the four-point calibration is not only the easiest but also the one that will work most often. Most people will only need to do the four-point calibration, but some have tablets that are more tricky. For those, you can try the 9 point or the 25 point calibration.

## buttons

You can assign actions to any buttons on the tablet. If you want to quickly disable all the actions, the "Buttons Enable" toggle at the top will disable them all immediately, but will not delete any actions. To get the behavior of the buttons back, you simply switch the toggle again.

## wheels

If your tablet has wheels or dials, they'll show up here. You can assign actions to the clockwise and counterclockwise rotations.



## filters

*(Hidden unless you turn on **Show the Filters tab**, under *Tablet page extras* on **Settings → Developer**.)*

A read-only list of the OpenTabletDriver **filters** on this tablet's profile — the plugin stages that sit in the pen pipeline between the tablet and your screen. Each card shows a friendly name (**Pen Dynamics**, **Hover Limit**, **Calibration**) or, for a filter OpenTabletArtist doesn't recognize, its raw type name; below that, the filter's full type path; and on the right, whether it's **Enabled** or **Disabled**.

A **Legacy** chip marks a filter left behind by an older version of the app. The driver has no plugin for that type, so it does nothing — these are removed automatically.

### Only OpenTabletArtist's own filters stay enabled

When you're running the bundled daemon, OpenTabletArtist keeps **only its own three filters** enabled — Pen Dynamics, Calibration, and Hover Limit (see the [Plugins guide](PLUGINS.md#whats-inside-the-pen-dynamics-plugin); Hover Limit is currently inactive). Any other filter on the profile, whether from a third-party plugin or one of OpenTabletDriver's built-ins, is switched **off**: once when OTA loads your settings, and again every time it saves them. This is deliberate — it keeps the pen behaving the same way no matter what was enabled elsewhere.

Two things worth knowing:

- The filter is only **disabled**, never deleted. It stays on the profile and in this list (marked *Disabled*), with its settings intact — it just won't run.
- If you enable a filter in the OpenTabletDriver UX and then open OpenTabletArtist, expect to find it turned off again.

Only filters are affected; your **output mode** is left alone.

This doesn't apply when OTA is connected to an **External (not started by OTA)** daemon — see [Advanced → Daemon](ADVANCED.md#daemon). In that case your filters are left exactly as you set them.
