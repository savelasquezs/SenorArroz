# Temporary reviewed patch installer; removed before the final PR. No production access.
import base64, bz2, hashlib, json, subprocess
from pathlib import Path
encoded = ''.join(Path(f'.remote-orders-patch-{part}.txt').read_text().strip() for part in (1, 2, 3))
patch = bz2.decompress(base64.b64decode(encoded))
assert hashlib.sha256(patch).hexdigest() == '4c6685a7f6f2372b826e4fd888ed942881f6d713db1f2073d462f00685726e5b', 'Patch checksum mismatch'
subprocess.run(['git', 'apply', '--unidiff-zero', '--check', '-'], input=patch, check=True)
subprocess.run(['git', 'apply', '--unidiff-zero', '-'], input=patch, check=True)
stat = subprocess.check_output(['git', 'apply', '--numstat', '-'], input=patch).decode()
paths = [line.split('\t')[-1] for line in stat.splitlines()]
fixture = Path('SenorArroz.Tests/RemoteOrderManagementTests.cs')
fixture.write_text(fixture.read_text().replace('StatusTimes = new Dictionary<string, DateTime>()', 'StatusTimes = "{}"'))
encoded_extra = Path('.remote-orders-extra.txt').read_text().strip().replace('MejsLfp', 'Mejsfp')
extra = bz2.decompress(base64.b64decode(encoded_extra))
assert hashlib.sha256(extra).hexdigest() == '934b82b300ecc9bde13bea52b40f61f2c6d7ab71d002d81668aac5d08346c191', 'Review patch checksum mismatch'
subprocess.run(['git', 'apply', '--unidiff-zero', '--check', '-'], input=extra, check=True)
subprocess.run(['git', 'apply', '--unidiff-zero', '-'], input=extra, check=True)
stat = subprocess.check_output(['git', 'apply', '--numstat', '-'], input=extra).decode()
paths += [line.split('\t')[-1] for line in stat.splitlines()]
paths = sorted(set(paths))
assert paths and all(not p.startswith(('.github/', '/', '.remote-orders')) and '..' not in p.split('/') for p in paths)
Path('.remote-orders-files.json').write_text(json.dumps(paths))
