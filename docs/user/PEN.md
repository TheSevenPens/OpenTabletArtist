# Pen

The Pen page has three tabs.

## basics

- Check that the pen tip is working normally, and turn it off with **Disable pen tip** if you want. The tip has a section of its own.
- Check that the eraser is working normally. The eraser has its own section too.
- Choose how the pen moves (like a drawing tablet, or like a mouse) and how much its position is smoothed.
- Choose how the pen presses: normally, with pressure and tilt, or like a mouse. While it presses like a mouse the **Disable tilt** switch here and the **Disable pressure sensitivity** switch on the pressure tab are greyed out, because a mouse takes neither.
- Turn tilt off.

## pressure

- Turn pressure sensitivity off with **Disable pressure sensitivity**, at the top of the tab, next to the curve it overrides.
- Adjust the pressure curve and pressure smoothing.
- Test them with a drawing preview and live readouts.

## buttons

This tab only appears for a pen that has barrel buttons. A picture of the pen sits across the top with its buttons numbered from the tip, and each button has a card underneath. When the window is wide enough the cards sit side by side, with a line from each button on the pen to its card; in a narrow window they stack and the lines go. Each card shows three choices side by side, with the current one highlighted:

- **Default action**: the pen's own button. In Windows Ink mode this is the Barrel Switch (right-click / context menu), and your drawing software decides what it does through its own stylus settings. All the barrel buttons share this one action, so a card tells you when two buttons do the same thing.
- **Keyboard shortcut**: opens an editor where you press **Record** and then the shortcut on your keyboard, or pick the keys on screen. The card then shows the shortcut as keys, with a **Change…** button beside it. The shortcut is held down for as long as you hold the pen button, so a modifier on its own (for example **Alt** for the eyedropper) works too. If you close the editor without saving, nothing changes.
- **Disabled**: the button does nothing.

If a button was set up somewhere else, such as in OpenTabletDriver's own window, the card says what it does now, with none of the three choices highlighted, and leaves it alone until you choose something. In Windows Ink mode a mouse click or scroll on a button gets a warning, because a click can interrupt your stroke and scrolling doesn't work. The same warning appears on ExpressKeys and wheel buttons. OpenTabletArtist never changes a button for you.
