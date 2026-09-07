# Scribble page

*(Part of the [User Manual](USERMANUAL.md).)*

The scribble page is essentially a mini paint app built into OpenTabletArtist. It's intended to give you a way of trying more of the Pen tablet and pen's features without always having to open up an application.

It is useful for debugging whether a problem you're experiencing lies with the tablet, the driver, or an application. For example, if you're having trouble with pressure in an application, check the scribble page to see if that same problem occurs. If it does, then the problem is either with the driver or perhaps with the tablet itself, but probably not with the application.

The scribble page has several brush types to explore different facets of the pen, from pressure to tilt and barrel rotation; pick one from the **Brush** dropdown at the left of the controls row. **Clear** empties the canvas and **Copy** puts the drawing on the clipboard as an image.

With more than one tablet connected, the **switcher** at the right of the page menu picks which one you're drawing with — the same dropdown, in the same place, as on the Tablet and Pen pages.

What you draw with is the pen data the driver itself reports — its own view of the pen, before Windows sees it — so the page works even if Windows Ink isn’t set up yet. That does mean the tablet needs an **Absolute** output mode for the canvas to know where the pen is; in a Relative mode the canvas is disabled and says so, though the readouts above it keep working.
