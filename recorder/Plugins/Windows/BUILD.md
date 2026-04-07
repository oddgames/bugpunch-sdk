# Building ODDRecorder.dll

Native Windows plugin for hardware-accelerated H.264 video encoding via Media Foundation.

## Prerequisites

- **Visual Studio 2019 or later** (Community/Professional/Enterprise) with the "Desktop development with C++" workload
- **Windows 10 SDK** (10.0.19041.0 or later) -- provides Media Foundation headers and libs
- **Target**: x64 only (Unity no longer supports 32-bit Windows builds)

## Required Libraries

The following libraries are linked automatically via `#pragma comment(lib, ...)` in the source:

| Library            | Purpose                                      |
|--------------------|----------------------------------------------|
| `mfreadwrite.lib`  | IMFSinkWriter (read/write pipeline)          |
| `mfplat.lib`       | MFStartup, MFCreateMemoryBuffer, MFCreateSample |
| `mfuuid.lib`       | Media Foundation GUIDs                       |
| `mf.lib`           | Core Media Foundation                        |
| `d3d11.lib`        | D3D11 device and texture operations          |

These are all part of the Windows SDK. No additional downloads required.

## Required Headers

All headers are part of the Windows SDK:

- `<mfapi.h>` -- MFStartup, MFCreateMediaType, attribute helpers
- `<mfidl.h>` -- IMFMediaType, media type GUIDs
- `<mfreadwrite.h>` -- IMFSinkWriter, MFCreateSinkWriterFromURL
- `<mferror.h>` -- MF error codes
- `<d3d11.h>` -- ID3D11Device, ID3D11Texture2D, etc.
- `<wrl/client.h>` -- Microsoft::WRL::ComPtr smart pointers

## Build Option A: Visual Studio Project

### Create a new project

1. File > New > Project > "Dynamic-Link Library (DLL)" for C++
2. Set the project name to `ODDRecorder`
3. Set the platform to **x64** (remove Win32/ARM configurations)
4. Set Configuration Type to **Dynamic Library (.dll)**

### Configure project settings

Under Project Properties (Configuration: Release, Platform: x64):

**General:**
- Configuration Type: `Dynamic Library (.dll)`
- Windows SDK Version: `10.0` (latest installed)
- Platform Toolset: `v143` (VS 2022) or `v142` (VS 2019)
- C++ Language Standard: `ISO C++17 Standard (/std:c++17)`

**C/C++ > General:**
- Additional Include Directories: (none needed beyond Windows SDK defaults)

**C/C++ > Preprocessor:**
- Preprocessor Definitions: `WIN32_LEAN_AND_MEAN;NOMINMAX;NDEBUG;_WINDOWS;_USRDLL`

**C/C++ > Code Generation:**
- Runtime Library: `Multi-threaded DLL (/MD)` (Release) or `Multi-threaded Debug DLL (/MDd)` (Debug)

**Linker > General:**
- Output File: `$(OutDir)ODDRecorder.dll`

**Linker > Input:**
- Libraries are auto-linked via `#pragma comment(lib, ...)` in the source, but you can also add them here explicitly if preferred: `mfreadwrite.lib;mfplat.lib;mfuuid.lib;mf.lib;d3d11.lib`

### Add source files

- Add `ODDRecorder.cpp` and `ODDRecorder.h` to the project

### Build

1. Set configuration to **Release | x64**
2. Build > Build Solution
3. Output DLL will be at `x64\Release\ODDRecorder.dll`

### Copy to Unity

Copy the built DLL to the Unity plugin location:

```
recorder\Plugins\Windows\x86_64\ODDRecorder.dll
```

## Build Option B: Command Line (Developer Command Prompt)

Open a **x64 Native Tools Command Prompt for VS 2022** (or 2019) and run:

```bat
cd c:\Workspaces\odddev\sdk\recorder\Plugins\Windows

cl /LD /EHsc /std:c++17 /O2 /DWIN32_LEAN_AND_MEAN /DNOMINMAX /DNDEBUG ^
   ODDRecorder.cpp ^
   /Fe:x86_64\ODDRecorder.dll ^
   /link mfreadwrite.lib mfplat.lib mfuuid.lib mf.lib d3d11.lib
```

Flags:
- `/LD` -- Build a DLL
- `/EHsc` -- C++ exception handling (standard)
- `/std:c++17` -- C++17 standard
- `/O2` -- Optimize for speed
- `/Fe:x86_64\ODDRecorder.dll` -- Output path

Make sure the `x86_64` subdirectory exists first:

```bat
mkdir x86_64
```

## Build Option C: CMake

Create a `CMakeLists.txt` alongside the source:

```cmake
cmake_minimum_required(VERSION 3.15)
project(ODDRecorder LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

add_library(ODDRecorder SHARED ODDRecorder.cpp ODDRecorder.h)

target_compile_definitions(ODDRecorder PRIVATE
    WIN32_LEAN_AND_MEAN
    NOMINMAX
)

target_link_libraries(ODDRecorder PRIVATE
    mfreadwrite mfplat mfuuid mf d3d11
)

# Output directly to the Unity plugin folder
set_target_properties(ODDRecorder PROPERTIES
    RUNTIME_OUTPUT_DIRECTORY "${CMAKE_CURRENT_SOURCE_DIR}/x86_64"
    LIBRARY_OUTPUT_DIRECTORY "${CMAKE_CURRENT_SOURCE_DIR}/x86_64"
)
```

Build:

```bat
mkdir build && cd build
cmake .. -G "Visual Studio 17 2022" -A x64
cmake --build . --config Release
```

## Unity Plugin Setup

### Plugin placement

```
recorder/
  Plugins/
    Windows/
      x86_64/
        ODDRecorder.dll       <-- Built DLL goes here
      ODDRecorder.cpp         <-- Source (not included in Unity build)
      ODDRecorder.h           <-- Source (not included in Unity build)
      BUILD.md                <-- This file
```

### Unity import settings

In Unity, select `ODDRecorder.dll` and configure the Plugin Inspector:

- **Platform**: Standalone Windows x86_64 only
- **CPU**: x86_64
- **Editor**: Include (for testing in Play Mode)
- **Standalone Player**: Include (for builds)

### Meta file

Unity will auto-generate a `.meta` file. Ensure it has the correct platform settings:

```yaml
PluginImporter:
  platformData:
    Editor:
      enabled: 1
    Standalone Windows 64-bit:
      enabled: 1
      cpu: x86_64
```

## Debugging

### OutputDebugString

All log messages use `OutputDebugStringA` with the prefix `[ODDRecorder]`. View them with:

- **Visual Studio**: Debug > Windows > Output (attach to Unity)
- **DebugView** (Sysinternals): Filter for `[ODDRecorder]`
- **Unity Console**: Messages do not appear in Unity's console by default. To forward them, register a Unity debug callback from C# that captures `OutputDebugString`.

### Common issues

| Issue | Cause | Fix |
|-------|-------|-----|
| DLL not found | Wrong path or missing dependencies | Ensure DLL is in `x86_64/` folder, check with Dependency Walker |
| MFCreateSinkWriterFromURL fails | Invalid path or missing codec | Ensure output directory exists, check Windows Media Feature Pack is installed |
| H.264 not available | Windows N/KN edition | Install Media Feature Pack from Microsoft |
| Black frames | RGBA/BGRA mismatch or flipped image | Ensure C# flips the readback data vertically before passing |
| Native capture: no device | D3D11 device not set | Call ODDRecorder_SetD3D11Device or ensure UnityPluginLoad captures the device |

## System Requirements

- Windows 10 version 1809 or later (for reliable Media Foundation H.264 support)
- Hardware H.264 encoder (Intel Quick Sync, NVIDIA NVENC, or AMD VCE) for best performance
- Falls back to software encoding (Microsoft H.264 Encoder MFT) if no hardware encoder available
