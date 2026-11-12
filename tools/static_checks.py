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
        xml_root = ET.parse(path).getroot()
        if path.suffix == '.xaml':
            for element in xml_root.iter():
                # WPF Thickness accepts 1, 2 or 4 comma-separated lengths, never 3.
                for attribute in ('Margin', 'Padding', 'BorderThickness'):
                    value = element.attrib.get(attribute, '')
                    if value and not value.startswith('{'):
                        assert len(value.split(',')) in {1, 2, 4}, (
                            f'{path.relative_to(root)}: invalid {attribute}="{value}"; WPF Thickness needs 1, 2 or 4 lengths'
                        )
                if element.tag.endswith('ProgressBar'):
                    value_binding = element.attrib.get('Value', '')
                    if value_binding.startswith('{Binding '):
                        assert 'Mode=OneWay' in value_binding, (
                            f'{path.relative_to(root)}: ProgressBar.Value must explicitly use Mode=OneWay '
                            'when bound to read-only usage percentages'
                        )
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
assert len(re.findall(r'CREATE\s+TABLE\s+', sql, re.I)) == 36
assert re.search(r'SET\s+COMPATIBILITY_LEVEL\s*=\s*130', sql, re.I)
assert 'CREATE DATABASE [ProjectZ]' in sql
assert '[ProjectZClient]' in (root / 'sql/04_SetupLanSqlAuth.sql').read_text(encoding='utf-8')
full_schema = (root / 'sql/06_CreateFullSchema.sql').read_text(encoding='utf-8')
reporting_views = (root / 'sql/07_CreateReportingViews.sql').read_text(encoding='utf-8')
expected_tables = {
    'Users', 'Roles', 'Permissions', 'UserRoles', 'RolePermissions', 'SystemSettings', 'DocumentSequences',
    'Customers', 'Suppliers', 'Categories', 'Products', 'FinancialAccounts', 'CustomerTransfers',
    'FinancialTransactions', 'InventoryUnits', 'StockMovements', 'PurchaseInvoices', 'PurchaseInvoiceLines',
    'PurchaseInvoiceLineUnits', 'PurchaseReturns', 'PurchaseReturnLines', 'PurchasePayments',
    'PurchaseInvoiceActivities', 'SalesInvoices', 'SalesInvoiceLines', 'SalesInvoiceLineUnits', 'SalesPayments',
    'SalesInvoiceActivities', 'ReturnExchanges', 'ReturnExchangeLines', 'ReturnExchangeUnits',
    'MaintenanceOrders', 'MaintenanceOrderParts', 'MaintenancePayments', 'MaintenanceActivities', 'AuditLog'
}
created_tables = set(re.findall(r'CREATE\s+TABLE\s+dbo\.([A-Za-z0-9_]+)', sql, re.I))
assert created_tables == expected_tables, f'schema table mismatch: missing={expected_tables-created_tables}, extra={created_tables-expected_tables}'
assert len(re.findall(r'CREATE\s+OR\s+ALTER\s+VIEW\s+dbo\.', reporting_views, re.I)) == 5
for required_relation in ('SalesInvoiceId', 'PurchaseInvoiceId', 'MaintenanceOrderId', 'FinancialAccountId', 'ProductId', 'ActorUserId'):
    assert required_relation in full_schema, f'missing relational key: {required_relation}'
assert 'ON DELETE CASCADE' not in full_schema.upper(), 'financial and inventory history must not be cascade-deleted'
assert 'CurrentBalance DECIMAL(19,2)' in full_schema and 'BalanceAfter DECIMAL(19,2)' in full_schema
assert 'Id BIGINT IDENTITY' not in full_schema and 'InvoiceId BIGINT' not in full_schema
assert 'LastValue BIGINT NOT NULL' in full_schema
assert "ALTER TABLE dbo.Products ADD Sku NVARCHAR(100) NULL" in full_schema
assert "THROW 50012" in full_schema and "THROW 50015" in full_schema
assert 'TransferGroupId UNIQUEIDENTIFIER' in full_schema
transfer_table = full_schema.split('CREATE TABLE dbo.CustomerTransfers', 1)[1].split('CREATE TABLE dbo.FinancialTransactions', 1)[0]
assert 'Reference' not in transfer_table and 'المرجع' not in transfer_table
assert '01_CreateDatabase.sql' in (root / 'docs/DATABASE.md').read_text(encoding='utf-8')
schema_verification = (root / 'sql/08_VerifyFullSchema.sql').read_text(encoding='utf-8')
assert 'SELECT expected.v AS MissingView' in schema_verification
assert 'SELECT v.name AS MissingView' not in schema_verification
print('PASS: complete ProjectZ database schema, 36 related tables, five reporting views, compatibility 130')

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
    ('المشتريات', ['شراء فاتورة', 'فواتير المشتريات']),
    ('الصيانة', ['امر صيانة', 'فواتير الصيانة', 'اجراءات الصيانة', 'قطع الغيار']),
    ('المالية', ['حساباتي', 'تحويل فودافون كاش', 'تحويل انستاباي', 'سجلات فودافون كاش', 'سجلات انستاباي']),
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

assert 'IsCategoriesSelected' in main_viewmodel and 'IsCategoriesSelected' in main_xaml
assert 'views:CategoriesView' in main_xaml
assert 'sharedCategories' in main_viewmodel and 'ProductCategories' in main_viewmodel

categories_xaml_path = root / 'src/ProjectZ/Views/CategoriesView.xaml'
assert categories_xaml_path.is_file()
categories_xaml = categories_xaml_path.read_text(encoding='utf-8')
for token in (
    'اسم التصنيف',
    'NewCategoryName',
    'AddCategoryCommand',
    'FilteredCategories',
    'BeginEditCommand',
    'SaveEditCommand',
    'CancelEditCommand',
    'DeleteCommand',
):
    assert token in categories_xaml, f'Categories UI missing expected token: {token}'

assert 'IsInvoicesSelected' in main_viewmodel and 'IsInvoicesSelected' in main_xaml
assert 'views:InvoicesView' in main_xaml
assert 'RecordCompletedPosSale' in main_viewmodel

invoices_xaml_path = root / 'src/ProjectZ/Views/InvoicesView.xaml'
assert invoices_xaml_path.is_file()
invoices_xaml = invoices_xaml_path.read_text(encoding='utf-8')
for token in (
    'رقم الفاتورة',
    'التاريخ والوقت',
    'العميل',
    'الكاشير',
    'الأصناف / القطع',
    'الإجمالي',
    'الخصم',
    'الصافي',
    'طريقة الدفع',
    'الحالة',
    'OpenPrintPreviewCommand',
    'OpenReturnDialogCommand',
    'OpenCollectPaymentCommand',
    'CancelInvoiceCommand',
    'SerialSummary',
    'ActivityLog',
):
    assert token in invoices_xaml, f'Invoices UI missing expected token: {token}'

assert 'IsReturnExchangeSelected' in main_viewmodel and 'IsReturnExchangeSelected' in main_xaml
assert 'views:ReturnExchangeView' in main_xaml
assert 'sharedReturnOperations' in main_viewmodel

return_exchange_xaml_path = root / 'src/ProjectZ/Views/ReturnExchangeView.xaml'
assert return_exchange_xaml_path.is_file()
return_exchange_xaml = return_exchange_xaml_path.read_text(encoding='utf-8')
for token in (
    'IsWorkspaceTabSelected',
    'IsHistoryTabSelected',
    'SearchText',
    'SelectDatePresetCommand',
    'IsReturnMode',
    'IsExchangeMode',
    'GoodReturnQtyDisplay',
    'DamagedReturnQtyDisplay',
    'ReplacementItems',
    'FilteredReplacementCatalog',
    'NeedsCustomerRefund',
    'NeedsCustomerPayment',
    'ConfirmOperationCommand',
    'OpenOperationInvoiceCommand',
    'PrintSelectedInvoiceCommand',
):
    assert token in return_exchange_xaml, f'ReturnExchange UI missing expected token: {token}'

assert 'IsPurchaseEntrySelected' in main_viewmodel and 'IsPurchaseEntrySelected' in main_xaml
assert 'views:PurchaseEntryView' in main_xaml
assert 'sharedSuppliers' in main_viewmodel and 'sharedPurchaseInvoices' in main_viewmodel

purchase_entry_xaml_path = root / 'src/ProjectZ/Views/PurchaseEntryView.xaml'
add_supplier_xaml_path = root / 'src/ProjectZ/Views/AddSupplierWindow.xaml'
purchase_print_xaml_path = root / 'src/ProjectZ/Views/PurchaseInvoicePrintPreviewWindow.xaml'
assert purchase_entry_xaml_path.is_file() and add_supplier_xaml_path.is_file() and purchase_print_xaml_path.is_file()

purchase_entry_xaml = purchase_entry_xaml_path.read_text(encoding='utf-8')
for token in (
    'OpenAddSupplierWindowCommand',
    'SelectSupplierCommand',
    'SetCashPresetCommand',
    'SetVodafonePresetCommand',
    'SetInstaPayPresetCommand',
    'SetSplitPresetCommand',
    'SetFullCreditPresetCommand',
    'UnitPurchaseCostText',
    'QuantityText',
    'RemainingPayableDisplay',
    'CompletePurchaseInvoiceCommand',
    'CompleteAndPrintPurchaseInvoiceCommand',
    'PreviewDraftCommand',
    'PreviewLastSavedInvoiceCommand',
):
    assert token in purchase_entry_xaml, f'PurchaseEntry UI missing expected token: {token}'

assert 'IsPurchaseInvoicesSelected' in main_viewmodel and 'IsPurchaseInvoicesSelected' in main_xaml
assert 'views:PurchaseInvoicesView' in main_xaml

purchase_invoices_xaml_path = root / 'src/ProjectZ/Views/PurchaseInvoicesView.xaml'
purchase_details_xaml_path = root / 'src/ProjectZ/Views/PurchaseInvoiceDetailsWindow.xaml'
edit_purchase_xaml_path = root / 'src/ProjectZ/Views/EditPurchaseInvoiceWindow.xaml'
purchase_return_xaml_path = root / 'src/ProjectZ/Views/PurchaseReturnWindow.xaml'
pay_supplier_xaml_path = root / 'src/ProjectZ/Views/PaySupplierInvoiceWindow.xaml'
assert all(p.is_file() for p in (
    purchase_invoices_xaml_path,
    purchase_details_xaml_path,
    edit_purchase_xaml_path,
    purchase_return_xaml_path,
    pay_supplier_xaml_path,
))

purchase_invoices_xaml = purchase_invoices_xaml_path.read_text(encoding='utf-8')
for token in (
    'SearchText',
    'SelectDatePresetCommand',
    'SelectedSupplierFilter',
    'SelectedPaymentFilter',
    'SelectedStatusFilter',
    'SelectedSortOption',
    'OpenInvoiceDetailsCommand',
    'OpenEditInvoiceCommand',
    'OpenReturnDialogCommand',
    'OpenPrintPreviewCommand',
    'FilterCreditOnlyCommand',
):
    assert token in purchase_invoices_xaml, f'PurchaseInvoices UI missing expected token: {token}'

purchase_details_xaml = purchase_details_xaml_path.read_text(encoding='utf-8')
for token in (
    'PrintPreviewCommand',
    'PayRemainingCommand',
    'EditInvoiceCommand',
    'ReturnToSupplierCommand',
    'CancelInvoiceCommand',
    'DeleteInvoiceCommand',
    'LineRows',
    'ActivityLog',
):
    assert token in purchase_details_xaml, f'PurchaseInvoiceDetailsWindow missing expected token: {token}'

maintenance_intake_xaml_path = root / 'src/ProjectZ/Views/MaintenanceIntakeView.xaml'
maintenance_receipt_xaml_path = root / 'src/ProjectZ/Views/MaintenanceReceiptPreviewWindow.xaml'
assert maintenance_intake_xaml_path.is_file() and maintenance_receipt_xaml_path.is_file()
assert 'IsMaintenanceIntakeSelected' in main_viewmodel and 'MaintenanceIntake' in main_viewmodel
assert 'sharedCustomers' in main_viewmodel and 'sharedMaintenanceOrders' in main_viewmodel
assert 'views:MaintenanceIntakeView' in main_xaml

maintenance_intake_xaml = maintenance_intake_xaml_path.read_text(encoding='utf-8')
for token in (
    'OpenAddCustomerWindowCommand',
    'DeviceTypeOptions',
    'DeviceBrands',
    'DeviceModel',
    'SerialOrImei',
    'PasscodeValue',
    'CommonFaults',
    'ReportedFault',
    'ConditionChecklist',
    'AccessoryChecklist',
    'EstimatedCostText',
    'IsEstimatePendingInspection',
    'SaveOrderCommand',
    'SaveAndPrintCommand',
    'FilteredRecentOrders',
    'PreviewOrderReceiptCommand',
    'CopyCustomerForNewOrderCommand',
):
    assert token in maintenance_intake_xaml, f'MaintenanceIntake UI missing expected token: {token}'

maintenance_receipt_xaml = maintenance_receipt_xaml_path.read_text(encoding='utf-8')
for token in (
    'SelectCustomerReceiptModeCommand',
    'SelectDeviceStickerModeCommand',
    'SelectDeliveryReceiptModeCommand',
    'IsCustomerReceiptMode',
    'IsDeviceStickerMode',
    'IsDeliveryReceiptMode',
    'OrderNumber',
    'ReceivedDateDisplay',
    'CustomerName',
):
    assert token in maintenance_receipt_xaml, f'MaintenanceReceiptPreviewWindow missing expected token: {token}'

maintenance_actions_xaml_path = root / 'src/ProjectZ/Views/MaintenanceActionsView.xaml'
maintenance_delivery_xaml_path = root / 'src/ProjectZ/Views/MaintenanceDeliveryWindow.xaml'
maintenance_cancel_xaml_path = root / 'src/ProjectZ/Views/MaintenanceCancelOrderWindow.xaml'
assert all(
    p.is_file()
    for p in (
        maintenance_actions_xaml_path,
        maintenance_delivery_xaml_path,
        maintenance_cancel_xaml_path,
    )
)
assert 'IsMaintenanceActionsSelected' in main_viewmodel and 'MaintenanceActions' in main_viewmodel
assert 'views:MaintenanceActionsView' in main_xaml

maintenance_actions_xaml = maintenance_actions_xaml_path.read_text(encoding='utf-8')
for token in (
    'StageFilterOptions',
    'SelectedStageFilter',
    'FilteredOrders',
    'SelectedOrder',
    'AdvanceToNextStageCommand',
    'SetDirectStatusCommand',
    'ReopenOrderCommand',
    'OpenDeliveryDialogCommand',
    'OpenCancelDialogCommand',
    'OpenReceiptPreviewCommand',
    'EditDiagnosisNotes',
    'AddCatalogPartDirectCommand',
    'AddManualPartCommand',
    'RemoveUsedPartCommand',
    'EditLaborCostText',
    'SaveWorkshopUpdatesCommand',
    'CollectExtraDepositCommand',
    'الفحص وتفاصيل الاستلام',
    'قطع الغيار والتسعير',
    'المالية والضمان',
    'السجل الزمني',
):
    assert token in maintenance_actions_xaml, f'MaintenanceActions UI missing expected token: {token}'
assert 'ExtraDepositReference' not in maintenance_actions_xaml, 'Reference input should not appear in extra deposit collection'

maintenance_invoices_xaml_path = root / 'src/ProjectZ/Views/MaintenanceInvoicesView.xaml'
maintenance_invoice_details_xaml_path = root / 'src/ProjectZ/Views/MaintenanceInvoiceDetailsWindow.xaml'
assert maintenance_invoices_xaml_path.is_file() and maintenance_invoice_details_xaml_path.is_file()
assert 'IsMaintenanceInvoicesSelected' in main_viewmodel and 'MaintenanceInvoices' in main_viewmodel
assert 'views:MaintenanceInvoicesView' in main_xaml

maintenance_invoices_xaml = maintenance_invoices_xaml_path.read_text(encoding='utf-8')
for token in (
    'InvoicesCountDisplay',
    'TotalServiceValueDisplay',
    'TotalNetProfitDisplay',
    'TotalCollectedAllDisplay',
    'TotalRemainingDisplay',
    'SelectDateBasisCommand',
    'SelectDatePresetCommand',
    'CustomFromDate',
    'CustomToDate',
    'StatusFilterOptions',
    'PriorityFilterOptions',
    'TechnicianFilterOptions',
    'PaymentFilterOptions',
    'SettlementFilterOptions',
    'SortOptions',
    'FilteredOrders',
    'OpenInvoiceDetailsCommand',
    'OpenPrintPreviewCommand',
    'OpenDeliverDialogCommand',
    'OpenInMaintenanceActionsCommand',
):
    assert token in maintenance_invoices_xaml, f'MaintenanceInvoices UI missing expected token: {token}'

maintenance_invoice_details_xaml = maintenance_invoice_details_xaml_path.read_text(encoding='utf-8')
for token in (
    'PrintPreviewCommand',
    'DeliverAndCollectCommand',
    'OpenInActionsCommand',
    'Order.UsedParts',
    'Order.ActivityLog',
    'ProfitMarginDisplay',
):
    assert token in maintenance_invoice_details_xaml, f'MaintenanceInvoiceDetailsWindow missing expected token: {token}'

maintenance_spare_parts_xaml_path = root / 'src/ProjectZ/Views/MaintenanceSparePartsView.xaml'
assert maintenance_spare_parts_xaml_path.is_file()
assert 'IsMaintenanceSparePartsSelected' in main_viewmodel and 'MaintenanceSpareParts' in main_viewmodel
assert 'views:MaintenanceSparePartsView' in main_xaml

maintenance_spare_parts_xaml = maintenance_spare_parts_xaml_path.read_text(encoding='utf-8')
for token in (
    'TotalPartsCountDisplay',
    'TotalQuantityUnitsDisplay',
    'TotalCostValueDisplay',
    'LowOrOutCountDisplay',
    'FormHeaderTitle',
    'SaveButtonText',
    'PartName',
    'CategoryOptions',
    'SelectedCategory',
    'CustomCategoryName',
    'QuantityText',
    'CostPriceText',
    'SellPriceText',
    'PreviewProfitDisplay',
    'SavePartCommand',
    'CancelEditCommand',
    'BeginEditCommand',
    'DeletePartCommand',
    'SearchText',
    'CategoryFilterOptions',
    'SelectedCategoryFilter',
    'FilteredSpareParts',
    'GoToActionsCommand',
):
    assert token in maintenance_spare_parts_xaml, f'MaintenanceSpareParts UI missing expected token: {token}'

maintenance_actions_vm_text = (root / 'src/ProjectZ/ViewModels/MaintenanceActionsViewModel.cs').read_text(encoding='utf-8')
assert 'ObservableCollection<MaintenanceSparePartItem>' in maintenance_actions_vm_text
assert 'InventoryUnitRegistrationItem' not in maintenance_actions_vm_text, 'MaintenanceActions must use dedicated maintenance spare parts, not POS/Inventory registrations'

customers_xaml_path = root / 'src/ProjectZ/Views/CustomersView.xaml'
assert customers_xaml_path.is_file()
assert 'IsCustomersSelected' in main_viewmodel and 'Customers' in main_viewmodel
assert 'views:CustomersView' in main_xaml

customers_xaml = customers_xaml_path.read_text(encoding='utf-8')
for token in (
    'TotalCustomersCountDisplay',
    'TotalBusinessValueDisplay',
    'TotalCollectedAllDisplay',
    'TotalRemainingAllDisplay',
    'FilteredCustomers',
    'SelectedCustomerEntry',
    'AddCustomerCommand',
    'EditSelectedCustomerCommand',
    'DeleteSelectedCustomerCommand',
    'StartSaleForCustomerCommand',
    'StartMaintenanceForCustomerCommand',
    'SelectedCustomerSalesInvoices',
    'SelectedCustomerMaintenanceOrders',
    'CollectSalesInvoiceCommand',
    'PrintSalesInvoiceCommand',
    'DeliverAndCollectMaintenanceCommand',
    'OpenMaintenanceDetailsCommand',
    'OpenMaintenanceInActionsCommand',
):
    assert token in customers_xaml, f'Customers UI missing expected token: {token}'

suppliers_xaml_path = root / 'src/ProjectZ/Views/SuppliersView.xaml'
suppliers_vm_path = root / 'src/ProjectZ/ViewModels/SuppliersViewModel.cs'
assert suppliers_xaml_path.is_file() and suppliers_vm_path.is_file()
assert 'IsSuppliersSelected' in main_viewmodel and 'SuppliersViewModel' in main_viewmodel
assert 'views:SuppliersView' in main_xaml
suppliers_xaml = suppliers_xaml_path.read_text(encoding='utf-8')
for token in (
    'TotalSuppliersCountDisplay', 'TotalPurchasesDisplay', 'TotalPaidAllDisplay',
    'TotalOutstandingAllDisplay', 'FilteredSuppliers', 'SelectedSupplierEntry',
    'AddSupplierCommand', 'EditSelectedSupplierCommand', 'DeleteSelectedSupplierCommand',
    'StartPurchaseForSupplierCommand', 'PayOldestInvoiceCommand',
    'SelectedSupplierInvoices', 'SelectedSupplierStatement', 'PayInvoiceCommand',
    'ReturnInvoiceCommand', 'OpenInvoiceDetailsCommand', 'PrintInvoiceCommand',
):
    assert token in suppliers_xaml, f'Suppliers UI missing expected token: {token}'
suppliers_vm_text = suppliers_vm_path.read_text(encoding='utf-8')
for token in ('BuildStatement', 'RunningBalance', 'ReturnedGoodsTotal', 'NetCashOutflow', 'RemainingPayable'):
    assert token in suppliers_vm_text, f'Suppliers ledger missing expected calculation: {token}'
supplier_model_text = (root / 'src/ProjectZ/Models/PurchaseDesignModels.cs').read_text(encoding='utf-8')
for token in ('Specialization', 'VodafoneCashNumber', 'InstaPayAddress'):
    assert token in supplier_model_text
print('PASS: Suppliers directory, 360 profile, invoices, payments/returns and chronological debit/credit statement')

accounts_xaml_path = root / 'src/ProjectZ/Views/MyAccountsView.xaml'
accounts_vm_path = root / 'src/ProjectZ/ViewModels/MyAccountsViewModel.cs'
accounts_model_path = root / 'src/ProjectZ/Models/FinanceDesignModels.cs'
assert accounts_xaml_path.is_file() and accounts_vm_path.is_file() and accounts_model_path.is_file()
assert 'IsMyAccountsSelected' in main_viewmodel and 'MyAccountsViewModel' in main_viewmodel
assert 'views:MyAccountsView' in main_xaml
accounts_xaml = accounts_xaml_path.read_text(encoding='utf-8')
for token in (
    'CashAccount.BalanceDisplay', 'NewCashTransactionCommand', 'TransferFromCashCommand',
    'AddVodafoneWalletCommand', 'AddInstaPayWalletCommand', 'FilteredWallets',
    'SelectedAccount.DailyDepositUsagePercent', 'SelectedAccount.DailyWithdrawalUsagePercent',
    'SelectedAccount.MonthlyDepositUsagePercent', 'SelectedAccount.MonthlyWithdrawalUsagePercent',
    'SelectedAccountTransactions', 'EditSelectedAccountCommand', 'ToggleSelectedAccountCommand',
    'Scrollable workspace for small laptop screens', 'MinHeight="620"',
    'Grid.Column="1" Text="{Binding DailyDepositUsagePercent',
    'Grid.Column="1" Text="{Binding DailyWithdrawalUsagePercent',
):
    assert token in accounts_xaml, f'MyAccounts UI missing expected token: {token}'
accounts_vm_text = accounts_vm_path.read_text(encoding='utf-8')
accounts_dialogs_text = (root / 'src/ProjectZ/ViewModels/AccountDialogViewModels.cs').read_text(encoding='utf-8')
for token in ('ValidateLimits', 'DailyDepositLimit', 'DailyWithdrawalLimit', 'MonthlyDepositLimit', 'MonthlyWithdrawalLimit', 'RecalculateUsage'):
    assert token in accounts_dialogs_text or token in accounts_model_path.read_text(encoding='utf-8')
assert 'OpenTransferRequested' in accounts_vm_text and 'OpenTransactionRequested' in accounts_vm_text
print('PASS: MyAccounts cash, independent wallets, four limits, transfers and ledger UI')

pos_code_paths = [
    root / 'src/ProjectZ/Models/PosDesignModels.cs',
    root / 'src/ProjectZ/Models/FinanceDesignModels.cs',
    root / 'src/ProjectZ/Models/InventoryDesignModels.cs',
    root / 'src/ProjectZ/Models/InvoiceDesignModels.cs',
    root / 'src/ProjectZ/Models/ReturnExchangeDesignModels.cs',
    root / 'src/ProjectZ/Models/PurchaseDesignModels.cs',
    root / 'src/ProjectZ/Models/MaintenanceDesignModels.cs',
    root / 'src/ProjectZ/ViewModels/PosViewModel.cs',
    root / 'src/ProjectZ/ViewModels/PosInvoiceViewModel.cs',
    root / 'src/ProjectZ/ViewModels/PosInvoiceItemViewModel.cs',
    root / 'src/ProjectZ/ViewModels/AddCustomerViewModel.cs',
    root / 'src/ProjectZ/ViewModels/CustomersViewModel.cs',
    root / 'src/ProjectZ/ViewModels/AddProductViewModel.cs',
    root / 'src/ProjectZ/ViewModels/AddSupplierViewModel.cs',
    root / 'src/ProjectZ/ViewModels/SuppliersViewModel.cs',
    root / 'src/ProjectZ/ViewModels/MyAccountsViewModel.cs',
    root / 'src/ProjectZ/ViewModels/AccountDialogViewModels.cs',
    root / 'src/ProjectZ/ViewModels/InventoryViewModel.cs',
    root / 'src/ProjectZ/ViewModels/InventoryDialogViewModels.cs',
    root / 'src/ProjectZ/ViewModels/CategoriesViewModel.cs',
    root / 'src/ProjectZ/ViewModels/InvoicesViewModel.cs',
    root / 'src/ProjectZ/ViewModels/InvoiceDialogViewModels.cs',
    root / 'src/ProjectZ/ViewModels/ReturnExchangeViewModel.cs',
    root / 'src/ProjectZ/ViewModels/PurchaseInvoiceItemViewModel.cs',
    root / 'src/ProjectZ/ViewModels/PurchaseDraftInvoiceViewModel.cs',
    root / 'src/ProjectZ/ViewModels/PurchaseEntryViewModel.cs',
    root / 'src/ProjectZ/ViewModels/PurchaseInvoicePrintPreviewViewModel.cs',
    root / 'src/ProjectZ/ViewModels/PurchaseInvoiceDialogViewModels.cs',
    root / 'src/ProjectZ/ViewModels/PurchaseInvoicesViewModel.cs',
    root / 'src/ProjectZ/ViewModels/MaintenanceIntakeViewModel.cs',
    root / 'src/ProjectZ/ViewModels/MaintenanceInvoicesViewModel.cs',
    root / 'src/ProjectZ/ViewModels/MaintenanceInvoiceDetailsViewModel.cs',
    root / 'src/ProjectZ/ViewModels/MaintenanceActionsViewModel.cs',
    root / 'src/ProjectZ/ViewModels/MaintenanceActionsDialogViewModels.cs',
    root / 'src/ProjectZ/ViewModels/MaintenanceSparePartsViewModel.cs',
    root / 'src/ProjectZ/ViewModels/MaintenanceReceiptPreviewViewModel.cs',
    root / 'src/ProjectZ/Views/PosView.xaml.cs',
    root / 'src/ProjectZ/Views/AddCustomerWindow.xaml.cs',
    root / 'src/ProjectZ/Views/CustomersView.xaml.cs',
    root / 'src/ProjectZ/Views/AddSupplierWindow.xaml.cs',
    root / 'src/ProjectZ/Views/SuppliersView.xaml.cs',
    root / 'src/ProjectZ/Views/MyAccountsView.xaml.cs',
    root / 'src/ProjectZ/Views/AccountEditorWindow.xaml.cs',
    root / 'src/ProjectZ/Views/AccountTransactionWindow.xaml.cs',
    root / 'src/ProjectZ/Views/AccountTransferWindow.xaml.cs',
    root / 'src/ProjectZ/Views/AddProductView.xaml.cs',
    root / 'src/ProjectZ/Views/InventoryView.xaml.cs',
    root / 'src/ProjectZ/Views/CategoriesView.xaml.cs',
    root / 'src/ProjectZ/Views/InvoicesView.xaml.cs',
    root / 'src/ProjectZ/Views/ReturnExchangeView.xaml.cs',
    root / 'src/ProjectZ/Views/PurchaseEntryView.xaml.cs',
    root / 'src/ProjectZ/Views/PurchaseInvoicesView.xaml.cs',
    root / 'src/ProjectZ/Views/PurchaseInvoiceDetailsWindow.xaml.cs',
    root / 'src/ProjectZ/Views/EditPurchaseInvoiceWindow.xaml.cs',
    root / 'src/ProjectZ/Views/PurchaseReturnWindow.xaml.cs',
    root / 'src/ProjectZ/Views/PaySupplierInvoiceWindow.xaml.cs',
    root / 'src/ProjectZ/Views/PurchaseInvoicePrintPreviewWindow.xaml.cs',
    root / 'src/ProjectZ/Views/MaintenanceIntakeView.xaml.cs',
    root / 'src/ProjectZ/Views/MaintenanceInvoicesView.xaml.cs',
    root / 'src/ProjectZ/Views/MaintenanceInvoiceDetailsWindow.xaml.cs',
    root / 'src/ProjectZ/Views/MaintenanceActionsView.xaml.cs',
    root / 'src/ProjectZ/Views/MaintenanceSparePartsView.xaml.cs',
    root / 'src/ProjectZ/Views/MaintenanceDeliveryWindow.xaml.cs',
    root / 'src/ProjectZ/Views/MaintenanceCancelOrderWindow.xaml.cs',
    root / 'src/ProjectZ/Views/MaintenanceReceiptPreviewWindow.xaml.cs',
    root / 'src/ProjectZ/Views/InvoicePrintPreviewWindow.xaml.cs',
    root / 'src/ProjectZ/Views/InvoiceReturnWindow.xaml.cs',
    root / 'src/ProjectZ/Views/CollectInvoicePaymentWindow.xaml.cs',
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
    parsed_xf = ET.parse(xf).getroot()
    for elem in parsed_xf.iter():
        tag_short = elem.tag.split('}')[-1]
        for child in elem:
            ctag = child.tag.split('}')[-1]
            if ctag.startswith(f'{tag_short}.'):
                prop_name = ctag.split('.', 1)[1]
                assert prop_name not in elem.attrib, (
                    f'Duplicate XAML property {prop_name} set as both attribute and property element on {tag_short} in {xf}'
                )
        for k, v in elem.attrib.items():
            if k.endswith('Key'):
                local_keys.add(v)
    for m in re.finditer(r'\{StaticResource\s+([^\}\s]+)\}', xf.read_text(encoding='utf-8')):
        res = m.group(1)
        assert res in local_keys or res.startswith('{x:Type'), f'Undefined StaticResource {res} in {xf}'

vodafone_vm = (root / 'src/ProjectZ/ViewModels/VodafoneTransferViewModel.cs').read_text(encoding='utf-8')
vodafone_view = (root / 'src/ProjectZ/Views/VodafoneTransferView.xaml').read_text(encoding='utf-8')
vodafone_window = (root / 'src/ProjectZ/Views/VodafoneOperationWindow.xaml').read_text(encoding='utf-8')
assert 'a.Kind == FinancialAccountKind.VodafoneCash' in vodafone_vm
assert 'wallet.Balance += IsDepositSelected ? -amount : amount;' in vodafone_vm
assert 'IsDepositSelected ? amount + commission : commission - amount' in vodafone_vm
assert 'incoming: !IsDepositSelected' in vodafone_vm
assert 'ValidateLimits(SelectedWallet' in vodafone_vm
assert 'accounts.CashAccount.Balance += cashDelta;' in vodafone_vm
assert 'normalizedCustomer.Length != 11' in vodafone_vm
for removed_summary in ('أرصدة خطوط فودافون', 'إيداعات العملاء اليوم', 'سحوبات العملاء اليوم', 'عمولات اليوم', 'VfKpi'):
    assert removed_summary not in vodafone_view, f'removed Vodafone summary card is still present: {removed_summary}'
for field in ('CustomerNumber', 'AmountText', 'CommissionText', 'WalletBalanceAfterPreview', 'CashBalanceAfterPreview'):
    assert field in vodafone_window, f'missing operation window field: {field}'
for field in ('DailyDepositLimitDisplay', 'DailyWithdrawalLimitDisplay', 'MonthlyDepositLimitDisplay', 'MonthlyWithdrawalLimitDisplay', 'WalletBalanceAfterDisplay', 'ActorName'):
    assert field in vodafone_view, f'missing wallet/history field: {field}'
assert 'new VodafoneTransferViewModel(MyAccounts, user.DisplayName)' in main_viewmodel
assert 'views:VodafoneTransferView' in main_xaml
print('PASS: Vodafone customer transfers share wallet state, enforce limits, and apply cash/commission deltas')

instapay_vm = (root / 'src/ProjectZ/ViewModels/InstapayTransferViewModel.cs').read_text(encoding='utf-8')
instapay_view = (root / 'src/ProjectZ/Views/InstapayTransferView.xaml').read_text(encoding='utf-8')
instapay_window = (root / 'src/ProjectZ/Views/InstapayOperationWindow.xaml').read_text(encoding='utf-8')
instapay_models = (root / 'src/ProjectZ/Models/InstapayTransferDesignModels.cs').read_text(encoding='utf-8')
assert 'a.Kind == FinancialAccountKind.InstaPay' in instapay_vm
assert 'SelectedWallet.Kind != FinancialAccountKind.InstaPay' in instapay_vm
for required_rule in ('wallet.Balance += IsDepositSelected ? -amount : amount;', 'IsDepositSelected ? amount + commission : commission - amount', 'incoming: !IsDepositSelected', 'ValidateLimits(SelectedWallet', 'accounts.CashAccount.Balance += cashDelta;'):
    assert required_rule in instapay_vm, f'missing InstaPay transfer rule: {required_rule}'
for forbidden_reference in ('Vodafone', 'فودافون', 'VF-'):
    assert forbidden_reference not in instapay_vm + instapay_view + instapay_window + instapay_models, f'InstaPay screen must not depend on Vodafone: {forbidden_reference}'
for field in ('CustomerNumber', 'AmountText', 'CommissionText', 'WalletBalanceAfterPreview', 'CashBalanceAfterPreview'):
    assert field in instapay_window, f'missing InstaPay operation field: {field}'
assert 'projectz@instapay' in instapay_models and 'reserve@instapay' in instapay_models
assert 'IP-' not in instapay_vm and 'IP-' not in instapay_models
assert 'new InstapayTransferViewModel(MyAccounts, user.DisplayName)' in main_viewmodel
assert 'views:InstapayTransferView' in main_xaml
print('PASS: independent InstaPay customer transfers, accounts, history, limits, and cash/commission deltas')

vodafone_records_vm = (root / 'src/ProjectZ/ViewModels/VodafoneRecordsViewModel.cs').read_text(encoding='utf-8')
vodafone_records_view = (root / 'src/ProjectZ/Views/VodafoneRecordsView.xaml').read_text(encoding='utf-8')
assert '<DataGrid' not in vodafone_records_view, 'Vodafone records use detailed RTL cards rather than a DataGrid'
assert 'ItemsControl ItemsSource="{Binding FilteredOperations}" FlowDirection="RightToLeft"' in vodafone_records_view
assert 'RecordCard_MouseLeftButtonUp' in vodafone_records_view
for filter_name in ('FromDate', 'ToDate', 'SelectedWalletId', 'SelectedKind', 'SelectedStatus', 'CustomerFilter', 'UserFilter', 'MinimumAmountText', 'MaximumAmountText', 'MinimumCommissionText', 'MaximumCommissionText'):
    assert filter_name in vodafone_records_vm and filter_name in vodafone_records_view, f'missing Vodafone record filter: {filter_name}'
for control in ('ShowOperationDetailsCommand', 'BeginOperationEditCommand', 'ReverseOperationCommand', 'DeleteOperationCommand', 'CreateCsvLines', 'ApplyEffect', 'CanUndo', 'ApplyEdit'):
    assert control in vodafone_records_vm, f'missing Vodafone record control: {control}'
for file_name in ('VodafoneRecordDetailsWindow.xaml', 'VodafoneRecordEditWindow.xaml'):
    assert (root / 'src/ProjectZ/Views' / file_name).is_file(), f'missing Vodafone record dialog: {file_name}'
assert 'new VodafoneRecordsViewModel(VodafoneTransfer, MyAccounts, user.DisplayName)' in main_viewmodel
assert 'views:VodafoneRecordsView' in main_xaml
print('PASS: detailed Vodafone records, comprehensive filters, edit/reverse/delete controls, CSV and print UI')

instapay_records_vm = (root / 'src/ProjectZ/ViewModels/InstapayRecordsViewModel.cs').read_text(encoding='utf-8')
instapay_records_view = (root / 'src/ProjectZ/Views/InstapayRecordsView.xaml').read_text(encoding='utf-8')
instapay_records_code = (root / 'src/ProjectZ/Views/InstapayRecordsView.xaml.cs').read_text(encoding='utf-8')
assert '<DataGrid' not in instapay_records_view
assert 'ItemsControl ItemsSource="{Binding FilteredOperations}" FlowDirection="RightToLeft"' in instapay_records_view
assert 'Grid.Column="10"' in instapay_records_view and 'Width="210"' in instapay_records_view
for filter_name in ('FromDate', 'ToDate', 'SelectedWalletId', 'SelectedKind', 'SelectedStatus', 'CustomerFilter', 'UserFilter', 'MinimumAmountText', 'MaximumAmountText', 'MinimumCommissionText', 'MaximumCommissionText'):
    assert filter_name in instapay_records_vm and filter_name in instapay_records_view, f'missing InstaPay record filter: {filter_name}'
for control in ('ShowOperationDetailsCommand', 'BeginOperationEditCommand', 'ReverseOperationCommand', 'DeleteOperationCommand', 'CreateCsvLines', 'ApplyEffect', 'CanUndo', 'ApplyEdit'):
    assert control in instapay_records_vm, f'missing InstaPay record control: {control}'
for file_name in ('InstapayRecordDetailsWindow.xaml', 'InstapayRecordEditWindow.xaml'):
    assert (root / 'src/ProjectZ/Views' / file_name).is_file(), f'missing InstaPay record dialog: {file_name}'
assert 'FinancialAccountKind.InstaPay' in instapay_records_vm
assert 'new InstapayRecordsViewModel(InstapayTransfer, MyAccounts, user.DisplayName)' in main_viewmodel
assert 'views:InstapayRecordsView' in main_xaml
for forbidden_reference in ('Vodafone', 'فودافون'):
    assert forbidden_reference not in instapay_records_vm + instapay_records_view + instapay_records_code, f'InstaPay records must remain independent: {forbidden_reference}'
print('PASS: independent InstaPay records with compact rows, leftmost actions, filters, edit/reverse/delete, CSV and print')

transfer_implementation = '\n'.join((root / relative).read_text(encoding='utf-8') for relative in (
    'src/ProjectZ/Models/VodafoneTransferDesignModels.cs', 'src/ProjectZ/Models/InstapayTransferDesignModels.cs',
    'src/ProjectZ/ViewModels/VodafoneTransferViewModel.cs', 'src/ProjectZ/ViewModels/InstapayTransferViewModel.cs',
    'src/ProjectZ/ViewModels/VodafoneRecordsViewModel.cs', 'src/ProjectZ/ViewModels/InstapayRecordsViewModel.cs', 'src/ProjectZ/Views/VodafoneTransferView.xaml',
    'src/ProjectZ/Views/VodafoneOperationWindow.xaml', 'src/ProjectZ/Views/InstapayTransferView.xaml',
    'src/ProjectZ/Views/InstapayOperationWindow.xaml', 'src/ProjectZ/Views/VodafoneRecordsView.xaml',
    'src/ProjectZ/Views/VodafoneRecordDetailsWindow.xaml', 'src/ProjectZ/Views/VodafoneRecordEditWindow.xaml',
    'src/ProjectZ/Views/InstapayRecordsView.xaml', 'src/ProjectZ/Views/InstapayRecordDetailsWindow.xaml', 'src/ProjectZ/Views/InstapayRecordEditWindow.xaml'))
for removed_reference in ('Reference =', 'Binding Reference', 'ReferenceFilter', 'EditReference', 'ReversalReference', 'المرجع', 'VF-', 'IP-'):
    assert removed_reference not in transfer_implementation, f'transfer reference must stay removed: {removed_reference}'
print('PASS: Vodafone and InstaPay transfer references removed from models, UI, filters, CSV, and audit movements')
all_transfer_sources = '\n'.join(path.read_text(encoding='utf-8') for path in (root / 'src/ProjectZ').rglob('*') if path.suffix in ('.cs', '.xaml'))
for removed_reference in ('VodafoneCashReference', 'InstaPayReference', 'DepositVodafoneReference', 'DepositInstaPayReference', 'FinalPaymentVodafoneReference', 'FinalPaymentInstaPayReference', 'VodafoneReference', 'DiffVodafoneReference', 'DiffInstaPayReference', 'DepositVodafoneRef', 'DepositInstaPayRef', 'RefundReference'):
    assert removed_reference not in all_transfer_sources, f'wallet reference must stay removed globally: {removed_reference}'
assert 'VF-' not in all_transfer_sources and 'IP-' not in all_transfer_sources
print('PASS: wallet references removed globally from sales, purchases, maintenance, returns, details and printing')

print('PASS: POS, AddProduct, Inventory, Categories, Invoices, ReturnExchange, PurchaseEntry, PurchaseInvoices, MaintenanceIntake & MaintenanceActions UI design views, full audit, returns, print preview, no backend')
print('NOT RUN: .NET build, C# tests, WPF execution, SQL Server 2016 SP1, LAN acceptance, EXE publish')
