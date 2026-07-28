# Pen page

*(Part of the [User Manual](USERMANUAL.md).)*

The **Pen** page's tabs are **movement**, **inputs**, and **pressure** — each its own section below. It shares the [Tablet page](TABLET.md)'s header (linked switcher + Refresh).

## movement

Pick a movement mode — **Normal (Absolute)** maps the pen 1:1 to the screen (recommended for drawing) or **Mouse-like (Relative)** moves the cursor like a mouse (often for games).

Some people on Windows find that the typical behavior of a drawing tablet when using Windows Ink works well in drawing apps, but does weird things for other apps because pen taps are treated as touch gestures. If you want to make Windows treat the pen less like touch and more like a normal mouse in terms of tapping and clicking - then enable **Don't use Windows Ink** toggle *(Windows only)*. While enabled you will not be able to use pressure and tilt. 

Below the modes is a **Position smoothing** slider (moved here from the pressure tab, since it steadies the pen's on-screen position) — 0 = off to 1 = max, perceptually scaled; it applies while the pen is down and resets on lift. 

## inputs


This section controls the different inputs on the pen, including the pen tip, the pen eraser, and any pen buttons. Currently, OpenTabletDriver only allows you to use the default behavior of the buttons, so you can't control what they do. They just perform whatever Windows Ink does by default. This is not a limitation of Open Tablet Artist, but due to OpenTabletDriver, and in the future, if OpenTabletDriver changes, more options might be supported.

You can disable the pen tip, so tapping or clicking with the pen does nothing, but you can still move the pointer around. You can also disable pressure sensitivity, so you can click and tap, but you won't get different levels of pressure. You can also completely disable tilt.

## pressure

This section controls how pressure works. Here, you can control the pressure curve by dragging the node in the middle of the curve up or down, making the pen either more or less sensitive. Pressure smoothing allows you to get rid of jitter that is often present when you're drawing with very low pressure. On the right is a small canvas where you can draw and see what these settings actually do with pressure. Above the canvas is a live pressure gauge which shows you the exact data coming back from the pen and how the pressure data is processed. So you can see how smoothing and the curve actually affect the data visually.

