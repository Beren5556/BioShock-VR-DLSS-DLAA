"""Read-only PE32 projection audit. Prints findings; never exports game bytes."""
import argparse
import struct
import subprocess
from pathlib import Path


class Pe:
    def __init__(self, path):
        self.data = Path(path).read_bytes()
        pe = struct.unpack_from('<I', self.data, 0x3c)[0]
        assert self.data[:2] == b'MZ' and self.data[pe:pe + 4] == b'PE\0\0'
        machine, count = struct.unpack_from('<HH', self.data, pe + 4)
        assert machine == 0x14c
        optional_size = struct.unpack_from('<H', self.data, pe + 20)[0]
        self.base = struct.unpack_from('<I', self.data, pe + 24 + 28)[0]
        self.sections = []
        for index in range(count):
            at = pe + 24 + optional_size + index * 40
            name = self.data[at:at + 8].rstrip(b'\0').decode()
            _, rva, size, offset = struct.unpack_from('<IIII', self.data, at + 8)
            flags = struct.unpack_from('<I', self.data, at + 36)[0]
            self.sections.append((name, rva, size, offset, flags))

    def matches(self, pattern, executable=False):
        for name, rva, size, offset, flags in self.sections:
            if executable and not flags & 0x20000000:
                continue
            region = self.data[offset:offset + size]
            at = region.find(pattern)
            while at >= 0:
                yield name, rva + at
                at = region.find(pattern, at + 1)

    def read(self, rva, count):
        for _, start, size, offset, _ in self.sections:
            if start <= rva and rva + count <= start + size:
                at = offset + rva - start
                return self.data[at:at + count]
        return b''


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('exe')
    parser.add_argument('operation', choices=['float', 'xref', 'dis', 'hex', 'calls', 'rtti'])
    parser.add_argument('value')
    parser.add_argument('--span', type=lambda value: int(value, 16), default=0x180)
    options = parser.parse_args()
    image = Pe(options.exe)
    if options.operation == 'float':
        for name, rva in image.matches(struct.pack('<f', float(options.value))):
            print(f'{name} RVA 0x{rva:X} VA 0x{image.base+rva:X}')
    elif options.operation == 'rtti':
        for _, name_rva in image.matches(('.?AV' + options.value + '@@\0').encode()):
            type_rva = name_rva - 8
            print(f'TypeDescriptor RVA 0x{type_rva:X}')
            for _, ref in image.matches(struct.pack('<I', image.base + type_rva)):
                col = ref - 12
                raw = image.read(col, 20)
                if len(raw) != 20 or struct.unpack_from('<I', raw)[0] != 0:
                    continue
                for _, locator_ref in image.matches(struct.pack('<I', image.base + col)):
                    vtable = locator_ref + 4
                    entries = image.read(vtable, 40)
                    if len(entries) == 40:
                        print(f'COL 0x{col:X} vtable 0x{vtable:X} entries ' +
                              ' '.join(f'0x{v-image.base:X}' for v in struct.unpack('<10I', entries)))
    elif options.operation == 'hex':
        for name, rva in image.matches(bytes.fromhex(options.value), True):
            print(f'{name} RVA 0x{rva:X}')
    elif options.operation == 'calls':
        target = int(options.value, 16)
        for name, rva, size, offset, flags in image.sections:
            if not flags & 0x20000000:
                continue
            region = image.data[offset:offset + size]
            for at in range(len(region) - 5):
                if region[at] in (0xe8, 0xe9) and rva + at + 5 + struct.unpack_from('<i', region, at + 1)[0] == target:
                    print(f'{name} call/jump RVA 0x{rva + at:X}')
    elif options.operation == 'xref':
        rva = int(options.value, 16)
        for name, site in image.matches(struct.pack('<I', image.base + rva), True):
            print(f'{name} absolute-ref RVA 0x{site:X}')
    else:
        rva = int(options.value, 16)
        dumpbin = next(Path('C:/Program Files (x86)/Microsoft Visual Studio/2022/BuildTools/VC/Tools/MSVC').glob('*/bin/Hostx64/x86/dumpbin.exe'))
        subprocess.run([str(dumpbin), '/nologo', '/disasm:nobytes',
                        f'/range:0x{image.base+rva:X},0x{image.base+rva+options.span:X}',
                        options.exe], check=True)
