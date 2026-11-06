"""Dependency-free structural checks; NOT a WPF build or SQL Server integration test."""
from pathlib import Path
import base64
import hashlib
import json
import re
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[1]
for path in sorted(root.rglob('*')):
    if '.git' in path.parts or not path.is_file():
        continue
    if path.suffix in {'.xaml', '.csproj', '.pubxml', '.manifest'}:
        ET.parse(path)
        print('PASS XML:', path.relative_to(root))
    elif path.suffix == '.json':
        json.loads(path.read_text(encoding='utf-8'))
        print('PASS JSON:', path.relative_to(root))
seed = (root / 'sql/02_SeedAdmin.sql').read_text()
encoded = re.search(r'PBKDF2-SHA256\$600000\$[A-Za-z0-9+/=]+\$[A-Za-z0-9+/=]+', seed)[0]
_, iterations, salt, expected = encoded.split('$')
actual = hashlib.pbkdf2_hmac('sha256', b'Admin@123', base64.b64decode(salt), int(iterations), 32)
assert actual == base64.b64decode(expected)
assert encoded in (root / 'tests/FitohPhone.Security.Tests/Program.cs').read_text()
assert hashlib.pbkdf2_hmac('sha256', b'wrong', base64.b64decode(salt), int(iterations), 32) != actual
print('PASS: seed hash matches Admin@123, rejects wrong password, matches C# test vector')
sql = '\n'.join(p.read_text() for p in sorted((root / 'sql').glob('*.sql')))
create_table_count = len(re.findall(r'CREATE\s+TABLE\s+', sql, re.I))
assert create_table_count >= 1, f'Expected at least one CREATE TABLE, found {create_table_count}'
assert re.search(r'SET\s+COMPATIBILITY_LEVEL\s*=\s*130', sql, re.I)
print(f'PASS: {create_table_count} CREATE TABLE statement(s); explicit compatibility 130')
print('NOT RUN: .NET build, C# tests, WPF execution, SQL Server 2016 SP1, LAN acceptance, EXE publish')
