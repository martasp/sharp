import re
import sys

ansi = re.compile(r'\x1b\[[0-9;?]*[A-Za-z]|\x1b\][^\x07]*(?:\x07|\x1b\\)')

for line in sys.stdin:
    plain = ansi.sub('', line)
    if ': warning' in plain:
        continue
    if 'warning(s)' in plain and ('succeeded' in plain or plain.startswith('Build ')):
        plain = re.sub(r'succeeded with \d+ warning\(s\)', 'succeeded', plain)
        sys.stdout.write(plain + '\n')
        continue
    sys.stdout.write(line)
