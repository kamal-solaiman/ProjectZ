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

seed = (root / 'sql/02_SeedAdmin.sql').read_text(encoding='utf-8')
encoded = re.search(r'PBKDF2-SHA256\$600000\$[A-Za-z0-9+/=]+\$[A-Za-z0-9+/=]+', seed)[0]
_, iterations, salt, expected = encoded.split('$')
actual = hashlib.pbkdf2_hmac('sha256', b'Admin@123', base64.b64decode(salt), int(iterations), 32)
assert actual == base64.b64decode(expected)
assert encoded in (root / 'tests/ProjectZ.Security.Tests/Program.cs').read_text(encoding='utf-8')
assert hashlib.pbkdf2_hmac('sha256', b'wrong', base64.b64decode(salt), int(iterations), 32) != actual
print('PASS: seed hash matches Admin@123, rejects wrong password, matches C# test vector')

sql = '\n'.join(path.read_text(encoding='utf-8') for path in sorted((root / 'sql').glob('*.sql')))
assert len(re.findall(r'CREATE\s+TABLE\s+', sql, re.I)) == 1
assert re.search(r'SET\s+COMPATIBILITY_LEVEL\s*=\s*130', sql, re.I)
assert 'CREATE DATABASE [ProjectZ]' in sql
assert '[ProjectZClient]' in (root / 'sql/04_SetupLanSqlAuth.sql').read_text(encoding='utf-8')
print('PASS: ProjectZ database scripts; exactly one CREATE TABLE; compatibility 130')

# --- Application shutdown: closing the UI must terminate the WPF process ---
app_xaml = (root / 'src/ProjectZ/App.xaml').read_text(encoding='utf-8')
app_cs = (root / 'src/ProjectZ/App.xaml.cs').read_text(encoding='utf-8')
login_cs = (root / 'src/ProjectZ/Views/LoginWindow.xaml.cs').read_text(encoding='utf-8')
assert 'OnExplicitShutdown' not in app_xaml, 'WPF must not stay alive after windows close'
assert 'ShutdownMode="OnLastWindowClose"' in app_xaml
print('PASS: ShutdownMode=OnLastWindowClose; OnExplicitShutdown removed')
assert 'protected override void OnExit' in app_cs
assert 'Shutdown()' in app_cs and 'isShuttingDown' in app_cs
assert 'liveWindows.Contains(login)' in app_cs, 'a late sign-in must not recreate a window'
assert 'CancelPendingLogins()' in app_cs
assert 'window.Closed +=' in app_cs and 'liveWindows.Count == 0' in app_cs, 'last closed window must trigger shutdown'
assert '.Hide()' not in app_cs, 'hidden windows can keep the dispatcher alive'
assert re.search(r'main\.Show\(\);\s*login\.ClearPassword\(\);\s*login\.Close\(\);', app_cs), 'login swap must show the new window before closing the old one'
assert re.search(r'ShowLogin\(\);\s*main\.Close\(\);', app_cs), 'logout swap must show the new window before closing the old one'
assert 'lifetime.Dispose()' not in login_cs, 'do not dispose a token source while SQL may still use it'
print('PASS: guarded shutdown, late-login protection, token cleanup, and safe window swaps')

# --- Arabic UI with RTL content and LTR native window chrome ---
login_xaml = (root / 'src/ProjectZ/Views/LoginWindow.xaml').read_text(encoding='utf-8')
main_xaml = (root / 'src/ProjectZ/Views/MainWindow.xaml').read_text(encoding='utf-8')
login_root = ET.fromstring(login_xaml)
main_root = ET.fromstring(main_xaml)
for window_root in (login_root, main_root):
    assert window_root.attrib.get('FlowDirection') == 'LeftToRight', 'keep Windows caption controls on the right'
    assert window_root.attrib.get('Language') == 'ar-EG'
    assert 'ProjectZ' in window_root.attrib.get('Title', '')

window_style = next(
    style for style in ET.parse(root / 'src/ProjectZ/Resources/Theme.xaml').getroot()
    if style.tag.endswith('Style') and style.attrib.get('TargetType') == 'Window'
)
window_setters = {setter.attrib.get('Property'): setter.attrib.get('Value') for setter in window_style}
assert window_setters.get('FlowDirection') == 'LeftToRight'
text_style = next(
    style for style in ET.parse(root / 'src/ProjectZ/Resources/Theme.xaml').getroot()
    if style.tag.endswith('Style') and style.attrib.get('TargetType') == 'TextBlock'
)
text_setters = {setter.attrib.get('Property'): setter.attrib.get('Value') for setter in text_style}
assert text_setters.get('FlowDirection') == 'RightToLeft'
assert text_setters.get('TextAlignment') == 'Right'

login_content = next(element for element in login_root if element.tag.endswith('Grid'))
login_panels = [element for element in login_content if element.tag.endswith('Border')]
assert login_content.attrib.get('FlowDirection') == 'LeftToRight'  # stable physical column order
assert [panel.attrib.get('Grid.Column') for panel in login_panels] == ['0', '1']
assert all(panel.attrib.get('FlowDirection') == 'RightToLeft' for panel in login_panels)
main_content = next(element for element in main_root if element.tag.endswith('Grid'))
assert main_content.attrib.get('FlowDirection') == 'LeftToRight'  # stable physical column order
main_body = next(
    element for element in main_content
    if element.tag.endswith('Grid') and element.attrib.get('Grid.Row') == '1'
)
main_panels = [element for element in main_body if element.tag.endswith('Border')]
assert [panel.attrib.get('Grid.Column') for panel in main_panels] == ['0', '1']
assert all(panel.attrib.get('FlowDirection') == 'RightToLeft' for panel in main_panels)
logout_button = next(
    element for element in main_root.iter()
    if element.tag.endswith('Button') and element.attrib.get('Content') == 'تسجيل الخروج'
)
assert logout_button.attrib.get('FlowDirection') == 'RightToLeft'
sidebar_scrollers = [element for element in main_root.iter() if element.tag.endswith('ScrollViewer')]
assert len(sidebar_scrollers) == 1
assert sidebar_scrollers[0].attrib.get('VerticalScrollBarVisibility') == 'Auto'
assert sidebar_scrollers[0].attrib.get('HorizontalScrollBarVisibility') == 'Disabled'
assert any(element.attrib.get('Click') == 'CategoryHeader_Click' for element in main_root.iter())
assert any(element.attrib.get('Click') == 'MenuItem_Click' for element in main_root.iter())
assert 'IsSelected' in main_xaml and 'IsExpanded' in main_xaml

main_viewmodel = (root / 'src/ProjectZ/ViewModels/MainViewModel.cs').read_text(encoding='utf-8')
expected_menu = [
    ('المبيعات', ['نقطة البيع', 'اضافة منتج', 'المخزون', 'التصنيفات', 'الفواتير', 'المرتجع والاستبدال']),
    ('المشتريات', ['شراء فاتورة', 'فاتورة المشتريات']),
    ('الصيانة', ['امر صيانة', 'فواتير الصيانة', 'اجراءات الصيانة', 'قطع الغيار']),
    ('المالية', ['تحويل فودافون كاش', 'تحويل انستاباي', 'سجلات فودافون كاش', 'سجلات انستاباي', 'حساباتي']),
    ('العملاء والموردين', ['العملاء', 'الموردين']),
    ('المستخدمين', ['المستخدمين', 'الصلاحيات']),
    ('الاعدادات', ['اعدادات عامة', 'المظهر', 'النسخ الاحتياطي']),
]
menu_groups = re.findall(
    r'new NavigationMenuCategoryViewModel\("([^"]+)",\s*(.*?)\)',
    main_viewmodel,
    re.S,
)
actual_menu = [(title, re.findall(r'"([^"]+)"', items)) for title, items in menu_groups]
assert actual_menu == expected_menu, f'sidebar labels/order differ: {actual_menu}'
assert 'SelectItem(Categories[0].Items[0]);' in main_viewmodel
assert 'هذا العنصر للتحديد فقط حاليًا؛ لم تُنفذ شاشته بعد.' in main_viewmodel
assert 'itemCategory.SetExpanded(expand && ReferenceEquals(itemCategory, category));' in main_viewmodel
assert 'category.SetExpanded(ReferenceEquals(category, item.Category));' in main_viewmodel
print('PASS: post-login sidebar labels, selected state, single-open accordion and vertical scrolling')

for element in login_root.iter():
    if element.tag.endswith(('TextBox', 'PasswordBox')):
        assert element.attrib.get('FlowDirection') == 'LeftToRight', 'credential fields stay LTR'
assert 'CultureInfo.DefaultThreadCurrentCulture = arabicCulture;' in app_cs
assert 'CultureInfo.DefaultThreadCurrentUICulture = arabicCulture;' in app_cs
print('PASS: Arabic culture/content and RTL client areas; native window chrome stays LTR')
print('PASS: credential fields retain LTR input for account names and passwords')

# --- Application and database are consistently renamed ProjectZ ---
project_file = ET.parse(root / 'src/ProjectZ/ProjectZ.csproj').getroot()
project_properties = {
    child.tag: child.text for group in project_file if group.tag == 'PropertyGroup'
    for child in group
}
for property_name in ('AssemblyName', 'RootNamespace', 'Product', 'AssemblyTitle'):
    assert project_properties.get(property_name) == 'ProjectZ'
settings = json.loads((root / 'src/ProjectZ/connectionsettings.json').read_text(encoding='utf-8'))
assert settings['Database'] == 'ProjectZ'
assert settings['UserId'] == 'ProjectZClient'
assert 'ProjectZ' in (root / 'ProjectZ.sln').read_text(encoding='utf-8')
assert 'ProjectZ.exe' in (root / '.github/workflows/windows-build.yml').read_text(encoding='utf-8')
legacy_product_name = 'Fitoh' + 'Phone'
for path in (root / 'src/ProjectZ').rglob('*'):
    if path.is_file() and path.suffix in {'.cs', '.xaml', '.csproj', '.json', '.manifest'}:
        assert legacy_product_name not in path.read_text(encoding='utf-8'), f'old product name remains in {path}'
print('PASS: executable, assembly, namespaces, database and SQL login use ProjectZ')
print('NOT RUN: .NET build, C# tests, WPF execution, SQL Server 2016 SP1, LAN acceptance, EXE publish')
