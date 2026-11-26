# Burudani (Beta)
### Cross-Platform Front-End & Media Streaming Architecture Showcase

[![Platform](https://img.shields.io/badge/Platforms-Windows%20%7C%20Android-ea580c?style=flat-square)](#supported-platforms)
[![Framework](https://img.shields.io/badge/.NET-10.0%20MAUI-512bd4?style=flat-square)](#technology-stack)
[![Status](https://img.shields.io/badge/Status-Beta%20v1.0.0-f59e0b?style=flat-square)](#project-overview)
[![License](https://img.shields.io/badge/License-GPL--3.0-blue?style=flat-square)](#license--disclaimer)

---

## Educational Purpose & Disclaimer

> [!IMPORTANT]
> **Educational & Demonstration Software**:
> **Burudani is an open-source educational reference application.** It was built strictly to demonstrate modern software engineering concepts, including:
> - Responsive, gesture-driven cross-platform front-end architectures using **.NET 10 MAUI** and **WinUI 3**.
> - Asynchronous audio stream processing, HTTP chunked streaming, and web media API integration.
> - ID3v2 and MPEG-4 media atom embedding and audio tag serialization.
> - High-performance list and grid UI virtualization running at 60fps.
>
> **Notice**: The software is provided "as is", without warranty of any kind. Any use of this application, and whatever the end-user intends to use it for, is entirely up to the discretion and sole responsibility of the user.

---

## Project Overview

**Burudani** (Swahili for *entertainment* / *recreation*) is a showcase application illustrating how to build fluid, high-performance client applications for both desktop (**Windows 10/11**) and mobile (**Android 8.0+**). 

The project solves several complex real-world front-end and multimedia challenges:
- **Zero-Overlap Responsive Viewports**: Adapting complex media layouts dynamically between ultra-wide desktop monitors and narrow mobile screens.
- **YouTube Music-Style Bottom Sheet & Morphing Player**: Fluid tactile bottom sheet transitions from a headless collapsed mini-bar into a full dome audio player using gesture recognizers and pan interpolation.
- **Universal Artwork Pipeline**: Extracting embedded ID3v2/MP4 cover art bytes for offline playback as well as streaming high-resolution web artwork, coupled with in-memory RAM caching and Glide/Coil bitmap decoding.
- **Non-Blocking Background Media Service**: Managing Android `MediaSessionCompat`, lockscreen notification actions, and audio focus alongside an asynchronous UI heartbeat seekbar sync loop.
- **Multi-Catalog & Spotify Streaming Experience**: Unified browsing and streaming across public music catalogs—including a **Spotify-style streaming & discovery experience** alongside Apple Music/iTunes, Deezer, and YouTube Music.

> For full product specifications, functional requirements, and user stories, see the **[Product Requirements Document (PRD.md)](ProductRequirementsDocument.md)**.

---

## Architecture & Key Design Patterns

```
┌───────────────────────────────────────────────────────────────┐
│                       Burudani UI (MAUI)                      │
│  MainPage (Responsive App Bar, Windows TitleBar, Sheet Host)  │
├───────────────┬───────────────────────────────┬───────────────┤
│ DiscoveryView │          SearchView           │ DownloadsView │
├───────────────┴───────────────────────────────┴───────────────┤
│   Global Floating Playlist FAB  │  Global Mini Player Bar    │
├───────────────────────────────────────────────────────────────┤
│                  Core Services & ViewModels                   │
│   • PlayerService (State Engine, Position Heartbeat, Queue)   │
│   • DownloadService (Queue, yt-dlp, FFmpeg, TagLib Metadata)  │
│   • ArtworkService (Memory Cache, Local ID3/MP4 Art Decoder)  │
│   • StorageService (Settings, Playlists, Play History)        │
├───────────────────────────────┬───────────────────────────────┤
│       Windows (WinUI 3)       │       Android Platform        │
│   • Extended Desktop TitleBar │   • BurudaniMediaService      │
│   • WinUI MediaPlayerElement  │   • ExoPlayer / MediaElement  │
│   • Click-to-Seek Slider Track│   • Fluid Sliding Bottom Sheet│
└───────────────────────────────┴───────────────────────────────┘
```

### 1. Responsive Front-End Architecture
- **Windows Desktop**: Features a borderless, custom-templated native title bar with smooth window drag regions, quick-navigation pills, a persistent bottom mini-player bar with precise scrub-and-seek slider controls, and full queue/playlist split views.
- **Android Mobile**: Implements an uncrowded mobile app bar, a Material 3 bottom navigation bar, and an interactive swipeable bottom sheet that collapses into a 72dp mini-bar and expands into an arched dome player with reorderable queue management.
- **Global Floating Action Button (FAB)**: A standalone, 52x52 circular button floating above the interface on both platforms, providing instant access to playlist creation, management, and playback from anywhere in the application.

### 2. High-Performance Virtualization (60fps)
- Enforces MAUI `ItemSizingStrategy="MeasureFirstItem"` on incremental collections.
- Completely decouples scrolling from virtualization by avoiding nested `ScrollView` wrappers around `CollectionView` elements.
- Uses lightweight rounded `Border` shapes with tap gesture recognizers instead of heavy native button controls over artwork grids.
- Bypasses file I/O checks (`File.Exists`) for remote HTTP/HTTPS URLs and debounces redundant property changes during cell recycling.

### 3. Media & API Pipeline Demonstration
- **API Integration**: Showcases debounced live querying against web music search APIs (including Spotify-style catalog streaming, Apple Music/iTunes, Deezer, and YouTube Music) and stream endpoint resolution.
- **Metadata Embedding**: Demonstrates how to write standard ID3v2.3 tags for `.mp3` containers and native atom headers for `.m4a` containers using `TagLibSharp`.
- **Seek Synchronization**: Implements a 250ms dispatcher heartbeat timer to ensure continuous timestamp tracking and progress fill synchronization across platforms without freezing during backgrounding.

---

## Windows App Screenshots & Page Walkthrough

> [!TIP]
> Follow screenshots to get a sense of how the application runs on Windows. The reason the Android screenshots may not be available for now is that Android is still under critical development and the release version of Android is highly unstable.

### 1. Discovery Page
<img width="1912" height="1012" alt="Screenshot 2026-11-26 101451" src="https://github.com/user-attachments/assets/2408195d-b491-42ba-a0df-ef0c49f2cf9a" />

- **Region & Genre Filtering:** Switch between 12 regional charts and 13 genre pills (`All Genres`, `Pop`, `Hip-Hop`, `R&B`, `Afrobeats`, `Rock`, `Electronic`, and more).
- **4-Column Responsive Track Grid:** Displays artwork cards with hover/overlay Play and Queue buttons, duration badges, and incremental pagination.
- **Offline Awareness:** Automatically detects connectivity loss and provides a one-click Refresh action.

### 2. Search & Catalog Page
<img width="1908" height="1016" alt="Screenshot 2026-11-26 101333" src="https://github.com/user-attachments/assets/d62d89ea-c493-462a-a0d8-1f5e955bcb62" />

- **Live Autocomplete:** Typing 2+ characters fetches instant query suggestions in a dropdown.
- **Multi-Catalog & Spotify Experience:** Filter searches across **All Sources**, **Spotify**, **Apple Music / iTunes**, **Deezer**, and **YouTube Music**, or paste direct media links.
- **Flexible Layouts:** Switch seamlessly between **Songs (Grid View)**, **Songs (List View)**, and **Albums (Grid View)**.

### 3. Music Player, Mini-Player & Playlists
<img width="1912" height="1012" alt="Screenshot 2026-11-26 101451" src="https://github.com/user-attachments/assets/055ac90b-b546-4e7e-9c81-d023598c8f77" />

- **Persistent Global Mini-Player Bar:** Docked across the bottom on Windows with track artwork, transport controls, and a full click-and-drag **interactive seek bar** matching the main player.
- **Full Player & Queue Management:** Features large artwork display, Play/Pause, Next/Previous, Repeat modes, volume control, **Up Next** queue, **Playlists**, and **Play History** tabs.
- **Global Floating Playlist Button:** A circular floating action button (`#ea580c`) accessible from every page for instant playlist creation and playback.

### 4. Queue & Offline Hub (Downloads Hub)
<img width="1917" height="1013" alt="Screenshot 2026-11-26 101415" src="https://github.com/user-attachments/assets/66a02ecd-eded-47d6-9b66-fa0c3e35389e" />

- **Live Queue Monitoring:** Displays overall progress, transfer speed, and per-item status (`Queued`, `Downloading`, `Converting`, `Completed`, `Failed`).
- **Searchable History:** Filter past items in real time by title, artist, or album.

### 5. Settings & About (`Beta v1.0.0`)
<img width="1917" height="1015" alt="image" src="https://github.com/user-attachments/assets/9df84e68-7e84-48be-ab38-12977ca85df1" />

- **Personalization:** Switch between **Dark Mode**, **Light Mode**, and **System Default**, and configure default region, catalog source, audio bitrate (`128`–`320 kbps`), playback mode, and ID3/MP4 tag embedding.
- **Diagnostics & Transparency:** Shows FFmpeg engine status, `BETA v1.0.0` build badge, educational disclaimer, and GPL-3.0 license details.

---

## Technology Stack

| Component | Technology | Purpose |
| :--- | :--- | :--- |
| **Framework** | .NET 10.0 MAUI | Multi-platform App UI |
| **Language** | C# 13 | Core application code |
| **Desktop Target** | Windows App SDK / WinUI 3 | Fluent desktop interface |
| **Mobile Target** | Android 8.0+ (API 26 to 35) | Native mobile experience |
| **Media Engine** | CommunityToolkit.Maui.MediaElement | Cross-platform audio renderer |
| **MVVM Pattern** | CommunityToolkit.Mvvm | Clean reactive data-binding |
| **Tagging** | TagLibSharp | Audio container metadata serialization |
| **Stream Extraction** | YoutubeExplode | Demonstration media stream resolver |

---

## Building & Running the Project

### Prerequisites
1. **.NET 10 SDK** (with MAUI workloads installed):
   ```bash
   dotnet workload install maui
   dotnet workload install maui-windows
   dotnet workload install maui-android
   ```
2. **Visual Studio 2026** (or VS Code with .NET MAUI extension).
3. **Android SDK** (API Level 26 or newer for Android builds).

### Windows Desktop Build
Run from the repository root:
```powershell
dotnet build "Burudani.Maui\Burudani.csproj" -f net10.0-windows10.0.19041.0
dotnet run --project "Burudani.Maui\Burudani.csproj" -f net10.0-windows10.0.19041.0
```

### Android Build
Run from the repository root:
```powershell
dotnet build "Burudani.Android\Burudani.Android.csproj" -f net10.0-android
```
To install and launch on a connected device or running emulator:
```powershell
dotnet run --project "Burudani.Android\Burudani.Android.csproj" -f net10.0-android
```

---

## License & Disclaimer

This project is licensed under the **GNU General Public License v3.0 (GPL-3.0)** with additional educational disclaimers. See the [LICENSE](LICENSE) file for the full text.

```text
Burudani — Educational Cross-Platform Showcase
Copyright (C) 2024-2026 The Burudani Project Contributors

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
GNU General Public License for more details.
```
