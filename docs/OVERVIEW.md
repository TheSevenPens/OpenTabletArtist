# Driver UX Experiment — Overview

## What Is This?

A UX experiment exploring ideas in simplifying the experience for tablet drivers. Currently interfacing with [OpenTabletDriver](https://github.com/OpenTabletDriver/OpenTabletDriver) (OTD), an open-source, cross-platform drawing tablet driver. The prototype explores what a next-generation configuration experience could look like — one that prioritizes visual beauty, clarity, and delight alongside functional completeness.

This is not a fork of OpenTabletDriver. It is a standalone desktop app (Avalonia UI, .NET 10) that connects to the existing OTD daemon process via named pipe and controls it remotely.

## Why Does This Exist?

OpenTabletDriver's current UI is functional but utilitarian. The OTD team is actively building a new UX using Avalonia. This prototype exists to explore an alternative direction — one rooted in modern web aesthetics — and to demonstrate what's possible when beauty is treated as a first-class requirement.

Specific goals:

- **Demonstrate a premium visual experience** for tablet driver configuration, using glassmorphism, smooth transitions, and a refined dark/light theme system.
- **Iterate rapidly** on UI ideas. Originally built with Svelte + Vite, then rebuilt as WPF, now converted to Avalonia UI for cross-platform potential.
- **Validate the architecture** of a standalone app communicating with the OTD daemon, proving that the daemon's JSON-RPC interface is flexible enough to support diverse UI approaches.
- **Serve as a conversation piece** — a tangible artifact that the OTD community and team can react to, critique, and draw inspiration from.

## What This Is Not

- **Not a production-ready replacement.** This is a prototype. It does not implement every OTD feature, does not handle every edge case, and is not optimized for distribution.
- **Not a criticism of the existing UX.** The current OTD interface serves its users well. This project explores a different aesthetic direction, not a correction.
- **Not a permanent fork.** The goal is to produce ideas and demonstrate possibilities, not to maintain a parallel codebase indefinitely.

## Design Philosophy

1. **Beauty is the feature.** Every panel, every transition, every color choice is intentional. The UI should feel like a premium creative tool — because the people using it are creators.
2. **Glass and light.** The glassmorphism design language (frosted glass panels, depth through blur, subtle borders) creates a sense of layered depth without visual heaviness.
3. **Respect for both modes.** Dark and light themes are first-class citizens, each carefully tuned — not an afterthought toggle.
4. **Fast feedback loops.** Avalonia with XAML Hot Reload enables rapid visual iteration.

## Target Audience

The primary audience is **creatives** — digital artists, illustrators, and designers who use drawing tablets as their main input device. These users care about pressure sensitivity, tilt, and a configuration experience that doesn't feel like a developer tool. They are not gamers (osu! players) or technical power users — they want things to work without reading a terminal.

Today, setting up OTD for creative work on Windows is an involved process: installing vmulti, adding the Windows Ink plugin, switching output modes, configuring the drawing app. See the [SevenPens OTD Windows install guide](https://docs.sevenpens.com/drawtab/guides/drivers/opentabletdriver/otd-windows-install) for what this currently looks like. This prototype aims to make that experience dramatically simpler.

Secondary audiences:
- OTD contributors and maintainers evaluating UX directions
- UX designers exploring driver/configuration UI patterns
- Developers interested in the architecture of bridging web UIs to native daemon processes
