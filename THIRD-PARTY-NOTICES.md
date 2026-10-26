# Third-party notices

PixelPeeker itself is licensed under the GNU General Public License v3.0 or later (GPL-3.0-or-later). Copyright 2026 David Nagel. The full text is in `LICENSE`.

The portable Windows application includes or depends on the following third-party software. The listed package versions are the versions restored for this release. License texts and Microsoft's accompanying third-party notices are in the `licenses` directory. Each component below keeps its own license.

| Component | Version | License | Copyright / source |
| --- | --- | --- | --- |
| .NET Runtime and Windows Desktop Runtime | 10.0.13 | MIT; see the bundled .NET third-party notices for additional components | .NET Foundation and contributors; [dotnet/runtime](https://github.com/dotnet/runtime) |
| CommunityToolkit.Mvvm | 8.4.2 | MIT | .NET Foundation and contributors; [CommunityToolkit](https://github.com/CommunityToolkit/dotnet) |
| Microsoft.Data.Sqlite and Microsoft.Data.Sqlite.Core | 10.0.12 | MIT | .NET Foundation and contributors; [dotnet/efcore](https://github.com/dotnet/efcore) |
| Microsoft.Extensions.DependencyInjection and Abstractions | 10.0.0 | MIT | .NET Foundation and contributors; [dotnet/runtime](https://github.com/dotnet/runtime) |
| SharpCompress | 0.50.5 | MIT | Copyright (c) 2025 Adam Hathcock; [SharpCompress](https://github.com/adamhathcock/sharpcompress) |
| SQLitePCLRaw bundle, core, provider and e_sqlite3 native library | 2.1.12 | Apache-2.0 | Copyright 2014-2024 SourceGear, LLC; [SQLitePCL.raw](https://github.com/ericsink/SQLitePCL.raw) |
| LibVLCSharp and LibVLCSharp.WPF | 3.10.2 | LGPL-2.1-or-later | Copyright VideoLAN and contributors; [LibVLCSharp source](https://code.videolan.org/videolan/LibVLCSharp/-/tree/3.10.2) |
| VideoLAN.LibVLC.Windows (Windows x64 native playback engine and plugins) | 3.0.24 | LGPL-2.1-or-later for LibVLC; individual plugins and dependencies retain their own licenses, including GPL-2.0-or-later | Copyright VideoLAN and respective authors; [VLC source archive](https://get.videolan.org/vlc/3.0.24/vlc-3.0.24.tar.xz), [NuGet packaging source](https://code.videolan.org/videolan/libvlc-nuget) |

SQLite's database engine source is in the public domain; the SQLitePCLRaw packages, including their packaged `e_sqlite3` binaries, declare Apache-2.0 in their NuGet package metadata. Both notices are retained here.

The runtime MIT license and its bundled third-party notices come from the exact .NET 10.0.13 Windows x64 runtime packs used by this publish. The runtime notice file is byte-for-byte identical to the notice shipped in the `Microsoft.Extensions.DependencyInjection` 10.0.0 NuGet package. NuGet marks the `Microsoft.Data.Sqlite`, `Microsoft.Extensions.DependencyInjection`, and CommunityToolkit packages as MIT. The test-only xUnit packages and build-time linker tasks are not part of the desktop publish output.

The playback components are unmodified official VideoLAN NuGet binaries. LGPL/GPL license texts are retained in `licenses`. Native libraries are dynamically loaded. The standalone EXE extracts its bundled files into the .NET bundle cache; a compatible `libvlc/win-x64` directory beside the EXE takes precedence, so users can replace the native library and plugins. The application's buildable source and package references are in the accompanying project. Modification and reverse engineering for debugging changes to these libraries are permitted. See `licenses/VIDEOLAN-SOURCES.md` for source and build references.

FFmpeg's command-line executables are downloaded separately by the application and are not included in this portable folder. LibVLC includes its own decoding dependencies used for playback. See [README.md](README.md#ffmpeg-source-and-licence) for the encoding toolchain's source and licensing notes.

The Avalonia shell adds LibVLCSharp 3.10.2 (LGPL-2.1-or-later, same source commit as the Windows wrapper) and does not add LibVLCSharp.Avalonia, because that package depends on Avalonia 11. It does not bundle libvlc, libvlccore or VLC plugins. On Linux it dynamically loads the system libvlc, for example Ubuntu 24.04 `libvlc5` 3.0.20 and `vlc-plugin-base`. Those distro packages keep their own licenses, including GPL plugins. Corresponding VLC source for the system library is the distribution source package and the upstream archive for that version, listed in `licenses/VIDEOLAN-SOURCES.md`. Linux FFmpeg builds are also downloaded rather than bundled; the GPL builds and their source links are in the README section above.
