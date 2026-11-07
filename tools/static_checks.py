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
assert len(re.findall(r'CREATE\s+TABLE\s+', sql, re.I)) == 1
assert re.search(r'SET\s+COMPATIBILITY_LEVEL\s*=\s*130', sql, re.I)
print('PASS: exactly one CREATE TABLE; explicit compatibility 130')

# --- Application shutdown: the process must exit when the last window closes ---
app_xaml = (root / 'src/FitohPhone/App.xaml').read_text(encoding='utf-8')
app_cs = (root / 'src/FitohPhone/App.xaml.cs').read_text(encoding='utf-8')
login_cs = (root / 'src/FitohPhone/Views/LoginWindow.xaml.cs').read_text(encoding='utf-8')
assert 'OnExplicitShutdown' not in app_xaml, 'WPF must not stay alive after windows close'
assert 'ShutdownMode="OnLastWindowClose"' in app_xaml
print('PASS: ShutdownMode=OnLastWindowClose; OnExplicitShutdown removed')
assert 'protected override void OnExit' in app_cs
assert 'Shutdown()' in app_cs and 'isShuttingDown' in app_cs
assert 'liveWindows.Contains(login)' in app_cs, 'a late sign-in must not resurrect a window'
assert 'CancelPendingLogins()' in app_cs
assert '.Hide()' not in app_cs, 'hidden windows keep the dispatcher alive'
assert re.search(r'main\.Show\(\);\s*login\.ClearPassword\(\);\s*login\.Close\(\);', app_cs), \
    'login swap must show the new window before closing the old one'
assert re.search(r'ShowLogin\(\);\s*main\.Close\(\);', app_cs), \
    'logout swap must show the new window before closing the old one'
assert 'lifetime.Dispose()' not in login_cs, 'token source must not be disposed inside Window.Closed'
print('PASS: idempotent shutdown, late sign-in guard, token cleanup at exit, no zero-window swaps')
# --- LAN multi-device: one settings file, many PCs ---
settings = json.loads((root / 'src/FitohPhone/connectionsettings.json').read_text(encoding='utf-8'))
assert 'Default' in settings and settings['Default'].get('Server'), 'Default.Server must be set for LAN clients'
assert settings['Default'].get('Database') == 'FitohPhone'
assert not str(settings['Default']['Server']).startswith(('.', '(local)')), 'clients must not default to localhost/LocalDB'
assert isinstance(settings.get('Machines', {}), dict), 'Machines must be an object keyed by computer name'
provider = (root / 'src/FitohPhone/Services/ConnectionSettingsProvider.cs').read_text(encoding='utf-8')
assert 'Environment.MachineName' in provider, 'device identity must come from the Windows computer name'
assert 'OrdinalIgnoreCase' in provider, 'machine-name lookup must be case-insensitive'
assert 'ApplicationName = "FitohPhone"' in provider, 'session tracking relies on program_name=FitohPhone'
assert 'Encrypt = settings.Encrypt' in provider and 'TrustServerCertificate' in provider
assert 'legacy' in provider.lower(), 'old flat single-machine settings files must still load'
assert 'Merge(sections.Shared, own)' in provider, 'a machine section must be layered over Default'
login_vm = (root / 'src/FitohPhone/ViewModels/LoginViewModel.cs').read_text(encoding='utf-8')
assert 'settings.GetProfile()' in login_vm, 'configuration errors must surface before the SQL attempt'
assert 'ConnectionSummary' in login_vm, 'the resolved LAN target must be visible on the login window'
describe = login_vm.split('private static string DescribeTarget', 1)[1]
assert 'catch (Exception)' in describe, 'the diagnostic line must never crash the window'
assert 'settings.GetProfile()' in login_vm.split('LoginCommand = new AsyncCommand', 1)[1], \
    'configuration errors must be reported before the SQL attempt'
assert 'catch (Exception)' in login_vm.split('DescribeTarget')[2], 'the diagnostic line must never crash the window'
profile_model = (root / 'src/FitohPhone/Models/ConnectionProfile.cs').read_text(encoding='utf-8')
assert 'class ConnectionProfile' in profile_model
test_src = (root / 'tests/FitohPhone.Configuration.Tests/Program.cs').read_text(encoding='utf-8')
assert 'inherits the shared SQL login' in test_src, 'the override-inheritance regression must stay covered'
assert 'Encrypt": "yes' in test_src, 'a wrong JSON value type must stay covered'
print('PASS: LAN settings resolve per computer name, merge over Default, legacy fallback, shared server target')

print('NOT RUN: .NET build, C# tests, WPF execution, SQL Server 2016 SP1, LAN acceptance, EXE publish')
