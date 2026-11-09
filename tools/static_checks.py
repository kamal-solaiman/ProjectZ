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
assert main_panels[0].attrib.get('FlowDirection') == 'RightToLeft'  # Arabic workspace
assert main_panels[1].attrib.get('FlowDirection') == 'LeftToRight'  # stable sidebar layout
logout_button = next(
    element for element in main_root.iter()
    if element.tag.endswith('Button') and element.attrib.get('Content') == 'تسجيل الخروج'
)
assert logout_button.attrib.get('FlowDirection') == 'RightToLeft'
sidebar_button_style = next(
    style for style in main_root.iter()
    if style.tag.endswith('Style')
    and style.attrib.get('{http://schemas.microsoft.com/winfx/2006/xaml}Key') == 'SidebarBaseButtonStyle'
)
assert any(
    setter.attrib.get('Property') == 'HorizontalContentAlignment'
    and setter.attrib.get('Value') == 'Stretch'
    for setter in sidebar_button_style
), 'sidebar buttons must stretch to preserve a stable content area'
assert any(
    setter.attrib.get('Property') == 'FlowDirection'
    and setter.attrib.get('Value') == 'LeftToRight'
    for setter in sidebar_button_style
), 'button layout stays physical LTR while its text is RTL'
sidebar_heading = next(
    element for element in main_root.iter()
    if element.tag.endswith('TextBlock') and element.attrib.get('Text') == 'القائمة الرئيسية'
)
# NOTE: inside an RTL TextBlock, TextAlignment is flow-relative ("Right" renders on the physical LEFT),
# so sidebar labels are content-sized and pinned with HorizontalAlignment="Right" instead.
assert sidebar_heading.attrib.get('HorizontalAlignment') == 'Right'
assert sidebar_heading.attrib.get('FlowDirection') == 'RightToLeft'
assert sidebar_heading.attrib.get('TextAlignment') is None
sidebar_label_blocks = [
    element for element in main_root.iter()
    if element.tag.endswith('TextBlock') and element.attrib.get('Text') == '{Binding Title}'
]
assert len(sidebar_label_blocks) == 2
assert all(
    element.attrib.get('FlowDirection') == 'RightToLeft'
    and element.attrib.get('TextAlignment') is None
    for element in sidebar_label_blocks
), 'sidebar labels are RTL; physical placement must not depend on flow-relative TextAlignment'
item_label = next(
    element for element in sidebar_label_blocks if element.attrib.get('HorizontalAlignment') is not None
)
assert item_label.attrib.get('HorizontalAlignment') == 'Right', 'submenu labels pin to the right edge'
menu_style = next(
    style for style in main_root.iter()
    if style.tag.endswith('Style')
    and style.attrib.get('{http://schemas.microsoft.com/winfx/2006/xaml}Key') == 'SidebarMenuButtonStyle'
)
menu_padding = next(
    setter.attrib.get('Value') for setter in menu_style
    if setter.tag.endswith('Setter') and setter.attrib.get('Property') == 'Padding'
)
assert menu_padding == '12,9,50,9', 'submenu labels indent on the right, under the category title (after the icon)'
sidebar_scrollers = [element for element in main_root.iter() if element.tag.endswith('ScrollViewer')]
assert len(sidebar_scrollers) == 1
assert sidebar_scrollers[0].attrib.get('FlowDirection') == 'LeftToRight', 'place the scrollbar at the right edge'
scroller_setters = {
    setter.attrib.get('Property'): setter.attrib.get('Value')
    for style in sidebar_scrollers[0].iter()
    if style.tag.endswith('Style')
    for setter in style
    if setter.tag.endswith('Setter')
}
assert scroller_setters.get('VerticalScrollBarVisibility') == 'Auto'
assert sidebar_scrollers[0].attrib.get('HorizontalScrollBarVisibility') == 'Disabled'
sidebar_item_controls = [
    element for element in main_root.iter()
    if element.tag.endswith('ItemsControl')
    and element.attrib.get('ItemsSource') in {'{Binding Categories}', '{Binding Items}'}
]
assert len(sidebar_item_controls) == 2
assert all(element.attrib.get('FlowDirection') == 'LeftToRight' for element in sidebar_item_controls)
assert all(element.attrib.get('FlowDirection') == 'RightToLeft' for element in sidebar_label_blocks)
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
    r'new NavigationMenuCategoryViewModel\("([^"]+)",\s*"([^"]+)",\s*(.*?)\)',
    main_viewmodel,
    re.S,
)
actual_menu = [(title, re.findall(r'"([^"]+)"', items)) for title, _icon, items in menu_groups]
assert actual_menu == expected_menu, f'sidebar labels/order differ: {actual_menu}'
assert all(icon for _title, icon, _items in menu_groups), 'every category needs a rail icon'
assert 'ToggleSidebarCommand' in main_viewmodel and 'IsSidebarCollapsed' in main_viewmodel
assert 'OpenCategoryFromRail' in main_viewmodel
toggle_button = next(
    element for element in main_root.iter()
    if element.tag.endswith('Button') and element.attrib.get('Command') == '{Binding ToggleSidebarCommand}'
)
assert toggle_button.attrib.get('Style') == '{StaticResource SidebarToggleButtonStyle}'
sidebar_border = next(
    element for element in main_body
    if element.tag.endswith('Border') and element.attrib.get('Grid.Column') == '1'
)
assert any(
    node.tag.endswith('DoubleAnimation') and node.attrib.get('Storyboard.TargetProperty') == 'Width'
    for node in sidebar_border.iter()
), 'sidebar width animates between expanded and rail'
print('PASS: sidebar toggle command, animated width, icon rail')
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

# --- POS UI Design Phase (no backend/SQL) ---
pos_xaml_path = root / 'src/ProjectZ/Views/PosView.xaml'
add_customer_xaml_path = root / 'src/ProjectZ/Views/AddCustomerWindow.xaml'
pos_styles_path = root / 'src/ProjectZ/Resources/PosStyles.xaml'
assert pos_xaml_path.is_file() and add_customer_xaml_path.is_file() and pos_styles_path.is_file()
pos_xaml = pos_xaml_path.read_text(encoding='utf-8')
add_customer_xaml = add_customer_xaml_path.read_text(encoding='utf-8')
add_customer_root = ET.fromstring(add_customer_xaml)
assert add_customer_root.attrib.get('FlowDirection') == 'LeftToRight'
assert add_customer_root.attrib.get('Language') == 'ar-EG'
assert add_customer_root.attrib.get('WindowStartupLocation') == 'CenterOwner'

for token in (
    'NewInvoiceCommand',
    'BarcodeSearchInput',
    'ScanOrSearchEnterCommand',
    'OpenAddCustomerWindowCommand',
    'SetCashPresetCommand',
    'SetVodafonePresetCommand',
    'SetInstaPayPresetCommand',
    'SetSplitPresetCommand',
    'سعر قطاعي',
    'سعر مميز',
    'سعر جملة',
    'SelectedPriceTier',
):
    assert token in pos_xaml, f'POS UI missing expected token: {token}'

assert 'IsPosSelected' in main_viewmodel and 'IsPosSelected' in main_xaml
assert 'IsAddProductSelected' in main_viewmodel and 'IsAddProductSelected' in main_xaml
assert 'views:PosView' in main_xaml
assert 'views:AddProductView' in main_xaml

add_product_xaml_path = root / 'src/ProjectZ/Views/AddProductView.xaml'
assert add_product_xaml_path.is_file()
add_product_xaml = add_product_xaml_path.read_text(encoding='utf-8')
for token in (
    'ProductNameInput',
    'Barcode',
    'GenerateBarcodeCommand',
    'SelectedCategory',
    'ToggleNewCategoryCommand',
    'RetailPriceText',
    'SpecialPriceText',
    'WholesalePriceText',
    'سعر قطاعي',
    'سعر مميز',
    'سعر جملة',
    'SaveProductCommand',
    'PreviewName',
    'PreviewBarcode',
    'Width="190"',
    'Height="95"',
):
    assert token in add_product_xaml, f'AddProduct UI missing expected token: {token}'

add_product_vm_text = (root / 'src/ProjectZ/ViewModels/AddProductViewModel.cs').read_text(encoding='utf-8')
pos_models_text = (root / 'src/ProjectZ/Models/PosDesignModels.cs').read_text(encoding='utf-8')
for removed_token in (
    'Brand',
    'Sku',
    'ShelfLocation',
    'IsTaxable',
    'PreviewCategory',
    'PreviewBrandAndSku',
    'PreviewCostDisplay',
    'PreviewRetailDisplay',
    'PreviewSpecialDisplay',
    'PreviewWholesaleDisplay',
    'PreviewStockSummary',
    'PreviewLocationDisplay',
    'PreviewStatusText',
):
    assert removed_token not in add_product_xaml, f'Removed token {removed_token} still in AddProductView.xaml'
    assert removed_token not in add_product_vm_text, f'Removed token {removed_token} still in AddProductViewModel.cs'
    assert removed_token not in pos_models_text, f'Removed token {removed_token} still in PosDesignModels.cs'
for removed_label in ('الماركة', 'كود داخلي', 'موقع التخزين', 'خاضع للضريبة'):
    assert removed_label not in add_product_xaml, f'Removed label {removed_label} still in AddProductView.xaml'

assert 'private bool trackSerialNumber = true;' in add_product_vm_text, 'TrackSerialNumber must default to checked'
assert 'TrackSerialNumber = true;' in add_product_vm_text, 'ResetForm must keep TrackSerialNumber checked'
assert 'TrackSerialNumber = false;' not in add_product_vm_text, 'ResetForm must not uncheck TrackSerialNumber'
assert 'ParsedSpecial >= ParsedWholesale' in add_product_vm_text, 'Special price must be lower than Wholesale price'
assert 'قطاعي · جملة · مميز' in add_product_xaml, 'AddProduct price header must be Retail · Wholesale · Special'

# Verify AddProduct price cards RTL order (Retail right col 4 -> Wholesale center col 2 -> Special left col 0)
retail_idx = add_product_xaml.index('RetailPriceText')
wholesale_idx = add_product_xaml.index('WholesalePriceText')
special_idx = add_product_xaml.index('SpecialPriceText')
assert retail_idx < wholesale_idx < special_idx, 'Price inputs must appear in Retail -> Wholesale -> Special order'

# Verify sample catalog prices respect Retail > Wholesale > Special
for retail_m, wholesale_m, special_m in re.findall(
    r'RetailPrice\s*=\s*([0-9.]+)m,\s*WholesalePrice\s*=\s*([0-9.]+)m,\s*SpecialPrice\s*=\s*([0-9.]+)m',
    pos_models_text,
):
    assert float(retail_m) > float(wholesale_m) > float(special_m), (
        f'Sample catalog item must satisfy Retail > Wholesale > Special: {retail_m}, {wholesale_m}, {special_m}'
    )

assert 'IsInventorySelected' in main_viewmodel and 'IsInventorySelected' in main_xaml
assert 'views:InventoryView' in main_xaml

inventory_xaml_path = root / 'src/ProjectZ/Views/InventoryView.xaml'
registrations_xaml_path = root / 'src/ProjectZ/Views/ProductRegistrationsWindow.xaml'
assert inventory_xaml_path.is_file() and registrations_xaml_path.is_file()
inventory_xaml = inventory_xaml_path.read_text(encoding='utf-8')
registrations_xaml = registrations_xaml_path.read_text(encoding='utf-8')

for token in (
    'اسم المنتج',
    'باركود',
    'الكمية',
    'سعر القطاعي',
    'سعر الجملة',
    'سعر المميز',
    'الهالك',
    'أزرار التحكمات',
    'OpenRegistrationsCommand',
    'OpenSupplyCommand',
    'OpenEditCommand',
    'OpenBarcodePrintCommand',
    'OpenMarkDamagedCommand',
    'DeleteProductCommand',
    'IsDamagedTabSelected',
    'RestoreDamagedUnitCommand',
    'DeleteDamagedUnitCommand',
):
    assert token in inventory_xaml, f'Inventory UI missing expected token: {token}'

for token in (
    'ID',
    'الاسم',
    'سعر الشراء',
    'اسم المورد',
    'تاريخ التسجيل',
    'الحالة',
    'ID الفاتورة',
    'MarkUnitDamagedCommand',
    'RestoreUnitCommand',
):
    assert token in registrations_xaml, f'ProductRegistrationsWindow missing expected token: {token}'

pos_code_paths = [
    root / 'src/ProjectZ/Models/PosDesignModels.cs',
    root / 'src/ProjectZ/Models/InventoryDesignModels.cs',
    root / 'src/ProjectZ/ViewModels/PosViewModel.cs',
    root / 'src/ProjectZ/ViewModels/PosInvoiceViewModel.cs',
    root / 'src/ProjectZ/ViewModels/PosInvoiceItemViewModel.cs',
    root / 'src/ProjectZ/ViewModels/AddCustomerViewModel.cs',
    root / 'src/ProjectZ/ViewModels/AddProductViewModel.cs',
    root / 'src/ProjectZ/ViewModels/InventoryViewModel.cs',
    root / 'src/ProjectZ/ViewModels/InventoryDialogViewModels.cs',
    root / 'src/ProjectZ/Views/PosView.xaml.cs',
    root / 'src/ProjectZ/Views/AddCustomerWindow.xaml.cs',
    root / 'src/ProjectZ/Views/AddProductView.xaml.cs',
    root / 'src/ProjectZ/Views/InventoryView.xaml.cs',
    root / 'src/ProjectZ/Views/ProductRegistrationsWindow.xaml.cs',
    root / 'src/ProjectZ/Views/SupplyStockWindow.xaml.cs',
    root / 'src/ProjectZ/Views/EditProductWindow.xaml.cs',
    root / 'src/ProjectZ/Views/MarkDamagedWindow.xaml.cs',
    root / 'src/ProjectZ/Views/BarcodePrintPreviewWindow.xaml.cs',
    root / 'src/ProjectZ/Views/ConfirmActionWindow.xaml.cs',
]
for p in pos_code_paths:
    text = p.read_text(encoding='utf-8')
    assert 'SqlClient' not in text and 'SqlConnection' not in text, f'POS/Inventory design phase must not include backend SQL code: {p}'

app_resource_keys = set()
for rf in (
    root / 'src/ProjectZ/App.xaml',
    root / 'src/ProjectZ/Resources/Theme.xaml',
    root / 'src/ProjectZ/Resources/PosStyles.xaml',
):
    for elem in ET.parse(rf).getroot().iter():
        for k, v in elem.attrib.items():
            if k.endswith('Key'):
                app_resource_keys.add(v)

for xf in (root / 'src/ProjectZ').rglob('*.xaml'):
    local_keys = set(app_resource_keys)
    for elem in ET.parse(xf).getroot().iter():
        for k, v in elem.attrib.items():
            if k.endswith('Key'):
                local_keys.add(v)
    for m in re.finditer(r'\{StaticResource\s+([^\}\s]+)\}', xf.read_text(encoding='utf-8')):
        res = m.group(1)
        assert res in local_keys or res.startswith('{x:Type'), f'Undefined StaticResource {res} in {xf}'

print('PASS: POS, AddProduct & Inventory UI design views, registrations, damaged stock, modals, no backend')
print('NOT RUN: .NET build, C# tests, WPF execution, SQL Server 2016 SP1, LAN acceptance, EXE publish')
