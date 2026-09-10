# --- xinput1_3.dll proxy shim -------------------------------------------------
# Thin forwarder that the game loads in place of the real xinput1_3.dll; it
# forwards every export to the SysWOW64 original and loads bioshockvr.dll.
add_library(xinput_proxy SHARED
    proxy/xinput_proxy.cpp
    proxy/xinput_proxy.def
)
set_target_properties(xinput_proxy PROPERTIES
    OUTPUT_NAME "xinput1_3"
    PREFIX ""
)

# Developer-only profile redirection for physical BioShock 2 test copies.
# Release packaging always uses the default OFF configuration.
option(BVR_BS2_TEST_ISOLATION "Isolate BioShock 2 test-copy profile and logs" OFF)
if(BVR_BS2_TEST_ISOLATION)
    target_compile_definitions(xinput_proxy PRIVATE BVR_BS2_TEST_ISOLATION)
endif()

# --- bioshockvr.dll - the mod -------------------------------------------------
add_library(bioshockvr SHARED
    core/framework/command.cpp
    core/framework/command.h
    core/framework/dllmain.cpp
    core/framework/framework.cpp
    core/framework/framework.h
    core/debug/value_scan.cpp
    core/debug/value_scan.h
    core/gfx/blit.cpp
    core/gfx/blit.h
    core/gfx/dlss45_client.cpp
    core/gfx/dlss45_client.h
    core/gfx/image_control_policy.h
    core/gfx/image_controls.cpp
    core/gfx/image_controls.h
    core/gfx/dlss_sharpen.cpp
    core/gfx/dlss_sharpen.h
    core/gfx/sampled_gpu_timer.h
    core/gfx/frame_inspector.cpp
    core/gfx/frame_inspector.h
    core/gfx/gfx_hud.cpp
    core/gfx/gfx_hud.h
    core/gfx/hud_capture.cpp
    core/gfx/hud_capture.h
    core/gfx/spatial_upscaler.cpp
    core/gfx/spatial_upscaler.h
    core/hooks/d3d11_hook.cpp
    core/hooks/d3d11_hook.h
    core/hooks/heap_scan.cpp
    core/hooks/heap_scan.h
    core/hooks/pattern_scan.cpp
    core/hooks/pattern_scan.h
    core/input/swing.cpp
    core/input/swing.h
    core/input/xinput_bridge.cpp
    core/input/xinput_bridge.h
    core/ui/overlay.cpp
    core/ui/overlay.h
    core/vr/openxr_input.cpp
    core/vr/openxr_input.h
    core/vr/openxr_runtime.cpp
    core/vr/openxr_runtime.h
    core/util/log.cpp
    core/util/log.h
    core/util/module_id.cpp
    core/util/module_id.h
    core/util/crash.cpp
    core/util/crash.h
    core/util/close_policy.h
    core/util/game_exit_gate.h
    game/adapter_registry.cpp
    game/adapter_registry.h
    game/igame_adapter.h
    game/bioshock1r/aim.cpp
    game/bioshock1r/aim.h
    game/bioshock1r/bioshock1r_adapter.cpp
    game/bioshock1r/bioshock1r_adapter.h
    game/bioshock1r/body.cpp
    game/bioshock1r/body.h
    game/bioshock1r/bones.cpp
    game/bioshock1r/bones.h
    game/bioshock1r/camera.cpp
    game/bioshock1r/camera.h
    game/bioshock1r/console_exec.cpp
    game/bioshock1r/game_ini.cpp
    game/bioshock1r/game_ini.h
    game/bioshock1r/graphics_options.cpp
    game/bioshock1r/graphics_options.h
    game/bioshock1r/console_exec.h
    game/bioshock1r/frame_context.h
    game/bioshock1r/hands.cpp
    game/bioshock1r/hands.h
    game/bioshock1r/input_drive.cpp
    game/bioshock1r/input_drive.h
    game/bioshock1r/patterns.cpp
    game/bioshock1r/patterns.h
    game/bioshock1r/recorder.cpp
    game/bioshock1r/recorder.h
    game/bioshock1r/scenedraw.cpp
    game/bioshock1r/scenedraw.h
    game/bioshock1r/performance_probe.cpp
    game/bioshock1r/performance_probe.h
    core/gfx/eye_timeline.h
    game/bioshock1r/temporal_guides.cpp
    game/bioshock1r/temporal_guides.h
    game/bioshock2r/aim.cpp
    game/bioshock2r/aim.h
    game/bioshock2r/bioshock2r_adapter.cpp
    game/bioshock2r/bioshock2r_adapter.h
    game/bioshock2r/exit_guard.cpp
    game/bioshock2r/exit_guard.h
    game/bioshock2r/exit_guard_contract.h
    game/bioshock2r/body.cpp
    game/bioshock2r/body.h
    game/bioshock2r/bones.cpp
    game/bioshock2r/bones.h
    game/bioshock2r/camera.cpp
    game/bioshock2r/camera.h
    game/bioshock2r/frame_context.h
    game/bioshock2r/game_ini.cpp
    game/bioshock2r/game_ini.h
    game/bioshock2r/hands.cpp
    game/bioshock2r/hands.h
    game/bioshock2r/input_drive.cpp
    game/bioshock2r/input_drive.h
    game/bioshock2r/patterns.cpp
    game/bioshock2r/patterns.h
    game/bioshock2r/scenedraw.cpp
    game/bioshock2r/scenedraw.h
    game/bioshock2r/temporal_guides.cpp
    game/bioshock2r/temporal_guides.h
    game/bioshockinf/aim.cpp
    game/bioshockinf/aim.h
    game/bioshockinf/arsenal.cpp
    game/bioshockinf/arsenal.h
    game/bioshockinf/bioshockinf_adapter.cpp
    game/bioshockinf/bioshockinf_adapter.h
    game/bioshockinf/bones.cpp
    game/bioshockinf/bones.h
    game/bioshockinf/camera.cpp
    game/bioshockinf/camera.h
    game/bioshockinf/cine.cpp
    game/bioshockinf/cine.h
    game/bioshockinf/edgelog.cpp
    game/bioshockinf/edgelog.h
    game/bioshockinf/fidget.cpp
    game/bioshockinf/fidget.h
    game/bioshockinf/fire.cpp
    game/bioshockinf/fire.h
    game/bioshockinf/frame_context.h
    game/bioshockinf/fxorigin.cpp
    game/bioshockinf/fxorigin.h
    game/bioshockinf/gfx.cpp
    game/bioshockinf/gfx.h
    game/bioshockinf/hands.cpp
    game/bioshockinf/hands.h
    game/bioshockinf/hide.cpp
    game/bioshockinf/hide.h
    game/bioshockinf/hint.cpp
    game/bioshockinf/hint.h
    game/bioshockinf/hud.cpp
    game/bioshockinf/hud.h
    game/bioshockinf/config.cpp
    game/bioshockinf/config.h
    game/bioshockinf/game_ini.cpp
    game/bioshockinf/game_ini.h
    game/bioshockinf/inf_math.h
    game/bioshockinf/input_drive.cpp
    game/bioshockinf/input_drive.h
    game/bioshockinf/lens.cpp
    game/bioshockinf/lens.h
    game/bioshockinf/melee.cpp
    game/bioshockinf/melee.h
    game/bioshockinf/patterns.cpp
    game/bioshockinf/patterns.h
    game/bioshockinf/profiles.cpp
    game/bioshockinf/profiles.h
    game/bioshockinf/recorder.cpp
    game/bioshockinf/recorder.h
    game/bioshockinf/reflect.cpp
    game/bioshockinf/reflect.h
    game/bioshockinf/scenedraw.cpp
    game/bioshockinf/scenedraw.h
    game/bioshockinf/xhair.cpp
    game/bioshockinf/xhair.h
    game/shared/ue_math.h
    game/shared/temporal_adapter.cpp
    game/shared/image_adapter.cpp
    game/shared/image_adapter.h
    game/shared/resolution_mailbox.h
    game/shared/temporal_adapter.h
    game/shared/temporal_types.h
)
set_target_properties(bioshockvr PROPERTIES PREFIX "")
if(BVR_BS2_TEST_ISOLATION)
    target_compile_definitions(bioshockvr PRIVATE BVR_BS2_TEST_ISOLATION)
endif()

# --- generated version header --------------------------------------------------
# Regenerated before every build of the mod, so `bioshockvr X.Y.Z (build id)` in
# the log always matches the tree it was built from. Session 23: the old
# hand-edited #define shipped "0.1.0" for v0.1.0, v0.2.0 AND v0.3.0, which made a
# user's crash log unattributable to a release.
set(BVR_VERSION_HEADER "${CMAKE_BINARY_DIR}/generated/bvr_version.h")
# Once at configure time so the header exists for a fresh tree and for IDE parsing.
execute_process(COMMAND ${CMAKE_COMMAND}
        "-DBVR_VERSION=${BVR_DISTRIBUTION_VERSION}"
        "-DBVR_BASE_VERSION=${PROJECT_VERSION}"
        "-DBVR_PRODUCT_NAME=${BVR_DISTRIBUTION_NAME}"
        "-DBVR_BUILD_ID_OVERRIDE=${BVR_DISTRIBUTION_BUILD_ID}"
        -DBVR_SRC=${PROJECT_SOURCE_DIR}
        -DBVR_IN=${PROJECT_SOURCE_DIR}/cmake/bvr_version.h.in
        -DBVR_OUT=${BVR_VERSION_HEADER}
        -P ${PROJECT_SOURCE_DIR}/cmake/GenerateVersion.cmake)
add_custom_target(bvr_version ALL
    BYPRODUCTS "${BVR_VERSION_HEADER}"
    COMMAND ${CMAKE_COMMAND}
            "-DBVR_VERSION=${BVR_DISTRIBUTION_VERSION}"
            "-DBVR_BASE_VERSION=${PROJECT_VERSION}"
            "-DBVR_PRODUCT_NAME=${BVR_DISTRIBUTION_NAME}"
            "-DBVR_BUILD_ID_OVERRIDE=${BVR_DISTRIBUTION_BUILD_ID}"
            -DBVR_SRC=${PROJECT_SOURCE_DIR}
            -DBVR_IN=${PROJECT_SOURCE_DIR}/cmake/bvr_version.h.in
            -DBVR_OUT=${BVR_VERSION_HEADER}
            -P ${PROJECT_SOURCE_DIR}/cmake/GenerateVersion.cmake
    COMMENT "Stamping bvr_version.h (${BVR_DISTRIBUTION_VERSION}, base ${PROJECT_VERSION})"
    VERBATIM)
add_dependencies(bioshockvr bvr_version)

# If a configuration emits CodeView data, embed only the PDB filename rather
# than a developer-machine absolute path. Release normally emits no PDB at all;
# this also keeps accidental symbol-enabled RC builds free of private paths.
if(MSVC)
    target_link_options(bioshockvr PRIVATE "/PDBALTPATH:bioshockvr.pdb")
endif()

target_include_directories(bioshockvr PRIVATE . "${CMAKE_BINARY_DIR}/generated")
target_link_libraries(bioshockvr PRIVATE
    minhook
    imgui
    d3d11
    d3dcompiler
    dbghelp
)
if(BIOSHOCKVR_WITH_OPENXR)
    # Static loader linked straight into the mod DLL (no extra DLL shipped).
    target_sources(bioshockvr PRIVATE core/vr/image_status_quad.cpp core/vr/image_status_quad.h)
    target_link_libraries(bioshockvr PRIVATE openxr_loader)
    target_compile_definitions(bioshockvr PRIVATE BVR_WITH_OPENXR)
endif()

# --- xr_hello32 - DR-1 test client --------------------------------------------
# Standalone 32-bit console app that proves (or disproves) that this machine's
# OpenXR runtimes accept a 32-bit D3D11 client. Not shipped with the mod.
if(BIOSHOCKVR_WITH_OPENXR)
    add_executable(xr_hello32 tools/xr_hello32/main.cpp)
    target_link_libraries(xr_hello32 PRIVATE openxr_loader d3d11 dxgi)
endif()

# Pure launcher/HUD policy parity; no game, profiles, GPU or headset required.
add_executable(image_controls_test tools/image_controls_test/main.cpp)
add_executable(resolution_mailbox_test tools/image_controls_test/resolution_mailbox_test.cpp)
target_include_directories(resolution_mailbox_test PRIVATE ${CMAKE_CURRENT_SOURCE_DIR})
add_executable(bs2_config_test tools/image_controls_test/bs2_config_test.cpp game/bioshock2r/game_ini.cpp)
target_include_directories(bs2_config_test PRIVATE ${CMAKE_CURRENT_SOURCE_DIR})
target_compile_definitions(bs2_config_test PRIVATE BVR_BS2_TEST_ISOLATION=1 BVR_CONFIG_BATCH_TESTS=1)
target_link_libraries(bs2_config_test PRIVATE shell32 ole32)
target_include_directories(image_controls_test PRIVATE ${CMAKE_CURRENT_SOURCE_DIR})
add_executable(stereo_recovery_test tools/image_controls_test/stereo_recovery_test.cpp)
target_include_directories(stereo_recovery_test PRIVATE ${CMAKE_CURRENT_SOURCE_DIR})
add_executable(image_controls_controller_test
    tools/image_controls_test/controller_test.cpp core/gfx/image_controls.cpp)
target_include_directories(image_controls_controller_test PRIVATE ${CMAKE_CURRENT_SOURCE_DIR})
add_executable(graphics_options_test
    tools/image_controls_test/graphics_test.cpp game/bioshock1r/graphics_options.cpp)
target_include_directories(graphics_options_test PRIVATE ${CMAKE_CURRENT_SOURCE_DIR})
add_executable(image_hud_layout_test tools/image_controls_test/hud_layout_test.cpp)
target_include_directories(image_hud_layout_test PRIVATE ${CMAKE_CURRENT_SOURCE_DIR})
target_link_libraries(image_hud_layout_test PRIVATE openxr_loader gdi32)
if(BVR_PERFORMANCE_PROBE)
    target_compile_definitions(bioshockvr PRIVATE BVR_PERFORMANCE_PROBE=1)
    target_compile_definitions(image_controls_test PRIVATE BVR_PERFORMANCE_PROBE=1)
    target_compile_definitions(image_controls_controller_test PRIVATE BVR_PERFORMANCE_PROBE=1)
endif()
if(BVR_DLSS_OVERLAP)
    target_compile_definitions(bioshockvr PRIVATE BVR_DLSS_OVERLAP=1)
endif()
if(BVR_DLSS_TAIL_OVERLAP)
    target_compile_definitions(bioshockvr PRIVATE BVR_DLSS_TAIL_OVERLAP=1)
endif()
if(BVR_DLSS_EARLY_DELIVERY)
    target_compile_definitions(bioshockvr PRIVATE BVR_DLSS_EARLY_DELIVERY=1)
    target_sources(bioshockvr PRIVATE core/vr/delivery_probe.cpp core/vr/delivery_probe.h)
endif()
if(BVR_CRITICAL_PATH_PROBE)
    target_compile_definitions(bioshockvr PRIVATE BVR_CRITICAL_PATH_PROBE=1)
    target_sources(bioshockvr PRIVATE core/vr/critical_path_probe.cpp core/vr/critical_path_probe.h)
endif()
if(BVR_LATENCY_PROBE)
    foreach(target bioshockvr image_controls_test image_controls_controller_test)
        target_compile_definitions(${target} PRIVATE BVR_LATENCY_PROBE=1)
    endforeach()
endif()
add_executable(dlss_sharpen_test32 tools/dlss_sharpen_test/main.cpp core/gfx/dlss_sharpen.cpp)
target_include_directories(dlss_sharpen_test32 PRIVATE ${CMAKE_CURRENT_SOURCE_DIR})
target_link_libraries(dlss_sharpen_test32 PRIVATE d3d11 d3dcompiler)

add_executable(eye_timeline_test32 tools/eye_timeline_test/main.cpp
    game/bioshock1r/performance_probe.cpp)
target_include_directories(eye_timeline_test32 PRIVATE ${CMAKE_CURRENT_SOURCE_DIR})
target_compile_definitions(eye_timeline_test32 PRIVATE BVR_PERFORMANCE_PROBE=1)
target_link_libraries(eye_timeline_test32 PRIVATE d3d11)

# Real split client with local pipes and WARP fences; no NGX, game or runtime files.
add_executable(dlss_overlap_test32 tools/dlss_overlap_test/main.cpp core/gfx/dlss_sharpen.cpp)
target_include_directories(dlss_overlap_test32 PRIVATE ${CMAKE_CURRENT_SOURCE_DIR})
target_link_libraries(dlss_overlap_test32 PRIVATE d3d11 d3dcompiler)

# Opt-in hardware NGX transitions. Explicit test directories; never a real game.
add_executable(dlss_runtime_test32 tools/dlss_runtime_test/main.cpp core/gfx/dlss_sharpen.cpp)
target_include_directories(dlss_runtime_test32 PRIVATE ${CMAKE_CURRENT_SOURCE_DIR})
target_link_libraries(dlss_runtime_test32 PRIVATE d3d11 d3dcompiler)

add_executable(latency_probe_test32 tools/latency_probe_test/main.cpp core/gfx/dlss_sharpen.cpp)
target_include_directories(latency_probe_test32 PRIVATE ${CMAKE_CURRENT_SOURCE_DIR})
target_compile_definitions(latency_probe_test32 PRIVATE BVR_LATENCY_PROBE=1)
target_link_libraries(latency_probe_test32 PRIVATE d3d11 d3dcompiler)
if(BVR_DLSS_TAIL_OVERLAP)
    target_compile_definitions(dlss_overlap_test32 PRIVATE BVR_DLSS_TAIL_OVERLAP=1)
    target_compile_definitions(latency_probe_test32 PRIVATE BVR_DLSS_TAIL_OVERLAP=1)
endif()

# Delivery ordering and observational hook; standalone, no game or OpenXR.
add_executable(delivery_probe_test32 tools/delivery_probe_test/main.cpp core/vr/delivery_probe.cpp)
target_include_directories(delivery_probe_test32 PRIVATE ${CMAKE_CURRENT_SOURCE_DIR})
target_compile_definitions(delivery_probe_test32 PRIVATE BVR_DELIVERY_PROBE_TEST=1)
target_link_libraries(delivery_probe_test32 PRIVATE minhook d3d11)

# Pure timing/accounting checks, no game or profile access.
add_executable(critical_path_probe_test32 tools/critical_path_probe_test/main.cpp core/vr/critical_path_probe.cpp)
target_include_directories(critical_path_probe_test32 PRIVATE ${CMAKE_CURRENT_SOURCE_DIR})
target_compile_definitions(critical_path_probe_test32 PRIVATE BVR_CRITICAL_PATH_PROBE=1 BVR_CRITICAL_PATH_PROBE_TEST=1)
add_executable(input_wait_probe_test32 tools/input_wait_probe_test/main.cpp)
target_include_directories(input_wait_probe_test32 PRIVATE ${CMAKE_CURRENT_SOURCE_DIR})
if(BVR_CRITICAL_PATH_PROBE)
    foreach(target dlss_overlap_test32 latency_probe_test32)
        target_compile_definitions(${target} PRIVATE BVR_CRITICAL_PATH_PROBE=1)
        target_sources(${target} PRIVATE core/vr/critical_path_probe.cpp)
    endforeach()
endif()

# --- spatial_upscaler_test32 - deterministic D3D11/WARP test -----------------
# Exercises the actual injected shader and validates basic output plus state
# restoration without a game or headset. It is a developer tool and the
# release packager's explicit file list does not ship it.
add_executable(spatial_upscaler_test32
    tools/spatial_upscaler_test/main.cpp
    core/gfx/spatial_upscaler.cpp
    core/gfx/spatial_upscaler.h
    core/util/log.cpp
    core/util/log.h
)
target_include_directories(spatial_upscaler_test32 PRIVATE .)
target_link_libraries(spatial_upscaler_test32 PRIVATE d3d11 d3dcompiler shell32)

# --- temporal_guides_test32 - deterministic D3D11/WARP test -----------------
# Validates main-DSV voting, D24->R32 conversion and independent L/R camera
# histories without loading the game or talking to an NGX/IPC host.
add_executable(temporal_guides_test32
    tools/temporal_guides_test/main.cpp
    game/bioshock1r/temporal_guides.cpp
    game/bioshock1r/temporal_guides.h
    core/util/log.cpp
    core/util/log.h
)
target_include_directories(temporal_guides_test32 PRIVATE .)
target_link_libraries(temporal_guides_test32 PRIVATE d3d11 d3dcompiler shell32)
if(BVR_DEPTH_COPY_REUSE)
    target_compile_definitions(bioshockvr PRIVATE BVR_DEPTH_COPY_REUSE=1)
    target_compile_definitions(temporal_guides_test32 PRIVATE BVR_DEPTH_COPY_REUSE=1)
endif()

# --- temporal_guides_bs2_test32 - BS2 finite-depth/camera contract ------------
add_executable(close_policy_test32 tools/close_policy_test/main.cpp)
target_include_directories(close_policy_test32 PRIVATE .)

add_executable(bvr_bs2_exit_guard_test tools/bs2_exit_guard_test/main.cpp)
target_include_directories(bvr_bs2_exit_guard_test PRIVATE .)

add_executable(game_exit_gate_test32 ../tools/game_exit_gate_test/main.cpp)
target_include_directories(game_exit_gate_test32 PRIVATE .)

add_executable(temporal_guides_bs2_test32
    tools/temporal_guides_bs2_test/main.cpp
    game/bioshock2r/temporal_guides.cpp
    core/util/log.cpp
)
target_include_directories(temporal_guides_bs2_test32 PRIVATE .)
if(BVR_DEPTH_COPY_REUSE)
    target_compile_definitions(temporal_guides_bs2_test32 PRIVATE BVR_DEPTH_COPY_REUSE=1)
endif()
target_link_libraries(temporal_guides_bs2_test32 PRIVATE d3d11 d3dcompiler shell32)

# --- bvr_xrsim32 - the simulated OpenXR runtime -------------------------------
# A Quest-3-shaped 32-bit OpenXR runtime so an agent can test the mod with no
# headset. NOT part of the mod and NOT in the release zip (tools/package.ps1
# stages an explicit two-file list, so there is nothing to exclude there).
#
# It links NOTHING from bioshockvr and must not link openxr_loader either - it
# IS the runtime the loader loads, so it takes the OpenXR headers only. Keeping
# it behind an option makes the "the mod DLLs are byte-identical" claim checkable
# by configuring once with -DBIOSHOCKVR_WITH_XRSIM=OFF and comparing hashes.
option(BIOSHOCKVR_WITH_XRSIM "Build the simulated OpenXR runtime (test tool)" ON)
if(BIOSHOCKVR_WITH_OPENXR AND BIOSHOCKVR_WITH_XRSIM)
    add_library(bvr_xrsim32 SHARED
        tools/xrsim/xrsim_common.h
        tools/xrsim/xrsim_internal.h
        tools/xrsim/xrsim_math.h
        tools/xrsim/xrsim_log.cpp
        tools/xrsim/xrsim_instance.cpp
        tools/xrsim/xrsim_session.cpp
        tools/xrsim/xrsim_frame.cpp
        tools/xrsim/xrsim_actions.cpp
        tools/xrsim/xrsim_control.cpp
        tools/xrsim/xrsim_compositor.cpp
        tools/xrsim/xrsim.def
    )
    set_target_properties(bvr_xrsim32 PROPERTIES PREFIX "" OUTPUT_NAME "bvr_xrsim32")
    target_include_directories(bvr_xrsim32 PRIVATE
        tools/xrsim
        "${PROJECT_SOURCE_DIR}/third_party/OpenXR-SDK/include")
    target_link_libraries(bvr_xrsim32 PRIVATE d3d11 dxgi d3dcompiler windowscodecs)
    # The SEH shims around every entry point need unwind semantics off; /EHsc and
    # __try in the same function is what the compiler warns about, and the shims
    # deliberately hold no unwindable objects.
    target_compile_options(bvr_xrsim32 PRIVATE /EHa)
endif()

# --- bvr_steamvr32 - the SteamVR (OpenXR-on-OpenVR) shim runtime --------------
# SteamVR ships no 32-bit OpenXR runtime; this DLL is one, backed by OpenVR.
# Same shape as bvr_xrsim32 (a real runtime the loader discovers via manifest,
# sole export xrNegotiateLoaderRuntimeInterface, links NOTHING from bioshockvr
# and not openxr_loader) - but unlike xrsim this one IS shipped: tools/
# package.ps1 stages it into the release zip together with Valve's
# openvr_api.dll (third_party/openvr_headers/bin/win32, BSD-3, see
# PROVENANCE.txt). The mod selects it at runtime by writing a manifest and
# setting XR_RUNTIME_JSON when no native 32-bit runtime works (openxr_runtime
# .cpp init_instance). Adapted from BioVRDev's OpenXRShim with permission -
# THIRD_PARTY_NOTICES.md.
option(BIOSHOCKVR_WITH_OVRSHIM "Build the SteamVR OpenVR shim runtime (shipped)" ON)
if(BIOSHOCKVR_WITH_OPENXR AND BIOSHOCKVR_WITH_OVRSHIM)
    add_library(bvr_ovr_shim SHARED
        tools/ovrshim/ovrshim.h
        tools/ovrshim/ovrshim_negotiate.cpp
        tools/ovrshim/ovrshim_main.cpp
        tools/ovrshim/ovrshim_render.cpp
        tools/ovrshim/ovrshim_input.cpp
        tools/ovrshim/ovrshim.def
    )
    set_target_properties(bvr_ovr_shim PROPERTIES PREFIX "" OUTPUT_NAME "bvr_steamvr32")
    target_include_directories(bvr_ovr_shim PRIVATE
        tools/ovrshim
        "${PROJECT_SOURCE_DIR}/third_party/OpenXR-SDK/include"
        "${PROJECT_SOURCE_DIR}/third_party/openvr_headers")
    # openvr_api.dll and d3dcompiler are LoadLibrary'd at runtime - link only
    # the D3D11 the compositing pipeline needs.
    target_link_libraries(bvr_ovr_shim PRIVATE d3d11)
endif()
