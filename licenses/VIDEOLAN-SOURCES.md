# VideoLAN playback components

This application uses unmodified LibVLCSharp / LibVLCSharp.WPF 3.10.2 and the Windows x64 files from VideoLAN.LibVLC.Windows 3.0.24. Copyright VideoLAN, the LibVLCSharp authors and respective component contributors. LibVLC and its managed wrapper declare LGPL-2.1-or-later; plugins and bundled dependencies retain their original license terms. Both LGPL 2.1 and GPL 2 texts are included in this directory.

Upstream source, package and build references:

- https://code.videolan.org/videolan/LibVLCSharp (release 3.10.2, package source commit 3082c09ca955131c705fc4767fde03e9c4a14999; LGPL license retained as LIBVLCSHARP-LICENSE.txt)
- https://github.com/videolan/libvlcsharp (official source mirror)
- https://get.videolan.org/vlc/3.0.24/vlc-3.0.24.tar.xz (VLC/LibVLC 3.0.24 sources, AUTHORS, COPYING, COPYING.LIB, module notices, contrib source recipes and build scripts)
- https://code.videolan.org/videolan/libvlc-nuget (official NuGet packaging scripts)
- https://downloads.videolan.org/pub/videolan/contrib/ (dependency source archives referenced by VLC's contrib recipes)
- https://www.nuget.org/packages/LibVLCSharp/3.10.2
- https://www.nuget.org/packages/LibVLCSharp.WPF/3.10.2
- https://www.nuget.org/packages/VideoLAN.LibVLC.Windows/3.0.24
- https://www.nuget.org/packages/LibVLCSharp/3.10.2 (also referenced by the Avalonia shell)

Native libraries remain dynamically linked. To substitute compatible modified native libraries on Windows, place libvlc.dll, libvlccore.dll and the plugins directory in libvlc/win-x64 beside PixelPeeker.exe; that directory takes precedence over the extracted bundled copy. The Avalonia shell does not bundle a native libvlc. On Linux it loads the system library (Ubuntu package `libvlc5`, currently 3.0.20, whose source package is `vlc` and whose upstream archive is https://get.videolan.org/vlc/3.0.20/vlc-3.0.20.tar.xz). LibVLCSharp.Avalonia is not used because it depends on Avalonia 11. A source checkout can also restore or substitute the managed library packages and rebuild the application. Modification and reverse engineering for debugging changes to these libraries are permitted.
