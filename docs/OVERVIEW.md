# Introduction to OpenTabletArtist (OTA)

OTA is an alternative user experience for OpenTabletDriver (OTD) that prioritizes the needs of artists: to get set up quickly, in a familiar environment, so they can be productive with their creative apps.

OTA is currently Windows-only.

## How OTA Works

OTD has two components. The first is the **OTD daemon**, which is the actual driver — the component that talks to the tablet and communicates with the operating system. The second is a separate **OTD user interface** that talks to the daemon; this interface lets the user configure the daemon as needed.

OpenTabletArtist is simply *another* user interface that talks to the very same OTD daemon. In this way, OTA is not a true driver; it is just an experience layer — one that is optimized for artists.

## Key Benefits of OTA

- The user interface is highly optimized for artists.
- It is much more similar to the traditional drawing-tablet driver interfaces you are used to from Wacom or XP-Pen.
- It guides you to the correct recommended configuration for artists, and if a configuration isn't right, it will tell you how to fix it — often with a single click.
- It contains many enhancements that simplify your typical workflows — for example, when you are configuring ExpressKeys or the tablet wheel.
- It speaks in a language that is less technical and more natural for an artist.
- It detects if you are missing components and lets you download and install them automatically. With OpenTabletDriver, this is something you have to discover through documentation, and the steps to install are completely manual.
- And finally, it is just very pretty. OTA starts with a theme called **Sakura**, which is quite beautiful, but you can switch to other, more standard-looking themes if you prefer.

## Relationship to OpenTabletDriver (OTD)

The OTA project is an independent, personal testing tool. **No code is currently being contributed back to the official OTD project.**

While the work done here is intended to serve as inspiration for OTD developers — particularly concerning user-experience improvements — there is **no expectation** that any features from the OTA project will be integrated into OpenTabletDriver.

## Who Should Use OpenTabletArtist? (Target Audience)

**1. Happy current users of proprietary drivers (Wacom, Huion, XP-Pen, etc.)**

If you are an artist currently using a proprietary driver and are satisfied with its performance, you likely do not need to switch to OpenTabletArtist.

**2. Owners of very old or abandoned tablets (recommended)**

If your tablet is old enough that its manufacturer no longer ships working drivers for modern systems, OpenTabletDriver can often still run it — and OpenTabletArtist gives you a much simpler, more streamlined way to set it up than the full OpenTabletDriver interface.

**3. Current OTD artists (recommended for testing)**

If you are an artist currently using OpenTabletDriver (OTD), even if you are satisfied, we encourage you to experiment with OpenTabletArtist. You may find the user experience significantly improved and better suited to your workflow.

**4. osu! players (not recommended)**

OpenTabletArtist focuses on artists and is designed for simplicity, so it **lacks the comprehensive power and advanced configuration** needed to master every technical aspect of OTD for high-level competitive play — many of the features serious osu! players rely on are intentionally absent. For osu!, use OpenTabletDriver directly.

## Community Involvement and Feedback

- **Feedback:** Submit bug reports and suggestions directly to the GitHub repository.
- **Documentation:** See the repository's docs (including the user manual) for more detail.
- **Community:** Join the drawing-tablet Discord for direct conversation with the developers and community.

---

In summary, OpenTabletArtist represents a significant leap forward in user-interface design compared to OTD — making it more visually appealing, better structured, and more user-friendly for digital artists — while inviting the community to participate in its development.
