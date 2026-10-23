"""Read only local BPP MessagePack/LZ4 replay payloads without extra packages."""
import gzip
import json
import pathlib
import struct
import sys


class Ext:
    def __init__(self, code, payload):
        self.code, self.payload = code, payload


def map_key(value):
    if isinstance(value,list): return tuple(map_key(v) for v in value)
    return value


class Unpacker:
    def __init__(self, data):
        self.data, self.pos = data, 0

    def take(self, n):
        ans = self.data[self.pos:self.pos+n]
        self.pos += n
        return ans

    def num(self, fmt):
        return struct.unpack('>'+fmt, self.take(struct.calcsize(fmt)))[0]

    def read(self):
        code = self.take(1)[0]
        if code < 128: return code
        if code > 223: return code-256
        if 160 <= code <= 191: return self.take(code & 31).decode('utf-8')
        if 144 <= code <= 159: return [self.read() for _ in range(code & 15)]
        if 128 <= code <= 143: return {map_key(self.read()): self.read() for _ in range(code & 15)}
        if code == 192: return None
        if code == 194: return False
        if code == 195: return True
        if 196 <= code <= 198: return self.take(self.num(('B','H','I')[code-196]))
        if 199 <= code <= 201:
            n = self.num(('B','H','I')[code-199]); return Ext(self.num('b'),self.take(n))
        if code == 202: return self.num('f')
        if code == 203: return self.num('d')
        if 204 <= code <= 211: return self.num(('B','H','I','Q','b','h','i','q')[code-204])
        if 212 <= code <= 216: return Ext(self.num('b'),self.take((1,2,4,8,16)[code-212]))
        if 217 <= code <= 219: return self.take(self.num(('B','H','I')[code-217])).decode('utf-8')
        if 220 <= code <= 221: return [self.read() for _ in range(self.num(('H','I')[code-220]))]
        if 222 <= code <= 223: return {map_key(self.read()): self.read() for _ in range(self.num(('H','I')[code-222]))}
        raise ValueError(f'Unsupported MessagePack 0x{code:x}')


def lz4(data):
    pos, out = 0, bytearray()
    while pos < len(data):
        token = data[pos]; pos += 1
        literal = token >> 4
        if literal == 15:
            while True:
                b=data[pos]; pos+=1; literal+=b
                if b != 255: break
        out.extend(data[pos:pos+literal]);pos+=literal
        if pos == len(data): break
        offset=data[pos]|data[pos+1]<<8;pos+=2
        count=(token&15)+4
        if (token&15) == 15:
            while True:
                b=data[pos];pos+=1;count+=b
                if b != 255: break
        for _ in range(count): out.append(out[-offset])
    return bytes(out)


def unpack(data):
    value=Unpacker(data).read()
    if isinstance(value,list) and value and isinstance(value[0],Ext) and value[0].code == 98:
        return unpack(b''.join(lz4(v) for v in value[1:]))
    if isinstance(value,Ext) and value.code == 99:
        header=Unpacker(value.payload); header.read()
        return unpack(lz4(value.payload[header.pos:]))
    return value


def clean(value):
    if isinstance(value, Ext): return {'$ext':value.code,'payload':value.payload.hex()}
    if isinstance(value,bytes): return {'$bytes': len(value)}
    if isinstance(value,list): return [clean(v) for v in value]
    if isinstance(value,dict): return {str(k):clean(v) for k,v in value.items()}
    return value


def read_replay(path):
    outer=unpack(gzip.decompress(pathlib.Path(path).read_bytes()))
    return {key:unpack(value) if key.endswith('MessageBytes') else value for key,value in outer.items()}


if __name__ == '__main__':
    replay=read_replay(sys.argv[1])
    if len(sys.argv)>2:
        pathlib.Path(sys.argv[2]).write_text(json.dumps(clean(replay),ensure_ascii=False,indent=2),encoding='utf-8')
    else:
        print(json.dumps(clean(replay),ensure_ascii=False)[:4000])
