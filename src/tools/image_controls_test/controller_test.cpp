// Exercises the shipping controller with only a private TEMP/GUID ini.
#include "core/gfx/image_controls.h"
#include "core/util/log.h"
#include "game/bioshock1r/game_ini.h"
#include "game/bioshock1r/graphics_options.h"
#include <windows.h>
#include <objbase.h>
#include <cstdio>
#include <string>
#pragma comment(lib, "ole32.lib")
namespace {
unsigned checks=0, failures=0, viewportWrites=0;
unsigned graphicsWrites = 0;
bool graphicsUnavailable = false;
bvr::b1r::game_ini::Viewport viewport{4096,4096,4096,4096,false,true};
bool expect(bool ok, const char* name) {
    ++checks; if (!ok) ++failures;
    std::printf("%s %u: %s\n", ok ? "PASS" : "FAIL", checks, name); return ok;
}
bool has(const std::string& s, const char* value) { return s.find(value) != std::string::npos; }
std::string bytes(const std::wstring& path) {
    HANDLE file = CreateFileW(path.c_str(), GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING, 0, nullptr);
    if (file == INVALID_HANDLE_VALUE) return {};
    std::string result(GetFileSize(file, nullptr), '\0'); DWORD got = 0;
    ReadFile(file, result.data(), DWORD(result.size()), &got, nullptr); CloseHandle(file);
    return got == result.size() ? result : std::string();
}
}
namespace bvr::log { void write(const char*, ...) {} }
namespace bvr::b1r::graphics_options {
Values fixture{};
Values read() {
    static bool first = true;
    if (first) { for (size_t i=0; i<kCount; ++i) fixture[i] = {kOptions[i].defaultOn,true,true}; first=false; }
    return fixture;
}
Change toggle(size_t index, Value& observed) {
    if (graphicsUnavailable) return Change::RestartRequired;
    ++graphicsWrites; fixture[index].on = !fixture[index].on;
    observed = fixture[index]; return Change::Applied;
}
}
namespace bvr::b1r::game_ini {
Viewport read_viewport() { return viewport; }
bool write_viewport(uint32_t w, uint32_t h) {
    ++viewportWrites; viewport={w,h,w,h,false,true}; return true;
}
}
int main() {
    namespace c = bvr::image_controls;
    using c::RenderMode; using c::Settings;
    wchar_t temp[MAX_PATH]{}, suffix[40]{}; GUID guid{};
    DWORD size=GetTempPathW(MAX_PATH,temp);
    if (!size || size>=MAX_PATH || FAILED(CoCreateGuid(&guid)) || !StringFromGUID2(guid,suffix,40)) return 2;
    std::wstring dir=std::wstring(temp)+L"bvr-controller-test-"+suffix;
    if (!CreateDirectoryW(dir.c_str(),nullptr)) return 2;
    const std::wstring ini=dir+L"\\dlss.ini";
    WritePrivateProfileStringW(L"fixture",L"untouched",L"keep",ini.c_str());
    WritePrivateProfileStringW(L"dlss",L"srScaleNumerator",L"2",ini.c_str());
    WritePrivateProfileStringW(L"dlss",L"srScaleDenominator",L"3",ini.c_str());
    std::printf("Private fixture retained: %ls\n",ini.c_str());
    Settings applied{}, requested{}, previous{};
    applied.mode=RenderMode::Dlss; applied.outputWidth=applied.outputHeight=4096;
    c::geometry(applied); c::set_enabled(true); c::initialize(applied,ini.c_str());
    auto take=[&]{return c::take_request(requested,previous);};
    auto accept=[&]{applied=requested;c::confirm(applied);c::game_tick();};
    auto saved=[&](const wchar_t* key){return GetPrivateProfileIntW(L"dlss",key,999,ini.c_str());};
    auto select=[&](const char* panel){
        for(int i=0;i<6;++i){if(has(c::panel_status(),panel))return true;c::on_key(VK_F1);} return false;
    };
    expect(c::panel_status().empty(),"starts hidden");
    c::on_key(VK_F3);c::on_key(VK_F2);c::on_key(VK_F6);
    expect(!take(),"hidden +/- and legacy F6 cannot change settings");
    const char* labels[]={"MODO DE RENDERIZADO","RESOLUCION POR OJO","CALIDAD DLSS","SHARPNESS DLSS","OPCIONES GRAFICAS"};
    for(auto label:labels){
        c::on_key(VK_F1);expect(has(c::panel_status(),label),label);
        if (!has(label, "OPCIONES GRAFICAS")) {
            const auto before = c::panel_status(); c::on_key(VK_F4);
            expect(!take() && graphicsWrites == 0 && c::panel_status() == before,
                   "F4 inert outside graphics, existing value unchanged");
        }
    }
    c::on_key(VK_F1);expect(c::panel_status().empty(),"sixth F1 hides without changing mode");
    c::on_key(VK_F1);c::on_key(VK_F2);
    if(!expect(take()&&requested.mode==RenderMode::Normal&&previous.mode==RenderMode::Dlss&&
               requested.renderWidth==4096&&requested.srScale.numerator==2,
               "F2 decreases DLSS to NORMAL without changing output or SR preference"))return 1;
    const Settings normal=requested;
    c::on_key(VK_F3);c::on_key(VK_F2);
    expect(!take()&&has(c::panel_status(),"DLSS  |"),"busy ignores new changes and shows applied mode");
    c::on_key(VK_F1);c::on_key(VK_F1);
    c::confirm(normal);applied=normal;
    expect(has(c::panel_status(),"NORMAL  |  RESOLUCION POR OJO"),"F1 usable while busy, native confirm removes quality panel");
    c::on_key(VK_F3);expect(!take()&&viewportWrites==0,"unpersisted confirmation cannot race another request");
    c::game_tick();
    wchar_t kept[16]{}, mode[16]{};
    GetPrivateProfileStringW(L"fixture",L"untouched",L"",kept,16,ini.c_str());
    GetPrivateProfileStringW(L"dlss",L"mode",L"",mode,16,ini.c_str());
    expect(std::wstring(kept)==L"keep"&&std::wstring(mode)==L"off"&&saved(L"srScaleNumerator")==2&&
           saved(L"srScaleDenominator")==3,"only confirmed settings persisted, unknown INI keys retained");
    expect(viewportWrites==0,"unchanged viewport is not rewritten");
    c::on_key(VK_F1);expect(has(c::panel_status(),"OPCIONES GRAFICAS"),"NORMAL includes graphics after resolution");
    c::on_key(VK_F1);expect(c::panel_status().empty(),"NORMAL graphics ends wheel");
    c::on_key(VK_F1);c::on_key(VK_F3);
    if(!expect(take()&&requested.mode==RenderMode::Dlss&&requested.renderWidth==2730&&requested.outputWidth==4096,
               "F3 increases NORMAL to original DLSS 2/3"))return 1;
    accept();
    expect(select("CALIDAD DLSS"),"select existing quality option");c::on_key(VK_F3);
    if(!expect(take()&&requested.srScale.numerator==7&&requested.srScale.denominator==10&&requested.renderWidth==2868,
               "F3 quality selects unchanged 70% step"))return 1;
    accept();
    expect(select("SHARPNESS DLSS"),"select nitidez");c::on_key(VK_F3);
    if(!expect(take()&&requested.sharpnessPercent==5&&c::same_render_path(requested,previous),
               "nitidez modifies no geometry or ratio"))return 1;
    unsigned writes=viewportWrites;accept();
    expect(saved(L"sharpnessPercent")==5&&viewportWrites==writes,"nitidez saved without rewriting viewport");
    select("MODO DE RENDERIZADO");c::on_key(VK_F3);
    if(!expect(take()&&requested.mode==RenderMode::Dlaa&&requested.renderWidth==4096&&requested.sharpnessPercent==5,
               "F3 DLSS to DLAA retains nitidez preference"))return 1;
    accept();c::on_key(VK_F1);
    expect(has(c::panel_status(),"RESOLUCION POR OJO"),"DLAA mode then resolution");
    c::on_key(VK_F1);expect(has(c::panel_status(),"OPCIONES GRAFICAS"),"DLAA graphics omits quality and nitidez");
    c::on_key(VK_F1);expect(c::panel_status().empty(),"DLAA graphics ends wheel");
    c::on_key(VK_F3);c::on_key(VK_F2);expect(!take(),"hidden remains inert after DLAA");
    select("MODO DE RENDERIZADO");c::on_key(VK_F3);
    if(!expect(take()&&requested.mode==RenderMode::Normal,"F3 wraps DLAA to NORMAL"))return 1;
    accept();c::on_key(VK_F3);
    if(!expect(take()&&requested.mode==RenderMode::Dlss&&requested.renderWidth==2868&&requested.sharpnessPercent==5,
               "return DLSS restores exact quality and nitidez"))return 1;
    accept();select("CALIDAD DLSS");c::on_key(VK_F2);
    if(!expect(take()&&requested.srScale.numerator==13&&requested.srScale.denominator==20&&has(c::panel_status(),"70.0%"),
               "F2 selects 65% while panel still shows applied 70%"))return 1;
    writes=viewportWrites;c::reject("Test rejection");c::game_tick();
    expect(!take()&&saved(L"srScaleNumerator")==7&&viewportWrites==writes,"rejected setting cannot be persisted");
    select("RESOLUCION POR OJO");c::on_key(VK_F3);
    if(!expect(take()&&requested.outputWidth==4196&&requested.renderWidth==2938&&requested.srScale.numerator==7,
               "F3 resolution adds 100px, unchanged SR fraction"))return 1;
    c::reject("End fixture");
    expect(select("OPCIONES GRAFICAS"), "graphics page reachable");
    c::game_tick();
    const std::string beforeGraphicsIni = bytes(ini);
    unsigned beforeGraphicsViewport = viewportWrites;
    expect(has(c::panel_status(), "> Shaders de alto detalle: Si") &&
           has(c::panel_status(), "Reflejos *: No") && has(c::panel_status(), "Ondulaciones del agua *: No") &&
           has(c::panel_status(), "F4: cambiar"), "nine-option panel exposes defaults and F4 hint");
    c::on_key(VK_F2);
    expect(has(c::panel_status(), "> Detalle de fluidos:"), "F2 wraps to previous graphic option");
    c::on_key(VK_F3); c::on_key(VK_F4);
    expect(graphicsWrites == 0 && !take(), "F4 queues game-thread work without rendering reconfiguration");
    c::game_tick();
    expect(graphicsWrites == 1 && has(c::panel_status(), "> Shaders de alto detalle: No") &&
           bytes(ini) == beforeGraphicsIni && viewportWrites == beforeGraphicsViewport,
           "confirmed graphics switch leaves DLSS geometry and preferences untouched");
    graphicsUnavailable = true; c::on_key(VK_F4); c::game_tick();
    expect(graphicsWrites == 1 && has(c::panel_status(), "No disponible en caliente") &&
           has(c::panel_status(), "> Shaders de alto detalle: No"), "unsupported hot change is not reported as applied");
    graphicsUnavailable = false;
    c::on_key(VK_F1); expect(c::panel_status().empty(), "F1 closes graphics page");
    c::set_enabled(false);c::on_key(VK_F3);expect(!take()&&c::panel_status().empty(),"disabled controller inert");
    c::set_enabled(true);c::on_key(VK_F6);expect(!take(),"F6 no longer changes mode");
    const Settings beforeProbe = applied;
    const std::string iniBefore = bytes(ini);
    const unsigned writesBeforeProbe = viewportWrites;
    if (c::kProbeAvailable) {
        expect(!iniBefore.empty(),"fixture bytes available for diagnostic no-write assertion");
        for (c::Probe stage : {c::Probe::Normal, c::kMiddleProbe, c::Probe::Dlaa}) {
            c::on_key(VK_F4);
            if (!expect(take() && requested.probe == stage &&
                        requested.renderWidth == beforeProbe.renderWidth &&
                        requested.outputWidth == beforeProbe.renderWidth,
                        "F4 keeps the same engine size in each variant")) return 1;
            c::on_key(VK_F4); expect(!take(),"F4 cannot race the pending GPU rebuild");
            accept();
            expect(has(c::panel_status(),c::probe_name(stage)),"panel names the actual confirmed variant");
            c::on_key(VK_F2);c::on_key(VK_F3);
            expect(!take(),"comparison blocks accidental value changes");
            c::on_key(VK_F1);expect(c::panel_status().empty(),"F1 hides diagnostic panel");
            c::on_key(VK_F1);
            expect(bytes(ini)==iniBefore && viewportWrites==writesBeforeProbe,
                   "each diagnostic phase leaves INI and viewport completely untouched");
        }
        c::on_key(VK_F4);
        if (!expect(take() && c::same(requested,beforeProbe),"F4 restores exact pre-test settings"))return 1;
        accept();
        expect(bytes(ini)==iniBefore && viewportWrites==writesBeforeProbe,
               "leaving the diagnostic does not persist temporary values");
        c::on_key(VK_F4);expect(take(),"diagnostic can start again");
        c::reject("Probe unavailable");c::game_tick();
        expect(bytes(ini)==iniBefore && !has(c::panel_status(),"PRUEBA:"),
               "failed diagnostic entry leaves original mode and INI intact");
    } else {
        c::on_key(VK_F4);expect(!take(),"F4 is inert outside a diagnostic build");
    }
    std::printf("Controller checks: %u/%u passed. No game/profile touched.\n",checks-failures,checks);
    return failures?1:0;
}
