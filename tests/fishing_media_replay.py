"""Optional reference replay; requires the user's two MP4 files and developer ffmpeg.

This is not a live game acceptance test. Original MP4/WAV paths are never published.
The installed program does not depend on Python or ffmpeg.
"""
from pathlib import Path
import argparse
import json
import subprocess
import uuid

root = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser()
parser.add_argument('--source-directory', type=Path, required=True)
parser.add_argument('--output-directory', type=Path, default=root/'test-output'/'fishing-reference-replay')
parser.add_argument('--ffmpeg', type=Path, default=root/'.tools'/'fishing-media'/'imageio_ffmpeg'/'binaries'/'ffmpeg-win-x86_64-v9.0.2.exe')
parser.add_argument('--test-executable', type=Path, default=root/'bin'/'FishingPerceptionTests.exe')
args = parser.parse_args()
args.output_directory.mkdir(parents=True, exist_ok=True)
output_root = args.output_directory
# Each run owns a fresh directory: shorter replacement clips must not reuse
# trailing frames from a previous run, and no existing user image is deleted.
args.output_directory = output_root/('run-'+uuid.uuid4().hex)
args.output_directory.mkdir()
names = {'splash': '水花上浮可以收杆的声音.mp4', 'bag': '背包已满的声音和游戏窗口文字显示.mp4'}
metadata = []
for key, filename in names.items():
    source = args.source_directory/filename
    if not source.is_file():
        raise FileNotFoundError(filename)
    subprocess.run([str(args.ffmpeg), '-hide_banner', '-loglevel', 'error', '-y', '-i', str(source), '-vn', '-ac', '1', '-ar', '16000', '-acodec', 'pcm_s16le', str(args.output_directory/(key+'.wav'))], check=True)
    frames = args.output_directory/(key+'-frames')
    frames.mkdir(exist_ok=True)
    subprocess.run([str(args.ffmpeg), '-hide_banner', '-loglevel', 'error', '-y', '-i', str(source), '-vf', 'fps=5', str(frames/'%03d.png')], check=True)
    metadata.append({'reference': key, 'frame_count': len(list(frames.glob('*.png'))), 'frame_sampling_fps': 5})
report = args.output_directory/'replay-report.json'
subprocess.run([str(args.test_executable), '--replay', str(args.output_directory), str(report)], check=True)
data = json.loads(report.read_text(encoding='utf-8'))
data['sampling'] = metadata
report.write_text(json.dumps(data, indent=2, ensure_ascii=False)+'\n', encoding='utf-8')
(output_root/'latest-report.json').write_text(json.dumps(data, indent=2, ensure_ascii=False)+'\n', encoding='utf-8')
print('FISHING_REFERENCE_REPLAY_OK '+str(report))
