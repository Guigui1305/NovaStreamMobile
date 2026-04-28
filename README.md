# NovaStream Mobile

**NovaStream** is a personal and legal IPTV application for Android, built with .NET MAUI.

## Features

- Multi-profile support (Netflix-style)
- M3U / M3U8 playlist parsing
- Xtream Codes API connection
- LibVLC-powered video player
- Electronic Program Guide (EPG / XMLTV)
- Favorites and watch history
- Subtitle track selection
- Channel search and category filtering
- Zapping (Next / Previous channel)
- Picture-in-Picture mode
- Chromecast support
- Brightness control via gesture
- Local image caching for performance
- Modern Dark UI theme

## How to Get Your APK (via GitHub Actions)

### Step 1: Create a GitHub Repository

Go to [github.com/new](https://github.com/new) and create a new repository (public or private).

### Step 2: Push the Code

Open a terminal and run the following commands:

```bash
cd NovaStreamMobile
git init
git add .
git commit -m "Initial commit - NovaStream Mobile"
git branch -M main
git remote add origin https://github.com/YOUR_USERNAME/YOUR_REPO_NAME.git
git push -u origin main
```

### Step 3: Wait for the Build

1. Go to your repository on GitHub.
2. Click on the **"Actions"** tab.
3. You will see the workflow **"Build NovaStream Android APK"** running automatically.
4. Wait for the green checkmark (approximately 5-10 minutes).

### Step 4: Download Your APK

1. Click on the completed workflow run.
2. Scroll down to the **"Artifacts"** section.
3. Click on **"NovaStream-Android-APK"** to download the ZIP file.
4. Extract the ZIP to find your `.apk` file.

### Step 5: Install on Your Phone

1. Transfer the `.apk` file to your Android phone.
2. Open the file on your phone.
3. If prompted, allow installation from unknown sources.
4. Tap **"Install"** and enjoy NovaStream!

## Manual Build (Optional)

If you prefer to build locally:

```bash
# Install prerequisites
dotnet workload install maui-android

# Build the APK
dotnet publish NovaStreamMobile.csproj -f net9.0-android -c Release -p:AndroidPackageFormat=apk
```

The APK will be located in `bin/Release/net9.0-android/publish/`.

## Creating a Release with APK

To automatically create a GitHub Release with the APK attached:

```bash
git tag v1.0.0
git push origin v1.0.0
```

The workflow will detect the tag and create a release with the APK downloadable directly from the Releases page.

## Tech Stack

- .NET 9 / .NET MAUI
- C# / XAML
- MVVM Architecture
- LibVLCSharp (Video Playback)
- JSON Local Storage

## Legal Notice

NovaStream is a personal IPTV player. It does not include, distribute, or provide access to any IPTV content. Users are solely responsible for ensuring they have the legal rights to access any streams they configure in the application.

## License

This project is for personal use only.
