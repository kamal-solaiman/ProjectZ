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
assert len(re.findall(r'CREATE\s+TABLE\s+', sql, re.I)) == 37
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
    'MaintenanceOrders', 'MaintenanceOrderParts', 'MaintenancePayments', 'MaintenanceActivities',
    'MaintenanceSpareParts', 'AuditLog'
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
    # The window title carries the live store name (the program name) from the general settings.
    assert 'StaticResource StoreIdentity' in window_root.attrib.get('Title', ''), \
        'window titles must bind to the live store name'

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

# --- POS UI and SQL-backed inventory phase ---
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

# The five quick-view KPI cards were removed from the invoices screen on request; keep the table and the
# filters as the source of that information and fail the check if the boxes ever come back.
for removed_kpi in ('عدد الفواتير', 'صافي المبيعات', 'إجمالي التحصيلات', 'المتبقي الآجل (اضغط للتصفية)'):
    assert removed_kpi not in invoices_xaml, f'removed invoice KPI card is back in the UI: {removed_kpi}'
# "صافي الربح" remains only as a label inside the per-invoice detail panel, never as a summary card.
assert invoices_xaml.count('صافي الربح') == 1
assert '<UniformGrid Grid.Row="1" Columns="5"' not in invoices_xaml
for removed_binding in ('InvoicesCountDisplay', 'TotalNetSalesDisplay', 'TotalProfitDisplay',
                        'TotalCollectedAllDisplay', 'TotalRemainingDisplay', 'FilterCreditOnlyCommand'):
    assert removed_binding not in invoices_xaml, f'removed invoice KPI binding is back: {removed_binding}'
assert 'SelectDatePresetCommand' in invoices_xaml and 'FilteredInvoices' in invoices_xaml

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
    'RefreshFromDatabaseCommand',
    'DataSourceLabel',
    'HistorySourceLabel',
):
    assert token in return_exchange_xaml, f'ReturnExchange UI missing expected token: {token}'

# The four quick-view KPI cards were removed from this screen on request; the history tab and the
# workspace keep the same information per operation.
for removed_kpi in ('إجمالي عمليات المرتجع والاستبدال', 'قطع سليمة عادت للمخزون',
                    'قطع تالفة رُحلت للهالك', 'إجمالي المسترد للعملاء'):
    assert removed_kpi not in return_exchange_xaml, f'removed return/exchange KPI card is back: {removed_kpi}'
for removed_binding in ('TotalOperationsCount', 'OperationsKpiSummary', 'TotalGoodReturnedKpi',
                        'TotalDamagedReturnedKpi', 'TotalRefundedValueKpiDisplay',
                        'TotalExchangeUpgradesKpiSubtext'):
    assert removed_binding not in return_exchange_xaml, f'removed return/exchange KPI binding is back: {removed_binding}'

return_exchange_vm = (root / 'src/ProjectZ/ViewModels/ReturnExchangeViewModel.cs').read_text(encoding='utf-8')
sales_repository = (root / 'src/ProjectZ/Data/SalesRepository.cs').read_text(encoding='utf-8')
return_exchange_permissions = (root / 'sql/11_EnableReturnExchange.sql').read_text(encoding='utf-8')

# The screen must persist to SQL instead of only mutating in-memory design collections.
assert 'مرحلة التصميم (بدون Backend)' not in return_exchange_vm, 'the return screen still announces design-only mode'
assert 'private void ExecuteDatabaseOperation(' in return_exchange_vm
assert 'repository!.CreateExchange(' in return_exchange_vm and 'repository!.CreateReturn(' in return_exchange_vm
assert 'repository.LoadInvoicesInto(sharedInvoices)' in return_exchange_vm
assert 'repository.LoadReturnExchanges()' in return_exchange_vm
assert 'IsConnectedToDatabase' in return_exchange_vm

# Repository: exchange posting, history read, mixed good/damaged returns and financial settlement.
assert 'public string CreateExchange(ExchangeRequest request, ExchangePayment payment)' in sales_repository
assert 'public List<ReturnExchangeRecord> LoadReturnExchanges()' in sales_repository
for table in ('dbo.ReturnExchanges', 'dbo.ReturnExchangeLines', 'dbo.ReturnExchangeUnits'):
    assert table in sales_repository, f'the return screen does not write {table}'
assert 'VALUES(@Operation,1,@SalesLine' in sales_repository and 'VALUES(@Operation,2,NULL' in sales_repository
assert 'public int DamagedQuantity => Quantity - GoodQuantityValue;' in sales_repository
assert 'goodRemaining > 0' in sales_repository, 'mixed good/damaged returns are not split per unit'
assert 'ApplyExchangePayment(connection, transaction, request.Invoice, payment)' in sales_repository
assert 'private static ExchangeStockClaim ClaimExchangeUnits(' in sales_repository
assert 'RecordUntrackedExchange(connection, transaction, request.Invoice.Id, product.Id' in sales_repository
assert 'RefundAccounts(connection, transaction, request.Invoice, Math.Abs(difference)' in sales_repository
assert 'StockMovement(connection, transaction, request.Invoice.Id, unit.Id, "ExchangeOut"' in sales_repository
assert 'INSERT dbo.AuditLog' in sales_repository

# Cross-screen wiring: the sales repository feeds the screen and invoice writes reload stock from SQL.
assert 'Invoices.StockReloadRequested = ReloadInventoryOnly;' in main_viewmodel
assert 'NotifyInventoryChanged?.Invoke' not in return_exchange_vm
assert 'notifyInventoryChanged?.Invoke();' in return_exchange_vm
assert 'salesRepository);' in main_viewmodel
assert "if (!ReturnExchange.RefreshFromDatabase())" in main_viewmodel

# Permissions script is idempotent and grants only the return/exchange tables.
assert 'GRANT SELECT, INSERT ON OBJECT::dbo.ReturnExchanges TO [ProjectZClient];' in return_exchange_permissions
assert 'GRANT SELECT, INSERT ON OBJECT::dbo.ReturnExchangeLines TO [ProjectZClient];' in return_exchange_permissions
assert 'GRANT SELECT, INSERT ON OBJECT::dbo.ReturnExchangeUnits TO [ProjectZClient];' in return_exchange_permissions
assert 'DELETE' not in return_exchange_permissions.replace('-- ', ''), 'the return permission script must not grant DELETE'
print('PASS: return/exchange screen persists to SQL, reads its ledger back, and reloads stock across screens')

assert 'IsPurchaseEntrySelected' in main_viewmodel and 'IsPurchaseEntrySelected' in main_xaml
assert 'views:PurchaseEntryView' in main_xaml
assert 'sharedSuppliers' in main_viewmodel and 'sharedPurchaseInvoices' in main_viewmodel

# ---------------------------------------------------------------- purchasing -> SQL Server
purchase_repository_path = root / 'src/ProjectZ/Data/PurchaseRepository.cs'
purchase_permissions_path = root / 'sql/12_EnablePurchasing.sql'
assert purchase_repository_path.is_file(), 'PurchaseRepository.cs is missing'
assert purchase_permissions_path.is_file(), 'sql/12_EnablePurchasing.sql is missing'
purchase_repository = purchase_repository_path.read_text(encoding='utf-8')
purchase_permissions = purchase_permissions_path.read_text(encoding='utf-8')

# The repository must really write the purchase ledger, not only read it.
for token in (
    'INSERT dbo.PurchaseInvoices',
    'INSERT dbo.PurchaseInvoiceLines',
    'INSERT dbo.PurchaseInvoiceLineUnits',
    'INSERT dbo.PurchaseReturns',
    'INSERT dbo.PurchaseReturnLines',
    'INSERT dbo.PurchasePayments',
    'INSERT dbo.PurchaseInvoiceActivities',
    'INSERT dbo.InventoryUnits',
    "N'Supply'",
    'INSERT dbo.FinancialTransactions',
    'UPDATE dbo.FinancialAccounts',
    'UPDATE dbo.Suppliers',
    'INSERT dbo.Suppliers',
    'SELECT Id, AccountKind FROM dbo.FinancialAccounts',
    'NextDocumentNumber',
):
    assert token in purchase_repository, f'PurchaseRepository missing SQL token: {token}'

for customer_sql in ('UPDATE dbo.Customers', 'DELETE dbo.Customers',
                     'UPDATE dbo.Customers SET IsActive=0'):
    assert customer_sql in sales_repository, f'SalesRepository must persist customers: {customer_sql}'

# Every money parameter must be DECIMAL(19,2): implicit metadata is rejected by SQL Server.
assert 'SqlDbType.Decimal' in purchase_repository and 'Precision = 19' in purchase_repository
assert 'AddWithValue' not in purchase_repository, 'PurchaseRepository must declare explicit parameter types'

# The two screens must be wired to the repository, not to the design seed data.
assert 'new PurchaseRepository(connectionSettings, user.Id)' in app_cs, 'App.xaml.cs must build the purchase repository'
for token in (
    'PurchaseRepository? purchaseRepository = null',
    'purchaseRepository is null',
    'LoadSuppliersInto',
    'LoadInvoicesInto',
    'PurchaseEntry.RefreshSuppliersFromDatabase()',
    'PurchaseInvoices.RefreshFromDatabase()',
    'Suppliers.RefreshFromDatabase()',
    'SuppliesChanged()',
    'Customers.RefreshFromDatabase()',
    'CustomersChanged()',
):
    assert token in main_viewmodel, f'MainViewModel is not wired to the purchase repository: {token}'
assert 'new PurchaseRepository(connectionSettings, user.Id)' in app_cs, 'App.xaml.cs must build the purchase repository'

purchase_entry_vm = (root / 'src/ProjectZ/ViewModels/PurchaseEntryViewModel.cs').read_text(encoding='utf-8')
purchase_invoices_vm = (root / 'src/ProjectZ/ViewModels/PurchaseInvoicesViewModel.cs').read_text(encoding='utf-8')
suppliers_vm = (root / 'src/ProjectZ/ViewModels/SuppliersViewModel.cs').read_text(encoding='utf-8')

for view_model, name in ((purchase_entry_vm, 'PurchaseEntryViewModel'),
                         (purchase_invoices_vm, 'PurchaseInvoicesViewModel'),
                         (suppliers_vm, 'SuppliersViewModel')):
    assert 'using ProjectZ.Data;' in view_model, f'{name} must import the data layer'
    assert 'IsConnectedToDatabase' in view_model, f'{name} must report its data source'

assert 'repository.CreateInvoice' in purchase_entry_vm, 'the purchase entry screen must save through SQL'
assert 'مرحلة التصميم (بدون Backend)' not in purchase_entry_vm
for command in ('repository.PayInvoice', 'repository.ReturnToSupplier', 'repository.CancelInvoice',
                'repository.DeleteInvoice', 'repository.UpdateInvoice', 'repository.LoadInvoicesInto'):
    assert command in purchase_invoices_vm, f'the purchase invoices screen must use {command}'
assert 'مرحلة التصميم (بدون Backend)' not in purchase_invoices_vm
for command in ('repository.CreateSupplier', 'repository.UpdateSupplier', 'repository.DeleteSupplier',
                'repository.LoadSuppliersInto'):
    assert command in suppliers_vm, f'the supplier directory must use {command}'

customers_vm = (root / 'src/ProjectZ/ViewModels/CustomersViewModel.cs').read_text(encoding='utf-8')
assert 'using ProjectZ.Data;' in customers_vm, 'CustomersViewModel must import the data layer'
assert 'IsConnectedToDatabase' in customers_vm, 'CustomersViewModel must report its data source'
for command in ('repository.CreateCustomer', 'repository.UpdateCustomer', 'repository.DeleteCustomer',
                'repository.LoadCustomersInto', 'repository.CollectPayment'):
    assert command in customers_vm, f'the customer directory must use {command}'
assert 'مرحلة التصميم (بدون Backend)' not in customers_vm, 'the customer screen still announces design-only mode'

# The screens must no longer advertise a design-only mode, and must offer an explicit SQL refresh.
purchase_invoices_xaml_text = (root / 'src/ProjectZ/Views/PurchaseInvoicesView.xaml').read_text(encoding='utf-8')
purchase_entry_xaml_text = (root / 'src/ProjectZ/Views/PurchaseEntryView.xaml').read_text(encoding='utf-8')
for view in (purchase_invoices_xaml_text, purchase_entry_xaml_text):
    assert 'تصميم الواجهة — بدون Backend' not in view
    assert 'DataSourceLabel' in view, 'the screen must show its live data source'
assert 'RefreshFromDatabaseCommand' in purchase_invoices_xaml_text

# Permissions script: idempotent grants only, and no table is created in it.
assert 'CREATE TABLE' not in purchase_permissions, 'sql/12 must not create tables'
for grant in (
    'GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::dbo.PurchaseInvoices TO [ProjectZClient];',
    'GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::dbo.PurchaseInvoiceLines TO [ProjectZClient];',
    'GRANT SELECT, INSERT, DELETE ON OBJECT::dbo.PurchaseInvoiceLineUnits TO [ProjectZClient];',
    'GRANT SELECT, INSERT ON OBJECT::dbo.PurchasePayments TO [ProjectZClient];',
    'GRANT SELECT, INSERT ON OBJECT::dbo.PurchaseReturns TO [ProjectZClient];',
    'GRANT SELECT, INSERT ON OBJECT::dbo.PurchaseReturnLines TO [ProjectZClient];',
    'GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::dbo.Suppliers TO [ProjectZClient];',
):
    assert grant in purchase_permissions, f'sql/12 is missing a grant: {grant}'
print('PASS: purchase invoice screens and the supplier directory read and write SQL Server')

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
):
    assert token in purchase_invoices_xaml, f'PurchaseInvoices UI missing expected token: {token}'

for removed_kpi in ('عدد فواتير الشراء', 'إجمالي المشتريات الصافي', 'إجمالي المدفوع للموردين',
                    'مرتجعات المشتريات', 'المتبقي الآجل للموردين (اضغط للتصفية)'):
    assert removed_kpi not in purchase_invoices_xaml, f'removed purchase KPI card is back: {removed_kpi}'
for removed_binding in ('InvoicesCountDisplay', 'TotalNetPurchasesDisplay', 'TotalPaidNetDisplay',
                        'TotalReturnedGoodsDisplay', 'TotalRemainingPayableDisplay', 'FilterCreditOnlyCommand'):
    assert removed_binding not in purchase_invoices_xaml, f'removed purchase KPI binding is back: {removed_binding}'

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

# The intake screen must stay SQL-backed: the sequence badge, the sync stamp, the header badge and
# the customer creation path all read/write SQL Server, and the design-mode wording must not return.
maintenance_intake_vm = (root / 'src/ProjectZ/ViewModels/MaintenanceIntakeViewModel.cs').read_text(encoding='utf-8')
maintenance_repository_text = (root / 'src/ProjectZ/Data/MaintenanceRepository.cs').read_text(encoding='utf-8')
passcode_protection_path = root / 'src/ProjectZ/Services/PasscodeProtection.cs'
assert passcode_protection_path.is_file(), 'PasscodeProtection service is missing'
for token in ('IsDatabaseBacked', 'DataConnectionBadgeText', 'LastSyncedDisplay', 'RefreshFromDatabaseCommand',
              'PasscodeProtectionHint', 'ValidateFieldLengths', 'CreateCustomerFromDialog',
              'ValidateNewCustomerPhone', 'HasOpenOrderWithSerial'):
    assert token in maintenance_intake_vm, f'Maintenance intake view model missing expected token: {token}'
for token in ('IsDatabaseBacked', 'DataConnectionBadgeText', 'LastSyncedDisplay', 'RefreshFromDatabaseCommand',
              'PasscodeProtectionHint'):
    assert token in maintenance_intake_xaml, f'Maintenance intake UI is not SQL-backed: {token}'
for stale in ('تصميم الواجهة — بدون Backend', 'بدون Backend', 'MNT-2026-'):
    assert stale not in maintenance_intake_xaml, f'design-mode leftover is back in the intake UI: {stale}'
assert 'MNT-2026-' not in maintenance_intake_vm, 'the intake view model still guesses a fixed-year order number'
for token in ('PeekNextOrderNumber', 'HasOpenOrderWithSerial', 'AppendMaintenanceActivity', 'PasscodeProtector'):
    assert token in maintenance_repository_text, f'Maintenance repository missing expected token: {token}'
assert 'enc:v1:' in passcode_protection_path.read_text(encoding='utf-8')
assert (root / 'sql/14_EnableMaintenanceIntake.sql').is_file(), 'sql/14_EnableMaintenanceIntake.sql is missing'
print('PASS: maintenance intake writes orders, deposits and activities to SQL with a real sequence number')

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

# The actions screen must stay SQL-backed: real technician directory from SQL, a RowVersion guard so
# two clients cannot overwrite each other, an id-based reselection after a reload, and a readable
# failure path. These tokens guard against a silent regression to the design-mode behaviour.
maintenance_actions_vm_path = root / 'src/ProjectZ/ViewModels/MaintenanceActionsViewModel.cs'
maintenance_actions_dialogs_path = root / 'src/ProjectZ/ViewModels/MaintenanceActionsDialogViewModels.cs'
maintenance_order_model = (root / 'src/ProjectZ/Models/MaintenanceDesignModels.cs').read_text(encoding='utf-8')
maintenance_repository = (root / 'src/ProjectZ/Data/MaintenanceRepository.cs').read_text(encoding='utf-8')
maintenance_actions_vm = maintenance_actions_vm_path.read_text(encoding='utf-8')
maintenance_actions_dialogs = maintenance_actions_dialogs_path.read_text(encoding='utf-8')
for token in ('IsDatabaseBacked', 'DataConnectionBadgeText', 'LastSyncedDisplay', 'ReloadTechnicianDirectory',
              'isBulkReload', 'ValidateWorkshopFieldLengths', 'AppendMaintenanceActivity', 'RefreshFilteredOrders(selectedId)'):
    assert token in maintenance_actions_vm, f'MaintenanceActions view model missing expected token: {token}'
for token in ('DataConnectionBadgeText', 'LastSyncedDisplay'):
    assert token in maintenance_actions_xaml, f'MaintenanceActions UI is not SQL-backed: {token}'
assert 'TechnicianFilterOptions = new[] { "كل الفنيين" }' not in maintenance_actions_vm, \
    'the technician directory is hard-coded from the design samples again'
assert 'DatabaseRowVersion' in maintenance_order_model, 'MaintenanceOrderDesignItem.DatabaseRowVersion is missing'
for token in ('RowVersion = @RowVersion', 'OUTPUT inserted.RowVersion', 'LoadTechnicianDirectory'):
    assert token in maintenance_repository, f'MaintenanceRepository missing expected token: {token}'
assert 'ملاحظات التسليم أطول من الحد المسموح' in maintenance_actions_dialogs
assert 'سبب الإلغاء أطول من الحد المسموح' in maintenance_actions_dialogs
assert (root / 'sql/15_EnableMaintenanceActions.sql').is_file(), 'sql/15_EnableMaintenanceActions.sql is missing'
print('PASS: maintenance actions screen writes status, pricing, parts, payments, delivery and cancellation to SQL '
      'with optimistic concurrency and a SQL technician directory')

maintenance_invoices_xaml_path = root / 'src/ProjectZ/Views/MaintenanceInvoicesView.xaml'
maintenance_invoice_details_xaml_path = root / 'src/ProjectZ/Views/MaintenanceInvoiceDetailsWindow.xaml'
assert maintenance_invoices_xaml_path.is_file() and maintenance_invoice_details_xaml_path.is_file()
assert 'IsMaintenanceInvoicesSelected' in main_viewmodel and 'MaintenanceInvoices' in main_viewmodel
assert 'views:MaintenanceInvoicesView' in main_xaml

maintenance_invoices_xaml = maintenance_invoices_xaml_path.read_text(encoding='utf-8')
for token in (
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

for removed_kpi in ('عدد فواتير الصيانة', 'إجمالي قيمة الصيانات', 'صافي ربح الصيانة',
                    'صافي التحصيلات الفعلية', 'المتبقي للتحصيل (اضغط للتصفية)'):
    assert removed_kpi not in maintenance_invoices_xaml, f'removed maintenance KPI card is back: {removed_kpi}'
for removed_binding in ('InvoicesCountDisplay', 'TotalServiceValueDisplay', 'TotalNetProfitDisplay',
                        'TotalRemainingDisplay', 'FilterRemainingOnlyCommand'):
    assert removed_binding not in maintenance_invoices_xaml, f'removed maintenance KPI binding is back: {removed_binding}'

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

for removed_kpi in ('إجمالي الأصناف المسجلة', 'إجمالي القطع المتاحة', 'إجمالي قيمة المخزون (بالتكلفة)',
                    'رصيد منخفض أو نافد'):
    assert removed_kpi not in maintenance_spare_parts_xaml, f'removed spare-parts KPI card is back: {removed_kpi}'
for removed_binding in ('TotalPartsCountDisplay', 'TotalQuantityUnitsDisplay', 'TotalCostValueDisplay',
                        'LowOrOutCountDisplay'):
    assert removed_binding not in maintenance_spare_parts_xaml, f'removed spare-parts KPI binding is back: {removed_binding}'

maintenance_actions_vm_text = (root / 'src/ProjectZ/ViewModels/MaintenanceActionsViewModel.cs').read_text(encoding='utf-8')
assert 'ObservableCollection<MaintenanceSparePartItem>' in maintenance_actions_vm_text
assert 'InventoryUnitRegistrationItem' not in maintenance_actions_vm_text, 'MaintenanceActions must use dedicated maintenance spare parts, not POS/Inventory registrations'

customers_xaml_path = root / 'src/ProjectZ/Views/CustomersView.xaml'
assert customers_xaml_path.is_file()
assert 'IsCustomersSelected' in main_viewmodel and 'Customers' in main_viewmodel
assert 'views:CustomersView' in main_xaml

customers_xaml = customers_xaml_path.read_text(encoding='utf-8')
assert 'DataSourceLabel' in customers_xaml, 'the customer screen must show its live data source'
assert 'تصميم الواجهة — بدون Backend' not in customers_xaml, 'the customer screen still advertises a design-only badge'
assert 'MaintenanceEmptyMessage' in customers_xaml, 'the customer screen must explain the empty maintenance tab'
for token in (
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

for removed_kpi in ('العملاء المسجلون', 'إجمالي التعاملات (مبيعات + صيانة)',
                    'إجمالي المحصّل من العملاء', 'المستحقات المتبقية للتحصيل'):
    assert removed_kpi not in customers_xaml, f'removed customers KPI card is back: {removed_kpi}'
# TotalBusinessValueDisplay still exists on the per-customer directory row, so only the card-only
# bindings are forbidden here.
for removed_binding in ('TotalCustomersCountDisplay', 'TotalCollectedAllDisplay', 'TotalRemainingAllDisplay',
                        'TierBreakdownSubtext', 'DebtorsCountSubtext'):
    assert removed_binding not in customers_xaml, f'removed customers KPI binding is back: {removed_binding}'

suppliers_xaml_path = root / 'src/ProjectZ/Views/SuppliersView.xaml'
suppliers_vm_path = root / 'src/ProjectZ/ViewModels/SuppliersViewModel.cs'
assert suppliers_xaml_path.is_file() and suppliers_vm_path.is_file()
assert 'IsSuppliersSelected' in main_viewmodel and 'SuppliersViewModel' in main_viewmodel
assert 'views:SuppliersView' in main_xaml
suppliers_xaml = suppliers_xaml_path.read_text(encoding='utf-8')
for token in (
    'FilteredSuppliers', 'SelectedSupplierEntry',
    'AddSupplierCommand', 'EditSelectedSupplierCommand', 'DeleteSelectedSupplierCommand',
    'StartPurchaseForSupplierCommand', 'PayOldestInvoiceCommand',
    'SelectedSupplierInvoices', 'SelectedSupplierStatement', 'PayInvoiceCommand',
    'ReturnInvoiceCommand', 'OpenInvoiceDetailsCommand', 'PrintInvoiceCommand',
):
    assert token in suppliers_xaml, f'Suppliers UI missing expected token: {token}'

# "صافي المشتريات" also heads the supplier-invoices table column, so the card labels checked here are
# the ones that appeared only inside the removed summary cards.
for removed_kpi in ('الموردون المسجلون', 'إجمالي المسدد للموردين'):
    assert removed_kpi not in suppliers_xaml, f'removed suppliers KPI card is back: {removed_kpi}'
assert suppliers_xaml.count('صافي المشتريات') == 1, 'the suppliers summary card is back'
# TotalPurchasesDisplay is still bound per supplier row, so only the card-only bindings are forbidden.
for removed_binding in ('TotalSuppliersCountDisplay', 'TotalPaidAllDisplay', 'TotalOutstandingAllDisplay',
                        'SpecializationsBreakdownSubtext', 'CreditorsCountSubtext'):
    assert removed_binding not in suppliers_xaml, f'removed suppliers KPI binding is back: {removed_binding}'
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
    root / 'src/ProjectZ/ViewModels/AddSupplierViewModel.cs',
    root / 'src/ProjectZ/ViewModels/SuppliersViewModel.cs',
    root / 'src/ProjectZ/ViewModels/MyAccountsViewModel.cs',
    root / 'src/ProjectZ/ViewModels/AccountDialogViewModels.cs',
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
    root / 'src/ProjectZ/ViewModels/VodafoneTransferViewModel.cs',
    root / 'src/ProjectZ/ViewModels/InstapayTransferViewModel.cs',
    root / 'src/ProjectZ/ViewModels/VodafoneRecordsViewModel.cs',
    root / 'src/ProjectZ/ViewModels/InstapayRecordsViewModel.cs',
]
for p in pos_code_paths:
    text = p.read_text(encoding='utf-8')
    assert 'SqlClient' not in text and 'SqlConnection' not in text, f'UI code must keep SQL in repositories: {p}'

inventory_repository = (root / 'src/ProjectZ/Data/InventoryRepository.cs').read_text(encoding='utf-8')
inventory_vm = (root / 'src/ProjectZ/ViewModels/InventoryViewModel.cs').read_text(encoding='utf-8')
for operation in ('LoadInto', 'SaveNewProduct', 'UpdateProduct', 'Supply', 'MarkDamaged', 'MarkSingleDamaged',
                  'RegisterSerial', 'RestoreDamaged', 'MarkReturnedToSupplier', 'DeleteDamaged', 'DeleteOrArchiveProduct'):
    assert f'public void {operation}' in inventory_repository or f'public bool {operation}' in inventory_repository, f'missing inventory SQL operation: {operation}'
for transaction_use in ('BeginTransaction(IsolationLevel.Serializable)', 'transaction.Commit()', 'transaction.Rollback()', 'RowVersion=@RowVersion'):
    assert transaction_use in inventory_repository, f'missing inventory persistence guard: {transaction_use}'
for schema_column in ('PurchaseInvoiceNumber', 'SaleInvoiceNumber'):
    assert schema_column in inventory_repository and schema_column in full_schema
for callback in ('repository.Supply(', 'repository.UpdateProduct(', 'repository.MarkDamaged(', 'repository.MarkSingleDamaged(',
                 'repository.RegisterSerial(', 'repository.RestoreDamaged(', 'repository.MarkReturnedToSupplier(',
                 'repository.DeleteDamaged(', 'repository.DeleteOrArchiveProduct(', 'repository.SaveNewProduct('):
    assert callback in inventory_vm, f'inventory screen is not wired to SQL operation: {callback}'
assert 'new InventoryRepository(connectionSettings, user.Id)' in (root / 'src/ProjectZ/App.xaml.cs').read_text(encoding='utf-8')
assert 'public string AddCategory(string name)' in inventory_repository
for operation in ('repository.SaveNewProduct(', 'repository.DeleteOrArchiveProduct(', 'createCategory(trimmed)', 'reloadInventory()'):
    assert operation in add_product_vm_text, f'AddProduct is not wired to SQL operation: {operation}'
assert 'AddProduct.RefreshFromDatabase();' in main_viewmodel
assert 'Command="{Binding RefreshCatalogCommand}"' in add_product_xaml
assert 'CanDeleteFromCatalog' in add_product_xaml and 'OpenConfirmDialogRequested' in add_product_vm_text
print('PASS: AddProduct persists products/categories, refreshes SQL data, and confirms delete/archive')
pos_vm = (root / 'src/ProjectZ/ViewModels/PosViewModel.cs').read_text(encoding='utf-8')
assert 'useDesignSamples: inventoryRepository is null' in main_viewmodel
assert 'if (useDesignSamples)' in pos_vm and 'else\n            CreateAndSelectNewInvoice();' in pos_vm
assert 'if (Catalog.Count < 4 || Customers.Count < 4)' in pos_vm
print('PASS: SQL startup uses an empty POS invoice; design samples cannot index a short catalog')
inventory_view = (root / 'src/ProjectZ/Views/InventoryView.xaml').read_text(encoding='utf-8')
inventory_vm = (root / 'src/ProjectZ/ViewModels/InventoryViewModel.cs').read_text(encoding='utf-8')
inventory_repo = (root / 'src/ProjectZ/Data/InventoryRepository.cs').read_text(encoding='utf-8')
inventory_dialog_vm = (root / 'src/ProjectZ/ViewModels/InventoryDialogViewModels.cs').read_text(encoding='utf-8')
registrations_window = (root / 'src/ProjectZ/Views/ProductRegistrationsWindow.xaml').read_text(encoding='utf-8')
supply_window = (root / 'src/ProjectZ/Views/SupplyStockWindow.xaml').read_text(encoding='utf-8')

# The screen must not present itself as a design mock anymore.
assert 'بدون Backend' not in inventory_view, 'the inventory screen still shows the "no backend" badge'
assert 'متصل بقاعدة البيانات' in inventory_view, 'the inventory screen is missing the SQL connection badge'

# Every button on the screen must bind a real command; no Click handlers in the code-behind.
assert 'Click=' not in inventory_view, 'the inventory screen still uses Click handlers instead of commands'
inventory_buttons = [b for b in re.findall(r'<Button\b[^>]*>', inventory_view, re.S) if not b.startswith('<Button.')]
assert len(inventory_buttons) >= 10, 'the inventory screen unexpectedly has almost no buttons'
for button in inventory_buttons:
    assert 'Command=' in button or 'IsCancel=' in button, f'inventory button without a command: {button[:120]}'
for token in ('اسم المنتج', 'باركود', 'الكمية', 'سعر القطاعي', 'سعر الجملة', 'سعر المميز', 'الهالك', 'أزرار التحكمات'):
    assert token in inventory_view, f'missing inventory column header: {token}'
for command in (
    'OpenRegistrationsCommand', 'OpenSupplyCommand', 'OpenEditCommand', 'OpenBarcodePrintCommand',
    'OpenMarkDamagedCommand', 'DeleteProductCommand', 'RestoreDamagedUnitCommand', 'DeleteDamagedUnitCommand',
    'MarkReturnedToSupplierCommand', 'GoToAddProductCommand', 'RefreshInventoryCommand',
    'ShowProductMovementsCommand', 'EditDamagedUnitCommand', 'MarkAllDamagedReturnedCommand',
    'SelectDamagedTabCommand',
    'ClearCatalogFiltersCommand', 'ClearDamagedFiltersCommand', 'IsDamagedTabSelected',
):
    assert command in inventory_view, f'inventory screen is not wired to {command}'

# Card-only labels: "مخزون الهالك" also names the damaged tab, so the KPI binder count is checked instead.
for removed_kpi in ('إجمالي الأصناف', 'إجمالي الكميات المتاحة', 'نواقص المخزون'):
    assert removed_kpi not in inventory_view, f'removed inventory KPI card is back: {removed_kpi}'
for removed_binding in ('TotalProductsCount', 'TotalAvailableStock', 'LowStockCount', 'TotalDamagedCount'):
    assert removed_binding not in inventory_view, f'removed inventory KPI binding is back: {removed_binding}'

# Refresh + ledger panel read straight from SQL through the repository.
assert 'RefreshInventoryCommand = new ActionCommand(() => RefreshFromDatabase());' in inventory_vm
assert 'repository.LoadStockMovements' in inventory_vm
assert 'public ObservableCollection<StockMovementRow> RecentMovements' in inventory_vm
assert 'FROM dbo.StockMovements m' in inventory_repo
assert 'dbo.Users u ON u.Id = m.ActorUserId' in inventory_repo

# Suppliers and categories come from the database, never from design seed data.
assert 'InventoryDesignSeedData.DefaultSuppliers' not in inventory_vm, 'suppliers still come from seed data'
assert 'repository.LoadSupplierNames()' in inventory_vm
assert 'SELECT Name FROM dbo.Suppliers WHERE IsActive=1' in inventory_repo
assert 'categoryRepository.LoadNamesInto(target)' in inventory_vm, 'inventory categories are not read from dbo.Categories'
assert 'ReloadCategoryNames(categories)' in inventory_vm
assert 'PosDesignSeedData.ProductCategories' not in inventory_vm, 'inventory categories still fall back to seed data'

# Writes are audited and notify the rest of the application.
for write in ('Supply', 'MarkDamaged', 'MarkSingleDamaged', 'RestoreDamaged', 'MarkReturnedToSupplier',
              'DeleteDamaged', 'DeleteOrArchiveProduct', 'RegisterSerial', 'UpdateProduct', 'SaveNewProduct'):
    assert f'public ' in inventory_repo and write in inventory_repo, f'missing repository operation {write}'
assert 'Inventory.InventoryChanged += OnInventoryChanged' in main_viewmodel
assert 'Pos.NotifyExternalInventoryChanged();' in main_viewmodel
assert 'InventoryChanged?.Invoke();' in inventory_vm
assert inventory_vm.count('ReloadAfterWrite();') >= 10, 'not every inventory write reloads and notifies'
for audited in ('MarkUnitDamaged', 'ReturnDamagedToSupplier', 'UpdateDamageSettlement',
                'UpdateDamageReason', 'RestoreDamaged', 'DeleteDamagedUnit'):
    assert f'"{audited}"' in inventory_repo, f'missing audit entry for {audited}'

# Registrations window exposes the per-unit ledger; the supply dialog suggests real suppliers.
assert 'ShowUnitMovementsCommand' in registrations_window
assert 'UnitMovements' in inventory_dialog_vm
assert 'dbo.Suppliers' in supply_window
assert 'OpenDamageEditDialogRequested' in (root / 'src/ProjectZ/Views/InventoryView.xaml.cs').read_text(encoding='utf-8')
damage_edit_vm = (root / 'src/ProjectZ/ViewModels/InventoryDialogViewModels.cs').read_text(encoding='utf-8')
assert 'class DamageEditViewModel' in damage_edit_vm
assert 'repository.UpdateDamageSettlement(unit, settlement)' in inventory_vm
assert 'repository.UpdateDamageReason(unit, reason)' in inventory_vm
assert 'UPDATE dbo.InventoryUnits SET DamageSettlementStatus=@Status' in inventory_repo
assert 'UPDATE dbo.InventoryUnits SET DamageReason=@Reason' in inventory_repo
print('PASS: inventory screen is fully wired to SQL (catalog, units, suppliers, categories, movements, cross-screen notifications)')

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


# --- POS sales persistence (SalesRepository + sql/09_EnablePosSales.sql) -------------------
sales_repository_path = root / 'src/ProjectZ/Data/SalesRepository.cs'
assert sales_repository_path.is_file(), 'missing src/ProjectZ/Data/SalesRepository.cs'
sales_repository = sales_repository_path.read_text(encoding='utf-8')
enable_pos_sql_path = root / 'sql/09_EnablePosSales.sql'
assert enable_pos_sql_path.is_file(), 'missing sql/09_EnablePosSales.sql'
enable_pos_sql = enable_pos_sql_path.read_text(encoding='utf-8')
for operation in ('LoadCustomersInto', 'CreateCustomer', 'CompleteSale', 'CollectPayment', 'LoadInvoicesInto',
                  'UpdateNotes', 'CancelSale', 'CreateReturn'):
    assert f'public' in sales_repository and operation + '(' in sales_repository, f'missing POS SQL operation: {operation}'
for guard in ('BeginTransaction(IsolationLevel.Serializable)', 'UPDLOCK, HOLDLOCK', 'dbo.DocumentSequences',
              'dbo.SalesPayments', 'dbo.FinancialTransactions', 'dbo.StockMovements', 'dbo.AuditLog',
              'INSERT dbo.SalesInvoiceLineUnits', 'CountsTowardLimits'):
    assert guard in sales_repository, f'missing POS persistence guard: {guard}'
for message_code in ('229 or 230', '1205', '2601 or 2627'):
    assert message_code in sales_repository, f'missing SQL error mapping: {message_code}'
for table in ('Customers', 'DocumentSequences', 'FinancialAccounts', 'FinancialTransactions', 'SalesInvoices',
              'SalesInvoiceLines', 'SalesInvoiceLineUnits', 'SalesPayments', 'SalesInvoiceActivities',
              'ReturnExchanges', 'ReturnExchangeLines', 'ReturnExchangeUnits', 'InventoryUnits', 'StockMovements',
              'AuditLog'):
    assert f'dbo.{table} TO [ProjectZClient]' in enable_pos_sql, f'sql/09 does not grant ProjectZClient on {table}'
assert 'GRANT DELETE' not in enable_pos_sql and 'CREATE TABLE' not in enable_pos_sql
for forbidden in ('SqlConnection', 'Microsoft.Data.SqlClient', 'SqlCommand'):
    assert forbidden not in pos_vm, f'POS view models must not talk to SQL directly: {forbidden}'
    assert forbidden not in (root / 'src/ProjectZ/ViewModels/InvoicesViewModel.cs').read_text(encoding='utf-8')
assert 'new SalesRepository(connectionSettings, user.Id)' in (root / 'src/ProjectZ/App.xaml.cs').read_text(encoding='utf-8')
assert 'salesRepository,' in main_viewmodel and 'CreateCustomerFromPos' in main_viewmodel
invoices_vm_text = (root / 'src/ProjectZ/ViewModels/InvoicesViewModel.cs').read_text(encoding='utf-8')
for wiring in ('CompleteSale(', 'CollectPayment(', 'UpdateNotes(', 'CancelSale(',
               'CreateReturn(', 'LoadInvoicesInto(', 'LoadCustomersInto'):
    assert 'repository' in invoices_vm_text + main_viewmodel, 'invoice screen must receive the sales repository'
    assert wiring in invoices_vm_text + main_viewmodel, f'invoice screen is not wired to SQL: {wiring}'
assert 'RefreshInvoicesFromDatabase()' in main_viewmodel
assert 'تصميم الواجهة — بدون Backend' not in pos_xaml, 'POS must not advertise a backend-less design screen'
assert 'تصميم الواجهة — بدون Backend' not in (root / 'src/ProjectZ/Views/InvoicesView.xaml').read_text(encoding='utf-8')
assert 'Command="{Binding RefreshCatalogCommand}"' in pos_xaml
assert 'IsDatabaseBacked' in pos_vm

# --- Categories screen + add-product category combo (CategoryRepository + sql/10) -----------
category_repository_path = root / 'src/ProjectZ/Data/CategoryRepository.cs'
assert category_repository_path.is_file(), 'missing src/ProjectZ/Data/CategoryRepository.cs'
category_repository = category_repository_path.read_text(encoding='utf-8')
enable_category_sql_path = root / 'sql/10_EnableCategoryAdmin.sql'
assert enable_category_sql_path.is_file(), 'missing sql/10_EnableCategoryAdmin.sql'
enable_category_sql = enable_category_sql_path.read_text(encoding='utf-8')
for operation in ('LoadAll', 'LoadNamesInto', 'Create', 'Rename', 'Delete'):
    assert f'{operation}(' in category_repository, f'missing category SQL operation: {operation}'
for guard in ('BeginTransaction(IsolationLevel.Serializable)', 'UPDLOCK, HOLDLOCK', 'dbo.Categories', 'dbo.Products',
              'INSERT dbo.AuditLog', 'UPDATE dbo.Categories', 'DELETE dbo.Categories'):
    assert guard in category_repository, f'missing category persistence guard: {guard}'
assert 'لا يمكن حذف التصنيف' in category_repository, 'deleting a used category must be refused with a clear message'
assert 'GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::dbo.Categories TO [ProjectZClient]' in enable_category_sql
assert 'CREATE TABLE' not in enable_category_sql
categories_vm = (root / 'src/ProjectZ/ViewModels/CategoriesViewModel.cs').read_text(encoding='utf-8')
for wiring in ('repository.LoadAll(', 'repository.LoadNamesInto(', 'repository.Create(', 'repository.Rename(',
               'repository.Delete(', 'EnsureCategory(', 'RefreshFromDatabase()'):
    assert wiring in categories_vm, f'categories screen is not wired to SQL: {wiring}'
for token in ('SearchText', 'ToggleOnlyUsedCommand', 'NewCategoryDescription', 'EditingDescription', 'ProductsCountDisplay',
              'CanDelete', 'RefreshCategoriesCommand', 'ToggleOnlyUsedCommand', 'EmptyCategoriesDisplay'):
    assert token in categories_xaml, f'Categories UI missing expected token: {token}'
assert 'متصل بقاعدة البيانات' in categories_xaml and 'بدون Backend' not in categories_xaml
assert 'new CategoryRepository(connectionSettings, user.Id)' in (root / 'src/ProjectZ/App.xaml.cs').read_text(encoding='utf-8')
assert 'categoryRepository,' in main_viewmodel and 'ProductCategories.CategoriesChanged' in main_viewmodel
add_product_vm = (root / 'src/ProjectZ/Views/AddProductView.xaml').read_text(encoding='utf-8')
for token in ('ItemsSource="{Binding Categories}"', 'SelectedCategory', 'CategoriesCountDisplay', 'HasNoCategories'):
    assert token in add_product_vm, f'add-product category combo is not wired: {token}'
assert 'EnsureCategory' in main_viewmodel, 'the add-product dialog must save categories through the shared screen'
print('PASS: categories screen and the add-product category combo both read and write dbo.Categories')
# --- Users screen (login accounts in dbo.Users + sql/14) -------------------------------------
users_repository_path = root / 'src/ProjectZ/Data/UserRepository.cs'
assert users_repository_path.is_file(), 'missing src/ProjectZ/Data/UserRepository.cs'
users_repository = users_repository_path.read_text(encoding='utf-8')
enable_users_sql_path = root / 'sql/14_EnableUserAdmin.sql'
assert enable_users_sql_path.is_file(), 'missing sql/14_EnableUserAdmin.sql'
enable_users_sql = enable_users_sql_path.read_text(encoding='utf-8')
for operation in ('FindAsync', 'LoadAll', 'Create', 'Update', 'ResetPassword', 'Delete'):
    assert f'{operation}(' in users_repository, f'missing user SQL operation: {operation}'
for guard in ('BeginTransaction(IsolationLevel.Serializable)', 'UPDLOCK, HOLDLOCK', 'dbo.Users',
              'INSERT dbo.Users', 'UPDATE dbo.Users', 'DELETE dbo.Users', 'INSERT dbo.AuditLog',
              'PasswordHasher.Hash'):
    assert guard in users_repository, f'missing user persistence guard: {guard}'
assert 'لا يمكن حذف المستخدم' in users_repository, 'deleting a linked user must be refused with a clear message'
assert 'لا يمكنك حذف الحساب الذي تستخدمه الآن' in users_repository, 'the signed-in account must not be deletable'
assert 'GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::dbo.Users TO [ProjectZClient]' in enable_users_sql
assert 'CREATE TABLE' not in enable_users_sql, 'sql/14 must not create tables'
assert "CREATE TABLE dbo.Users" in (root / 'sql/01_CreateDatabase.sql').read_text(encoding='utf-8'), \
    'the users screen must keep using the original dbo.Users table'

users_vm = (root / 'src/ProjectZ/ViewModels/UsersViewModel.cs').read_text(encoding='utf-8')
for wiring in ('repository.LoadAll(', 'repository.Update(', 'repository.Delete(', 'RefreshFromDatabase()',
               'OpenUserEditorRequested', 'OpenConfirmDialogRequested'):
    assert wiring in users_vm, f'users screen is not wired to SQL: {wiring}'
users_editor_vm = (root / 'src/ProjectZ/ViewModels/UserEditorViewModel.cs').read_text(encoding='utf-8')
for wiring in ('repository.Create(', 'repository.ResetPassword(', 'repository.Update(', 'readPassword'):
    assert wiring in users_editor_vm, f'user dialog is not wired to SQL: {wiring}'
for forbidden in ('private string password', 'public string Password', 'string Password '):
    assert forbidden not in users_editor_vm, 'the dialog must not store the plain password in a field'
assert 'clearPasswords' in users_editor_vm and 'readPassword()' in users_editor_vm

# The dialog receives the clearing delegate as a field (clearPasswords), never as a method of its own.
assert 'ClearPasswords(' not in users_editor_vm, \
    'the dialog view model must invoke its clearPasswords delegate, not an undefined ClearPasswords()'
assert users_editor_vm.count('clearPasswords();') == 2, \
    'both the create and the password-reset paths must clear the password boxes after saving'

users_xaml = (root / 'src/ProjectZ/Views/UsersView.xaml').read_text(encoding='utf-8')
for token in ('FilteredUsers', 'SearchText', 'StatusFilters', 'AddUserCommand', 'RefreshUsersCommand',
              'ToggleActiveCommand', 'DeleteCommand', 'ChangePasswordCommand', 'LinkedRecordsDisplay',
              'HasNoUsers', 'ConnectionBadgeText'):
    assert token in users_xaml, f'Users UI missing expected token: {token}'
assert 'متصل بقاعدة البيانات' in users_vm, 'the users screen must state it is connected to SQL'
assert 'بدون Backend' not in users_xaml, 'the users screen must not advertise a backend-less design' 
users_view_cs = (root / 'src/ProjectZ/Views/UsersView.xaml.cs').read_text(encoding='utf-8')
assert 'OpenUserEditorRequested' in users_view_cs and 'ConfirmActionWindow' in users_view_cs
editor_xaml_root = ET.parse(root / 'src/ProjectZ/Views/UserEditorWindow.xaml').getroot()
assert editor_xaml_root.attrib.get('FlowDirection') == 'LeftToRight'
assert editor_xaml_root.attrib.get('Language') == 'ar-EG'
assert editor_xaml_root.attrib.get('WindowStartupLocation') == 'CenterOwner'
editor_password_boxes = [el for el in editor_xaml_root.iter() if el.tag.endswith('PasswordBox')]
assert len(editor_password_boxes) == 2, 'the user dialog needs password + confirmation boxes'
assert all(box.attrib.get('FlowDirection') == 'LeftToRight' for box in editor_password_boxes), \
    'passwords are identifiers: keep their input LTR'
editor_cs = (root / 'src/ProjectZ/Views/UserEditorWindow.xaml.cs').read_text(encoding='utf-8')
assert 'PasswordInput.Password' in editor_cs and 'ClearPasswords' in editor_cs
assert 'DataContext = null;' in editor_cs, 'the dialog must drop its data context on close'
assert 'new UserRepository(connectionSettings, user.Id)' in app_cs
assert 'userRepository,' in main_viewmodel and 'Users.RefreshFromDatabase();' in main_viewmodel
assert 'IsUsersSelected' in main_xaml and 'UsersView DataContext="{Binding Users}"' in main_xaml
print('PASS: users screen reads and writes login accounts in dbo.Users with PBKDF2 hashing and guarded deletes')
# --- General settings screen: store identity is the program name (SystemSettings + sql/16) ---------
settings_repository_path = root / 'src/ProjectZ/Data/SettingsRepository.cs'
store_profile_path = root / 'src/ProjectZ/Models/StoreProfile.cs'
store_identity_path = root / 'src/ProjectZ/Services/StoreIdentity.cs'
settings_view_path = root / 'src/ProjectZ/Views/GeneralSettingsView.xaml'
settings_vm_path = root / 'src/ProjectZ/ViewModels/GeneralSettingsViewModel.cs'
enable_settings_sql_path = root / 'sql/16_EnableGeneralSettings.sql'
for required in (settings_repository_path, store_profile_path, store_identity_path,
                 settings_view_path, settings_vm_path, enable_settings_sql_path):
    assert required.is_file(), f'missing general-settings file: {required.relative_to(root)}'

settings_repository = settings_repository_path.read_text(encoding='utf-8')
for operation in ('public StoreProfile Load(', 'public StoreProfile Save('):
    assert operation in settings_repository, f'missing settings SQL operation: {operation}'
for guard in ('BeginTransaction(IsolationLevel.Serializable)', 'UPDLOCK, HOLDLOCK', 'dbo.SystemSettings',
              'UPDATE dbo.SystemSettings', 'INSERT dbo.SystemSettings', 'INSERT dbo.AuditLog',
              'UpdateGeneralSettings', 'UPDATE dbo.SystemSettings\nSET SettingValue'):
    assert guard in settings_repository, f'missing settings persistence guard: {guard}'
for key in ('StoreName', 'StoreAddress', 'StorePhone', 'CurrencyNameAr', 'DefaultLowStockAlert'):
    assert f'"{key}"' in settings_repository, f'missing settings key: {key}'

enable_settings_sql = enable_settings_sql_path.read_text(encoding='utf-8')
assert 'GRANT SELECT, INSERT, UPDATE ON OBJECT::dbo.SystemSettings TO [ProjectZClient]' in enable_settings_sql
assert 'CREATE TABLE' not in enable_settings_sql, 'sql/16 must not create tables'
assert "IF NOT EXISTS (SELECT 1 FROM dbo.SystemSettings WHERE SettingKey = N'StoreName')" in enable_settings_sql, \
    'sql/16 must seed the store name only when it is missing'

# One live identity object drives every surface.
store_identity = store_identity_path.read_text(encoding='utf-8')
for member in ('INotifyPropertyChanged', 'public static StoreIdentity Current', 'public static void Apply(',
               'PropertyChanged?.Invoke(instance, new PropertyChangedEventArgs(null))', 'public string WindowTitle',
               'public string Headline', 'public string ContactLine', 'public string ServiceCenterHeader'):
    assert member in store_identity, f'store identity missing: {member}'
assert 'PosFormatting.CurrencyLabel = saved.CurrencyLabel;' in store_identity, \
    'amount formatting must follow the configured currency label'
assert 'public static string CurrencyLabel { get; set; } = "ج.م";' in pos_models_text, \
    'the shared formatter must accept the configured currency label'
app_xaml = (root / 'src/ProjectZ/App.xaml').read_text(encoding='utf-8')
assert 'x:Key="StoreIdentity"' in app_xaml, 'the store identity must be a live application resource'

# The program name reaches every window title, the sidebar, the login screen and the receipts.
for xaml_path in (root / 'src/ProjectZ/Views').glob('*.xaml'):
    text = xaml_path.read_text(encoding='utf-8')
    # Only Window roots have a title; UserControls live inside the main window.
    if '<Window x:Class="ProjectZ.Views.' not in text:
        continue
    # A window title is either a direct binding to the store identity or a MultiBinding whose first
    # source is that identity; either way the store name (the program name) leads the caption.
    direct = re.search(r'Title="([^"]*)"', text)
    branded_direct = direct is not None and 'StaticResource StoreIdentity' in direct.group(1)
    branded_multi = '<MultiBinding' in text and 'Binding Path="Name" Source="{StaticResource StoreIdentity}"' in text
    assert branded_direct or branded_multi, \
        f'{xaml_path.name}: window title must follow the store name'
login_text = (root / 'src/ProjectZ/Views/LoginWindow.xaml').read_text(encoding='utf-8')
main_xaml_text = (root / 'src/ProjectZ/Views/MainWindow.xaml').read_text(encoding='utf-8')
assert 'StaticResource StoreIdentity' in login_text and 'StaticResource StoreIdentity' in main_xaml_text
for receipt in ('InvoicePrintPreviewWindow.xaml', 'PurchaseInvoicePrintPreviewWindow.xaml',
                'MaintenanceReceiptPreviewWindow.xaml'):
    receipt_text = (root / 'src/ProjectZ/Views' / receipt).read_text(encoding='utf-8')
    assert 'StaticResource StoreIdentity' in receipt_text, f'{receipt}: receipt header must follow the store name'
    assert 'ProjectZ' not in re.sub(r'x:Class="[^"]*"', '', receipt_text), f'{receipt}: hard-coded brand text remains'

# Startup and the screen wiring.
app_cs = (root / 'src/ProjectZ/App.xaml.cs').read_text(encoding='utf-8')
assert 'StoreIdentity.Apply(new SettingsRepository(connectionSettings).Load());' in app_cs, \
    'startup must load the store identity before the login window'
assert 'new SettingsRepository(connectionSettings, user.Id)' in app_cs
main_viewmodel_text = main_viewmodel
assert 'GeneralSettings = new GeneralSettingsViewModel(' in main_viewmodel_text
assert 'IsGeneralSettingsSelected' in main_xaml_text and 'GeneralSettingsView DataContext="{Binding GeneralSettings}"' in main_xaml_text
assert 'GeneralSettings.RefreshFromDatabase();' in main_viewmodel_text

settings_vm = settings_vm_path.read_text(encoding='utf-8')
for wiring in ('repository.Load(', 'repository.Save(', 'StoreIdentity.Apply(saved)', 'StoreNameHint', 'IsDirty'):
    assert wiring in settings_vm, f'general settings screen is not wired: {wiring}'
settings_xaml = settings_view_path.read_text(encoding='utf-8')
for token in ('StoreName', 'Address', 'Phone', 'CurrencyLabel', 'LowStockAlertText', 'FooterNote',
              'PreviewTitle', 'PreviewHeadline', 'SaveCommand', 'ReloadCommand', 'ResetDefaultsCommand'):
    assert token in settings_xaml, f'general settings UI missing expected token: {token}'
assert 'مظهر' in settings_xaml and 'نسخ' in settings_xaml, 'the screen must say themes/backup live elsewhere'
assert 'ادارة المظهر' not in settings_xaml

# The add-product form starts from the configured default low-stock alert.
assert 'StoreIdentity.Current.DefaultLowStockAlert' in add_product_vm_text
print('PASS: general settings save the store identity in dbo.SystemSettings and rename the program everywhere')
# Wallet transfers: the ledger row must name its source transfer id, because dbo.FinancialTransactions
# carries CK_FinancialTransactions_Source ((type IS NULL AND id IS NULL) OR (type and id both set)) and
# CK_FinancialTransactions_Amount (Amount > 0). Writing N'CustomerTransfer' with a NULL id made every
# Vodafone Cash / InstaPay deposit, withdrawal, edit and reversal fail with SQL 547, surfaced to the user as
# "لا يمكن إتمام العملية لارتباط السجل بحساب أو حركة أخرى".
financial_repository = (root / 'src/ProjectZ/Data/FinancialRepository.cs').read_text(encoding='utf-8')
assert "N'CustomerTransfer',@Source" in financial_repository, 'wallet ledger rows must carry SourceEntityId'
assert "N'CustomerTransfer',NULL" not in financial_repository, 'wallet ledger rows must not leave SourceEntityId NULL'
assert financial_repository.count('RecordWalletMovement(connection, transaction,') == 8
assert financial_repository.count(', actorUserId, transferId);') == 8, \
    'every wallet ledger row must keep its actor and be tied to its transfer'
assert 'if (signedAmount == 0m) return;' in financial_repository, 'a zero-value ledger row violates CK_FinancialTransactions_Amount'
assert "sql.Message.Contains(\"CHECK constraint\"" in financial_repository, 'error 547 must not blame a foreign key for a CHECK violation'
for ledger_insert in re.finditer(r'INSERT dbo\.FinancialTransactions\([^)]*SourceEntityType,SourceEntityId\)(.*?);"', financial_repository, re.S):
    tail = ledger_insert.group(1).split('VALUES', 1)[-1].rstrip().rstrip(')')
    source_type, source_id = [part.strip() for part in tail.rsplit(',', 2)[-2:]]
    both_null = source_type.upper() == 'NULL' and source_id.upper() == 'NULL'
    both_set = source_type.startswith('@') or source_type.startswith("N'")
    assert (both_null or both_set) and not (source_type.upper() == 'NULL') ^ (source_id.upper() == 'NULL'), \
        f'unpaired SourceEntityType/SourceEntityId in ledger insert: {source_type}, {source_id}'
    if both_set:
        assert source_id.startswith('@'), f'a named source type needs a source id: {source_type}, {source_id}'
print('PASS: wallet transfers write paired SourceEntityType/SourceEntityId ledger rows and explain 547 honestly')
print('NOT RUN: .NET build, C# tests, WPF execution, SQL Server 2016 SP1, LAN acceptance, EXE publish')
