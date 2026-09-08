cmake_minimum_required(VERSION 3.21)

# This tree is an add-on distribution derived from upstream v0.8.2. Keep the
# upstream base in project(VERSION), and give the add-on its own explicit public
# identity so logs/screenshots never imply that this is an upstream release.
project(BioshockVR VERSION 0.8.2 LANGUAGES C CXX)
set(BVR_DISTRIBUTION_NAME "BioShock VR DLSS/DLAA Beta")
set(BVR_DISTRIBUTION_VERSION "0.2.3-beta")
set(BVR_DISTRIBUTION_BUILD_ID "beren5556-v0.2.3-beta")

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
