# Tablet page

*(Part of the [User Manual](USERMANUAL.md).)*

A tablet's settings live on two top-nav pages — **Tablet** and **Pen** — each with a **switcher** at the top (shown when more than one tablet is connected; the Tablet, Pen, and Scribble switchers are linked, so they always show the same tablet) and a **Refresh** in the header that re-reads settings from the daemon (useful after changes in the OTD UX). **Forget** a tablet from its card on Home.

The **Tablet** page's tabs are **about**, **mapping**, **calibration**, **buttons**, and **wheels** (plus **hover**, **filters**, and **json**, hidden unless enabled on **Settings → Developer**). *(Pen dynamics — now the **pressure** tab — moved to the [**Pen** page](PEN.md).)* Each tab is its own section below.

## about

The about section just gives you some basic information about the tablet.

## mapping


The mapping section controls how the active area of the tablet is mapped to a display. If you click on any display, the active area will immediately be mapped to that display. Note that the Pen tablet artist does not give you the ability to map the active area so that it spans multiple splits. By default, as much as possible of the full active area of the tablet is mapped to the display, and this is done in a way that preserves the correct proportions as you draw. Some of you may like to draw with a smaller active area than is enabled. You can use the size slider on the right column to shrink the active area. If you want to restore it back to its maximum possible size, press the maximize button.

## calibration

For many Pen displays, there was often a slight inaccuracy in mapping the position of the pen to the display. In these cases, performing an initial calibration might help.

The calibration process is pretty simple. It will show you a series of circles, and you'll hold down your pen on the circle until enough data is collected. Once all the circles are complete, the calibration process is done, and you should find that your pen and the pointer are completely in sync.

There are three different calibration options offered: 4 point, 9 point, and 25 point. For most people, the four-point calibration is not only the easiest but also the one that will work most often. Most people will only need to do the four-point calibration, but some have tablets that are more tricky. For those, you can try the 9 point or the 25 point calibration.

## buttons

You can assign actions to any buttons on the tablet. If you want to quickly disable all the actions, the "Buttons Enable" toggle at the top will disable them all immediately, but will not delete any actions. To get the behavior of the buttons back, you simply switch the toggle again.

## wheels

If your tablet has wheels or dials, they'll show up here. You can assign actions to the clockwise and counterclockwise rotations.


