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
def export_packet(global_name, file_name):
    fixture = lua.globals()[global_name]
    if fixture is None or len(fixture) != 128:
        raise RuntimeError(f'Missing or invalid 128-byte Lua fixture: {global_name}')
    (out / file_name).write_bytes(bytes(fixture[i] for i in range(1, 129)))


export_packet('WoWProbeTestFixture', 'lua-packet.bin')
export_packet('WoWProbeCombatCoreFixture', 'lua-combat-core.bin')
export_packet('WoWProbeCombatTestFixture', 'lua-combat-packet.bin')
print('Exported real Lua 5.1 WP1 and sequence-matched WP1/WC3 packets for cross-language decoding.')
