# Pen page

*(Part of the [User Manual](USERMANUAL.md).)*

The **Pen** page's tabs are **basics**, **inputs**, and **pressure** — each its own section below. It shares the [Tablet page](TABLET.md)'s header (linked switcher + Refresh).

## basics

Two columns — **Movement** on the left, **Pressing** on the right.

**Movement** picks how the pen maps to the screen: **Normal (Absolute)** maps the pen 1:1 to the screen (recommended for drawing) or **Mouse-like (Relative)** moves the cursor like a mouse (often for games). Below the modes is a **Position smoothing** slider (moved here from the pressure tab, since it steadies the pen's on-screen position) — 0 = off to 1 = max, perceptually scaled; it applies while the pen is down and resets on lift.

**Pressing** *(Windows only)* controls how the pen's taps and clicks behave:

- **Presses normally** — works like an artist would expect, with pressure sensitivity and tilt (using Windows Ink).
- **Presses like a mouse** — uses OpenTabletDriver's plain output, so the pen clicks like a mouse (handy for apps where pen taps get treated as touch and act oddly), but you lose pressure and tilt.

## inputs


This section controls the different inputs on the pen, including the pen tip, the pen eraser, and any pen buttons. Currently, OpenTabletDriver only allows you to use the default behavior of the buttons, so you can't control what they do. They just perform whatever Windows Ink does by default. This is not a limitation of Open Tablet Artist, but due to OpenTabletDriver, and in the future, if OpenTabletDriver changes, more options might be supported.

Each input (the pen tip, the eraser, and each button) shows a small card. When the input is set to its normal, recommended behavior, the card simply reads **Working normally** — there's nothing to do. If an input has been changed to something that won't behave normally (for example, imported settings that set it to a mouse or keyboard action), the card explains that in plain language, shows what it's currently set to, and offers a single **Fix** button to reset it to normal.

You can disable the pen tip, so tapping or clicking with the pen does nothing, but you can still move the pointer around. You can also disable pressure sensitivity, so you can click and tap, but you won't get different levels of pressure. You can also completely disable tilt.

## pressure

This section controls how pressure works. Here, you can control the pressure curve by dragging the node in the middle of the curve up or down, making the pen either more or less sensitive. Pressure smoothing allows you to get rid of jitter that is often present when you're drawing with very low pressure. On the right is a small canvas where you can draw and see what these settings actually do with pressure. Above the canvas is a live pressure gauge which shows you the exact data coming back from the pen and how the pressure data is processed. So you can see how smoothing and the curve actually affect the data visually.

