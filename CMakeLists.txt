cmake_minimum_required(VERSION 3.21)

# This tree is an add-on distribution derived from upstream v0.8.2. Keep the
# upstream base in project(VERSION), and give the add-on its own explicit public
# identity so logs/screenshots never imply that this is an upstream release.
project(BioshockVR VERSION 0.8.2 LANGUAGES C CXX)
set(BVR_DISTRIBUTION_NAME "BioShock VR DLSS/DLAA")
set(BVR_DISTRIBUTION_VERSION "0.2.12")
set(BVR_DISTRIBUTION_BUILD_ID "beren5556-v0.2.12-bilingual")

# Local DLL-only diagnostic. No installer/launcher/runtime payload is rebuilt.
option(BVR_PERFORMANCE_PROBE "Enable the temporary BioShock 1 F4 comparison" OFF)
if(BVR_PERFORMANCE_PROBE)
    set(BVR_DISTRIBUTION_VERSION "0.2.6-diag2")
    set(BVR_DISTRIBUTION_BUILD_ID "beren5556-v0.2.6-diag2-scene-timing")
endif()

option(BVR_DLSS_OVERLAP "BioShock 1: overlap left DLSS host work with the right scene" ON)
if(BVR_DLSS_OVERLAP)
    set(BVR_DISTRIBUTION_VERSION "0.2.6-perf1")
    set(BVR_DISTRIBUTION_BUILD_ID "beren5556-v0.2.6-perf1-eye-overlap")
endif()

option(BVR_DEPTH_COPY_REUSE "BioShock 1: reuse unchanged captured depth between read-only passes" ON)
if(BVR_DEPTH_COPY_REUSE)
    if(NOT BVR_DLSS_OVERLAP)
        message(FATAL_ERROR "The perf2 candidate must retain BVR_DLSS_OVERLAP=ON")
    endif()
    set(BVR_DISTRIBUTION_VERSION "0.2.6-perf2")
    set(BVR_DISTRIBUTION_BUILD_ID "beren5556-v0.2.6-perf2-depth-copy-reuse")
endif()

# Local timing comparison; keeps both previously tested optimizations.
option(BVR_LATENCY_PROBE "BioShock 1: NORMAL/bridge-only/DLAA latency comparison" OFF)
if(BVR_LATENCY_PROBE)
    if(NOT BVR_PERFORMANCE_PROBE OR NOT BVR_DLSS_OVERLAP OR NOT BVR_DEPTH_COPY_REUSE)
        message(FATAL_ERROR "latency1 requires the existing probe, overlap and depth reuse")
    endif()
    set(BVR_DISTRIBUTION_VERSION "0.2.6-latency1")
    set(BVR_DISTRIBUTION_BUILD_ID "beren5556-v0.2.6-latency1-bridge-timing")
endif()

# Local perf3 keeps the proven left overlap and uses the right host's wait
# interval for independent compositor layers. No new history or frame queue.
option(BVR_DLSS_TAIL_OVERLAP "BioShock 1: overlap frame-tail work and reuse the submitted color backup" ON)
if(BVR_DLSS_TAIL_OVERLAP)
    if(NOT BVR_DLSS_OVERLAP OR NOT BVR_DEPTH_COPY_REUSE)
        message(FATAL_ERROR "perf3 must retain the existing eye overlap and depth reuse")
    endif()
    set(BVR_DISTRIBUTION_VERSION "0.2.6-perf3")
    set(BVR_DISTRIBUTION_BUILD_ID "beren5556-v0.2.6-perf3-tail-overlap-input-reuse")
endif()

# Keep desktop-only work outside the headset submission dependency. The
# accompanying observer reads VDXR's own reported GPU time without altering it.
option(BVR_DLSS_EARLY_DELIVERY "BioShock 1: submit XR before the desktop mirror/composite" ON)
if(BVR_DLSS_EARLY_DELIVERY)
    if(NOT BVR_DLSS_TAIL_OVERLAP)
        message(FATAL_ERROR "Early XR delivery requires the tested tail overlap")
    endif()
    set(BVR_DISTRIBUTION_VERSION "0.2.6-perf4")
    set(BVR_DISTRIBUTION_BUILD_ID "beren5556-v0.2.6-perf4-xr-before-desktop")
endif()

# Local perf5 observes critical-path intervals without changing image settings.
option(BVR_CRITICAL_PATH_PROBE "BioShock 1: exclusive CPU cycle and sparse input-fence timing" OFF)
if(BVR_CRITICAL_PATH_PROBE)
    if(NOT BVR_DLSS_EARLY_DELIVERY)
        message(FATAL_ERROR "perf5 retains the complete perf4 candidate")
    endif()
    set(BVR_DISTRIBUTION_VERSION "0.2.6-perf5")
    set(BVR_DISTRIBUTION_BUILD_ID "beren5556-v0.2.6-perf5-input-wait-timing")
endif()

# Ship the tested rendering optimizations independently from temporary A/B/C
# experiments. Diagnostic builds above retain their original traceable names.
if(NOT BVR_PERFORMANCE_PROBE AND NOT BVR_LATENCY_PROBE AND NOT BVR_CRITICAL_PATH_PROBE)
    if(NOT BVR_DLSS_OVERLAP OR NOT BVR_DEPTH_COPY_REUSE OR
       NOT BVR_DLSS_TAIL_OVERLAP OR NOT BVR_DLSS_EARLY_DELIVERY)
        message(FATAL_ERROR "The stable build must retain all four tested performance optimizations")
    endif()
    set(BVR_DISTRIBUTION_VERSION "0.2.12")
    set(BVR_DISTRIBUTION_BUILD_ID "beren5556-v0.2.12-bilingual")
endif()

# BioshockHD.exe is 32-bit x86. Everything we inject must be too.
if(NOT CMAKE_SIZEOF_VOID_P EQUAL 4)
    message(FATAL_ERROR
        "This mod targets a 32-bit game. Configure with the 'win32' preset:\n"
        "  cmake --preset win32\n"
        "(or pass -G \"Visual Studio 17 2022\" -A Win32)")
endif()

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# Static CRT: no VC redist dependency inside the game process.
set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")

add_compile_definitions(WIN32_LEAN_AND_MEAN NOMINMAX)

add_subdirectory(third_party)
add_subdirectory(src)
