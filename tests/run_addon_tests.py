"""Run the actual Lua 5.1 subsystem tests and export its fixture for C# cross checks."""
from pathlib import Path
import sys
import os

root = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(root / '.tools'))
from lupa.lua51 import LuaRuntime

os.chdir(root)
lua = LuaRuntime(unpack_returned_tuples=True)
lua.execute((root / 'tests' / 'addon_tests.lua').read_text(encoding='utf-8'))
out = root / 'test-output'
out.mkdir(exist_ok=True)
fixture = lua.globals().WoWProbeTestFixture
(out / 'lua-packet.bin').write_bytes(bytes(fixture[i] for i in range(1, 129)))
print('Exported real Lua 5.1 packet for cross-language decoding.')
