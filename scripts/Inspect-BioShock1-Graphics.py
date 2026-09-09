"""Offline graphics-key xrefs; prints findings only, never redistributes game data."""
import importlib.util
import struct
import sys

sys.path.insert(0, "artifacts/python-tools")
spec = importlib.util.spec_from_file_location("disasm", sys.argv[1])
disasm = importlib.util.module_from_spec(spec)
spec.loader.exec_module(disasm)
pe = disasm.Pe(sys.argv[2])
keys = ["HighDetailShaders", "Shadows", "RealTimeReflection", "PostProcessing",
        "UseRippleSystem", "UseHighDetailSoftParticles", "UseDistortion",
        "UseHighDetailPostProcEffects", "FluidSurfaceDetail"]
for key in keys:
    print("\nKEY", key)
    for encoding in ("ascii", "utf-16le"):
        token = (key + "\0").encode(encoding)
        start = 0
        while (offset := pe.data.find(token, start)) >= 0:
            start = offset + 1
            section = next(s for s in pe.sections if s["rawptr"] <= offset < s["rawptr"] + s["rawsize"])
            address = pe.image_base + section["vaddr"] + offset - section["rawptr"]
            immediate = struct.pack("<I", address)
            for text in (s for s in pe.sections if s["exec"]):
                code = pe.data[text["rawptr"]:text["rawptr"] + text["rawsize"]]
                pos = 0
                while (found := code.find(immediate, pos)) >= 0:
                    pos = found + 1
                    rva = text["vaddr"] + found
                    print("xref", hex(rva), "string", hex(address - pe.image_base))
                    begin = rva - 15
                    for ins in disasm.md().disasm(pe.read(begin, 75), pe.image_base + begin):
                        print(hex(ins.address - pe.image_base), ins.mnemonic, ins.op_str)
