# Product Requirements Document (PRD) & User Stories
## Burudani (Beta v1.0.0) — Cross-Platform Front-End & Media Streaming Architecture Showcase

| Field | Details |
| :--- | :--- |
| **Product Name** | Burudani (Beta) |
| **Version** | `v1.0.0` (Beta) |
| **Platforms** | Windows 10/11 (`net10.0-windows10.0.19041.0` / WinUI 3), Android 8.0+ (`net10.0-android` / API 26–35) |
| **Framework** | .NET 10 MAUI (`CommunityToolkit.Mvvm`, `CommunityToolkit.Maui.MediaElement`) |
| **License** | GNU General Public License v3.0 (GPL-3.0) — Educational & Demonstration Purposes Only |

---

## 1. Executive Summary & Educational Purpose

**Burudani** (Swahili for *entertainment* / *recreation*) is an open-source **educational reference application** built with **.NET 10 MAUI**. It demonstrates how to architect a responsive, gesture-driven, cross-platform media client that shares business logic across desktop (WinUI 3) and mobile (Android) while tailoring navigation, rendering, and background services to each platform's native idioms.

> [!NOTE]
> **Educational Disclaimer:** Burudani is provided strictly for educational and architectural demonstration purposes (API consumption, responsive UI design, virtualization, and media metadata serialization). Any use of the application is at the sole discretion and responsibility of the end user.

---

## 2. Product Goals & Non-Goals

### Goals
- **Cross-Platform Code Sharing with Native Polish:** Share ViewModels, services, and models across Windows and Android while adapting navigation chrome (custom Windows TitleBar vs. Android Material bottom navigation + swipeable bottom sheet).
- **60fps Virtualized Lists on Mobile & Desktop:** Deliver smooth scrolling on Android `RecyclerView` and Windows `ListView`/`GridView` via native bitmap caching, incremental pagination, compiled XAML bindings, and flat card templates.
- **Responsive Media Player Experience:** Provide persistent playback controls everywhere (global floating mini-player on Windows, interactive expandable bottom sheet on Android, and a global floating circular Playlists button on both platforms).
- **Demonstrate Real-World Pipelines:** Showcase debounced search autocomplete, multi-source catalog querying, two-tier artwork caching, ID3v2/MP4 tag embedding, and Android foreground media/notification services.

### Non-Goals
- User accounts, cloud sync, or proprietary DRM streaming.
- Guaranteed stability during Beta (explicitly marked `Beta` across UI headers, project metadata, and releases).

---

## 3. System Architecture Overview

```
Burudani.Maui (Shared Core + WinUI 3 Target)
├── Views/          DiscoveryPage, SearchPage, DownloadsPage, PlayerPage, SettingsPage
├── Controls/       ArtworkView, MiniPlayerBar, LottieLoaderView
├── ViewModels/     DiscoveryViewModel, SearchViewModel, DownloadsViewModel, PlayerViewModel, SettingsViewModel
├── Services/       ArtworkService, PlayerService, DownloadService, SearchService, StorageService, RegionService, ThemeService, CustomDialogService
├── Helpers/        ObservableRangeCollection<T>, MaterialIcon, KeyboardHelper
└── Platforms/
    ├── Windows/    Extended WinUI 3 TitleBar & desktop windowing
    └── Android/    BurudaniMediaService (MediaSessionCompat + ic_notification), BurudaniDownloadService

Burudani.Android (Dedicated Android Head Project)
└── Links shared C#/XAML from Burudani.Maui and targets net10.0-android (API 26–35)
```

---

## 4. Functional Requirements

### 4.1 Discovery (`DiscoveryPage` / `DiscoveryViewModel`)
- **FR-1.1 Region Selection:** Users can select a discovery region (12 supported countries including US, GB, KE, NG, ZA, TZ, UG, IN, FR, DE, BR, JP) via a modal picker.
- **FR-1.2 Genre Filtering:** Horizontal scrollable genre pills (`All Genres`, `Pop`, `Hip-Hop`, `R&B`, `Afrobeats`, `Rock`, `Electronic`, `Reggae`, `Country`, `Jazz`, `Latin`, `Acoustic`, `Metal`).
- **FR-1.3 Incremental Pagination:** Tracks load in batches of 20 using `ObservableRangeCollection<TrackItem>` and `RemainingItemsThreshold="6"`.
- **FR-1.4 Pull-to-Refresh & Offline Handling:** `RefreshView` supports pull-to-refresh on Android and a Refresh button on Windows; offline state displays a clear "No Internet Connection" view.

### 4.2 Search & Catalogs (`SearchPage` / `SearchViewModel`)
- **FR-2.1 Multi-Source & Region Filtering:** Users can search across `All Sources`, `Apple Music / iTunes`, `Deezer`, `YouTube Music`, and **Spotify** (for a Spotify-style streaming & discovery experience), or paste a direct URL, filtered by region.
- **FR-2.2 Live Autocomplete:** Typing 2+ characters fetches up to 6 autocomplete suggestions displayed in a dropdown; tapping a suggestion executes the search and hides the keyboard.
- **FR-2.3 View Modes:** Switch between **Songs (Grid View)**, **Songs (List View)**, and **Albums (Grid View)** using a single virtualized `SongsCollectionView` (swapping `ItemsLayout` and `DataTemplate`) and a dedicated Albums `CollectionView`.
- **FR-2.4 Album Inspection / Queueing:** Users can queue an entire album's tracks after a confirmation prompt.

### 4.3 Media Playback & Player UI (`PlayerPage`, `MiniPlayerBar`, `PlayerService`)
- **FR-3.1 Global Transport Controls:** Play/Pause, Next, Previous, Repeat mode, and Shuffle/Queue management.
- **FR-3.2 Interactive Seek Bar:** Both the full player and the Windows global mini-player bar support drag/click-to-seek synchronized via a 250ms position heartbeat.
- **FR-3.3 Platform-Adaptive Player Surface:**
  - **Windows:** Persistent floating `MiniPlayerBar` at the bottom + dedicated `Music Player` navigation pill.
  - **Android:** Expandable swipeable bottom sheet that morphs between a collapsed mini-bar and full player, backed by `BurudaniMediaService` (`MediaSessionCompat` lockscreen/notification controls using `ic_notification`).
- **FR-3.4 Play History & Up Next Queue:** View and jump to tracks in the active queue or play history.

### 4.4 Playlists & Global Floating Action Button (`MainPage`, `PlayerViewModel`, `StorageService`)
- **FR-4.1 Global Floating Playlist Button:** A 52x52 circular orange FAB (`#ea580c`) visible across both Windows and Android for instant playlist access.
- **FR-4.2 Playlist CRUD & Playback:** Create, browse, select, and delete custom playlists (plus built-in `Recent Downloads` / `Favorites` views).

### 4.5 Offline Caching / Queue Hub (`DownloadsPage` / `DownloadsViewModel`)
- **FR-5.1 Queue & Progress Tracking:** Displays active, queued, completed, failed, and cancelled items with per-item progress (`0–100%`), speed, ETA, and overall queue progress.
- **FR-5.2 History Search & Management:** Filter historical items in real time by title, artist, or album.
- **FR-5.3 Metadata Serialization:** Demonstrates embedding ID3v2 / MP4 tags and cover artwork via `TagLibSharp` (configurable in Settings).

### 4.6 Settings & Beta Transparency (`SettingsPage` / `SettingsViewModel`)
- **FR-6.1 Appearance & Defaults:** Configure Theme (`Dark Mode`, `Light Mode`, `System Default`), default region, default catalog source, audio bitrate (`128`–`320 kbps`), playback mode, and tag embedding.
- **FR-6.2 Beta & Educational Notice:** Clearly displays the `BETA v1.0.0` badge, educational showcase disclaimer, GPL-3.0 license notice, and FFmpeg/audio engine diagnostics.

---

## 5. Non-Functional Requirements

| Category | Requirement | Implementation |
| :--- | :--- | :--- |
| **Android Scroll Performance** | Smooth 60fps scrolling on Discovery & Search lists | Native `Android.Util.LruCache` (`Rgb565` bitmaps, 8–28 MB), `ImageView.SetImageBitmap` fast path, zero UI-thread disk I/O, `ClipToOutline` GPU rounded corners, fixed card `HeightRequest`, `RecyclerView` (`HasFixedSize=true`, `SetItemViewCacheSize(20)`) |
| **XAML Binding Efficiency** | Zero reflection overhead in list templates | Compiled bindings (`x:DataType`, `x:Reference`, `MauiEnableXamlCBindingWithSourceCompilation`) |
| **Network Resilience** | Bounded concurrent image fetches | 4-slot `SemaphoreSlim`, 6s HTTP timeout, off-screen request cancellation via `HasSubscribers` |
| **Platform Isolation** | Android optimizations must not regress Windows | Android-only pipelines guarded by `#if ANDROID` / `DevicePlatform.Android`; Windows retains `UriImageSource` |

---

## 6. User Stories & Acceptance Criteria

### Epic 1: Music Discovery
- **US-1.1 — Browse Trending Tracks by Region**
  - *As a user*, I want to switch my discovery region so that I can explore trending music charts from different countries.
  - *Acceptance Criteria:* Tapping the region pill opens the region selector; picking a region updates `CurrentRegion` and reloads trending tracks with a loading indicator.
- **US-1.2 — Filter by Genre**
  - *As a user*, I want to tap genre pills at the top of Discovery so that I can narrow trending results to a specific genre.
  - *Acceptance Criteria:* Tapping a genre pill highlights the selected pill, cancels any in-flight request, and loads matching tracks.
- **US-1.3 — Smooth Infinite Scroll**
  - *As an Android user*, I want to scroll rapidly through Discovery cards without stuttering or frozen frames.
  - *Acceptance Criteria:* Scrolling loads cached artwork from native `BitmapLruCache` in 0ms on the UI thread, batch-inserts 20 items at a time via `AddRange`, and avoids layout thrashing.

### Epic 2: Search & Catalog Exploration
- **US-2.1 — Live Autocomplete Search**
  - *As a user*, I want search suggestions to appear as I type so that I can quickly find artists or songs.
  - *Acceptance Criteria:* Typing 2+ characters shows up to 6 suggestions; tapping one fills the query, dismisses the keyboard, and runs the search.
- **US-2.2 — Switch Between Grid, List, and Album Views**
  - *As a user*, I want to toggle between Songs (Grid), Songs (List), and Albums so that I can browse results in my preferred layout.
  - *Acceptance Criteria:* Selecting a layout updates `SongsCollectionView` or `Albums` view cleanly without losing search results.
- **US-2.3 — Filter Search by Catalog Source**
  - *As a user*, I want to choose between All Sources, Apple Music/iTunes, Deezer, YouTube Music, or Spotify so that I can compare catalog results.
  - *Acceptance Criteria:* Changing the source re-runs the current query against the selected catalog.
- **US-2.4 — Stream & Experience with Spotify**
  - *As a user*, I want to stream music and browse tracks with a Spotify-style streaming experience so that I can discover, queue, and listen to tracks seamlessly from Spotify alongside other catalogs.
  - *Acceptance Criteria:* Selecting Spotify as a source or pasting a Spotify track/album/playlist link resolves playable streams, displays artwork and metadata, and plays smoothly in the in-app player.

### Epic 3: Audio Playback & Responsive Player
- **US-3.1 — Instant Track Playback**
  - *As a user*, I want to tap Play on any track card or list row so that playback starts immediately and queues the surrounding tracks.
  - *Acceptance Criteria:* Tapping Play invokes `PlayTrackCommand` and populates the active queue in `PlayerService`.
- **US-3.2 — Seek From Mini-Player (Windows)**
  - *As a Windows user*, I want the global mini-player seek bar to support scrubbing just like the full player.
  - *Acceptance Criteria:* Dragging or clicking the mini-player seek bar updates the playback position smoothly without fighting the 250ms heartbeat timer.
- **US-3.3 — Background Playback & Lockscreen Notification (Android)**
  - *As an Android user*, I want media controls in the notification shade using the custom `ic_notification` icon when the app is backgrounded.
  - *Acceptance Criteria:* `BurudaniMediaService` displays persistent media notification controls with `ic_notification` and responds to Play/Pause/Next/Previous actions.

### Epic 4: Playlists & Global Access
- **US-4.1 — Quick Access Playlists Anywhere**
  - *As a user*, I want a floating circular Playlists button accessible from any screen so that I can manage or play playlists without navigating away first.
  - *Acceptance Criteria:* The 52x52 floating circular button is visible on both Windows and Android and opens playlist management/selection on tap.

### Epic 5: Offline Queue Hub & Tagging
- **US-5.1 — Monitor Active Queue & History**
  - *As a user*, I want to view queued/active items with live progress, speed, and ETA, and search my completed history.
  - *Acceptance Criteria:* `DownloadsPage` updates progress in real time and filters `HistoryItems` as the user types in the history search box.

### Epic 6: Settings & Educational Transparency
- **US-6.1 — Customize Theme, Bitrate & Defaults**
  - *As a user*, I want to switch between Dark, Light, and System themes and set my preferred bitrate and region.
  - *Acceptance Criteria:* Theme changes apply immediately via `ThemeService` and persist via `StorageService`.
- **US-6.2 — Clear Beta & Educational Status**
  - *As a reviewer or contributor*, I want the app's Beta status, educational purpose, and GPL-3.0 license clearly visible in the UI and documentation.
  - *Acceptance Criteria:* `BETA` badges appear in the app headers and Settings/About section alongside the educational disclaimer and GPL-3.0 notice.
