// Included by main.cpp after its WARP helpers. These are real D24 copies and
// R32 readbacks with simulated hook notifications, not a game/NGX/OpenXR test.
#ifdef BVR_DEPTH_COPY_REUSE
bool depth_reuse_cases(ID3D11Device* device, ID3D11DeviceContext* context,
                       DepthSurface& main, DepthSurface& minor) {
    namespace G = bvr::b1r::temporal_guides;
    unsigned checks=0, failures=0;
    auto check=[&](bool pass,const char* text) {
        ++checks; if(!pass) ++failures;
        std::printf("%s: depth reuse: %s\n",pass?"PASS":"FAIL",text);
    };
    ID3D11DepthStencilState *readOnly=nullptr, *disabled=nullptr;
    D3D11_DEPTH_STENCIL_DESC desc{};
    desc.DepthEnable=TRUE;desc.DepthWriteMask=D3D11_DEPTH_WRITE_MASK_ZERO;
    desc.DepthFunc=D3D11_COMPARISON_ALWAYS;
    if(FAILED(device->CreateDepthStencilState(&desc,&readOnly))) return false;
    desc.DepthEnable=FALSE;desc.DepthWriteMask=D3D11_DEPTH_WRITE_MASK_ALL;
    if(FAILED(device->CreateDepthStencilState(&desc,&disabled))) {release_one(readOnly);return false;}
    auto state=[&](ID3D11DepthStencilState* value) {
        context->OMSetDepthStencilState(value,0);G::on_depth_state(context,value);
    };
    auto stats=[] {G::Diagnostics d{};G::get_diagnostics(&d);return d;};
    auto pass=[&](unsigned votes=1) {
        bind_depth(context,main.dsv);vote(votes,context);bind_depth(context,nullptr);
    };
    auto start=[&](float depth) {
        G::invalidate();state(readOnly);
        bind_depth(context,main.dsv);clear_depth(context,main,depth);
        vote(8,context);bind_depth(context,nullptr);
    };
    uint64_t build=1001;
    auto output=[&](float expected,unsigned draws) {
        G::EyeGuides guides{};float depth=0;
        bool ok=G::generate_eye(context,0,tagged_projection(0,build),&guides);
        build+=2;
        return ok&&guides.depthDraws==draws&&guides.coherent&&
            guides.depthTextureIdentity==reinterpret_cast<std::uintptr_t>(main.texture)&&
            read_depth(device,context,guides.depthTexture,16,16,&depth)&&almost(depth,expected);
    };
    G::set_copy_tracking_available(true);

    auto before=stats();start(0.63f);
    for(int i=0;i<9;++i) pass();
    auto after=stats();
    check(after.depthCopies-before.depthCopies==1&&after.unchangedCopiesSkipped-before.unchangedCopiesSkipped==9,
          "10 read-only subpasses require 1 copy, not 10");
    check(output(0.63f,17),"same D24 result, original winner/vote count and coherent camera");

    before=stats();start(0.31f);state(disabled);pass();after=stats();
    check(after.depthCopies-before.depthCopies==1&&after.unchangedCopiesSkipped-before.unchangedCopiesSkipped==1&&
          output(0.31f,9),"DepthEnable=FALSE permits reuse even with write-mask ALL");

    before=stats();start(0.21f);
    bind_depth(context,main.dsv);clear_depth(context,main,0.79f);vote(1,context);bind_depth(context,nullptr);
    after=stats();
    check(after.depthCopies-before.depthCopies==2&&output(0.79f,9),
          "depth clear bypasses read-only state and forces a fresh copy");

    before=stats();start(0.47f);
    context->ClearDepthStencilView(main.dsv,D3D11_CLEAR_STENCIL,0.0f,7);
    G::on_clear_dsv(context,main.dsv,D3D11_CLEAR_STENCIL,0.0f,7);
    pass();after=stats();
    check(after.depthCopies-before.depthCopies==1&&output(0.47f,9),
          "stencil-only clear cannot change the R24 depth consumed by DLSS");

    before=stats();start(0.42f);state(nullptr);pass();after=stats();
    check(after.depthCopies-before.depthCopies==2&&output(0.42f,9),
          "null/default depth state restores write tracking");

    before=stats();start(0.44f);state(nullptr);G::on_untracked_draw(context);
    state(readOnly);pass();after=stats();
    check(after.depthCopies-before.depthCopies==2&&output(0.44f,9),
          "non-voting/instanced/indirect draws invalidate reuse without changing votes");

    // Seed donor before the tested interval so its clear cannot dirty that interval.
    clear_depth(context,minor,0.88f);
    before=stats();start(0.22f);
    G::on_resource_write(context,main.texture);context->CopyResource(main.texture,minor.texture);
    pass();after=stats();
    check(after.depthCopies-before.depthCopies==2&&output(0.88f,9),
          "copy/update of the captured source invalidates reuse and yields the new pixels");

    before=stats();start(0.37f);
    context->ClearState();G::on_context_reset(context);state(readOnly);pass();after=stats();
    check(after.depthCopies-before.depthCopies==2&&output(0.37f,9),
          "context reset/command-list completion invalidates the cached snapshot");

    G::invalidate();state(readOnly);
    clear_depth(context,minor,0.15f);clear_depth(context,main,0.75f);
    before=stats();bind_depth(context,minor.dsv);vote(2,context);bind_depth(context,nullptr);
    pass(6);after=stats();
    check(after.depthCopies-before.depthCopies==2&&output(0.75f,6),
          "new winning texture always gets a copy, even without a new write");

    before=stats();start(0.59f);
    ID3D11DeviceContext* foreign=nullptr;
    bool foreignOk=SUCCEEDED(device->CreateDeferredContext(0,&foreign));
    if(foreignOk) {
        G::on_depth_state(foreign,nullptr);G::on_untracked_draw(foreign);
        G::on_context_reset(foreign);G::on_resource_write(foreign,main.texture);
    }
    pass();after=stats();release_one(foreign);
    check(foreignOk&&after.depthCopies-before.depthCopies==1&&output(0.59f,9),
          "foreign/deferred contexts do not change immediate-context tracking");

    G::set_copy_tracking_available(false);
    before=stats();start(0.68f);pass();pass();after=stats();
    check(after.depthCopies-before.depthCopies==3&&
          after.unchangedCopiesSkipped==before.unchangedCopiesSkipped&&output(0.68f,10),
          "missing tracking hook restores the exact original copy policy");
    G::set_copy_tracking_available(true);

    // No depth capture is ever reused across eyes or intervals.
    before=stats();start(0.61f);bool first=output(0.61f,8);
    start(0.62f);bool second=output(0.62f,8);after=stats();
    check(first&&second&&after.depthCopies-before.depthCopies==2,
          "fresh capture in each interval; no previous-frame reuse");
    state(nullptr);release_one(readOnly);release_one(disabled);
    std::printf("Depth-reuse checks: %u/%u passed\n",checks-failures,checks);
    return failures==0;
}
#endif
