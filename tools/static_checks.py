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
# 50 CREATE TABLE statements (including four purchase settlement/trace tables in 25): 06 creates the 39 business tables + Users, 01 creates Users,
# and 16/19/20/21 repeat the movement, expense-section, expense and expense-payment tables
# idempotently so an existing database can be upgraded without re-running 06.
assert len(re.findall(r'CREATE\s+TABLE\s+', sql, re.I)) == 50
assert re.search(r'SET\s+COMPATIBILITY_LEVEL\s*=\s*130', sql, re.I)
assert 'CREATE DATABASE [ProjectZ]' in sql
assert '[ProjectZClient]' in (root / 'sql/04_SetupLanSqlAuth.sql').read_text(encoding='utf-8')
full_schema = (root / 'sql/06_CreateFullSchema.sql').read_text(encoding='utf-8')
assert len(re.findall(r'CREATE\s+TABLE\s+', full_schema, re.I)) == 40
reporting_views = (root / 'sql/07_CreateReportingViews.sql').read_text(encoding='utf-8')
expected_tables = {
    'Users', 'Roles', 'Permissions', 'UserRoles', 'RolePermissions', 'SystemSettings', 'DocumentSequences',
    'Customers', 'Suppliers', 'Categories', 'Products', 'FinancialAccounts', 'CustomerTransfers',
    'FinancialTransactions', 'InventoryUnits', 'StockMovements', 'PurchaseInvoices', 'PurchaseInvoiceLines',
    'PurchaseInvoiceLineUnits', 'PurchaseReturns', 'PurchaseReturnLines', 'PurchasePayments',
    'PurchaseInvoiceActivities', 'SalesInvoices', 'SalesInvoiceLines', 'SalesInvoiceLineUnits', 'SalesPayments',
    'SalesInvoiceActivities', 'ReturnExchanges', 'ReturnExchangeLines', 'ReturnExchangeUnits',
    'MaintenanceOrders', 'MaintenanceOrderParts', 'MaintenancePayments', 'MaintenanceActivities',
    'MaintenanceSpareParts', 'MaintenanceSparePartMovements', 'ExpenseCategories', 'Expenses',
    'ExpensePayments', 'AuditLog', 'SupplierCreditNotes', 'SupplierCreditApplications', 'SupplierCreditReceipts', 'PurchaseReturnUnits'
}
created_tables = set(re.findall(r'CREATE\s+TABLE\s+dbo\.([A-Za-z0-9_]+)', sql, re.I))
assert created_tables == expected_tables, f'schema table mismatch: missing={expected_tables-created_tables}, extra={created_tables-expected_tables}'
assert set(re.findall(r'CREATE\s+OR\s+ALTER\s+VIEW\s+dbo\.(\w+)', reporting_views, re.I)) == {
    'vwProductStock', 'vwSalesLineValuation', 'vwSalesInvoiceSummary', 'vwPurchaseInvoiceSummary',
    'vwMaintenanceOrderSummary', 'vwFinancialAccountDailyUsage', 'vwPurchaseDocumentValue'
}
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
print('PASS: complete ProjectZ database schema, 40 base tables + four purchase settlement tables, seven base reporting views, compatibility 130')

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

login_shell = next(element for element in login_root if element.tag.endswith('Grid'))
assert any(element.tag.endswith('ContentControl') and element.attrib.get('Style') == '{StaticResource ThemeBackdropStyle}' for element in login_shell), 'login must render the active theme backdrop'
login_content = next(element for element in login_shell if element.tag.endswith('Grid'))
login_panels = [element for element in login_content if element.tag.endswith('Border')]
assert login_content.attrib.get('FlowDirection') == 'LeftToRight'  # stable physical column order
assert [panel.attrib.get('Grid.Column') for panel in login_panels] == ['0', '1']
assert all(panel.attrib.get('FlowDirection') == 'RightToLeft' for panel in login_panels)
main_shell = next(element for element in main_root if element.tag.endswith('Grid'))
assert any(element.tag.endswith('ContentControl') and element.attrib.get('Style') == '{StaticResource ThemeBackdropStyle}' for element in main_shell), 'main window must render the active theme backdrop'
main_content = next(element for element in main_shell if element.tag.endswith('Grid'))
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
    ('التقارير', ['تقارير المبيعات', 'تقارير المشتريات', 'تقارير الصيانة',
                  'تقارير المالية', 'تقارير المصروفات', 'اجمالي التقارير']),
    ('المصروفات', ['اضافة مصروف', 'فواتير المصروفات', 'تصنيفات المصروفات']),
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
# «التقارير» and «المصروفات» were added as navigation only: they sit between
# «العملاء والموردين» and «المستخدمين», and must not gain a screen host or a screen flag
# until their screens are actually implemented.
menu_titles = [title for title, _items in actual_menu]
customers_pos = menu_titles.index('العملاء والموردين')
users_pos = menu_titles.index('المستخدمين')
assert customers_pos < menu_titles.index('التقارير') < menu_titles.index('المصروفات') < users_pos, \
    'التقارير and المصروفات belong between العملاء والموردين and المستخدمين'
assert 'IsReportsSelected' not in main_viewmodel, \
    'the reports category is still navigation-only and must not claim a screen yet'
assert 'views:SalesReportsView' in main_xaml and 'IsSalesReportsSelected' in main_viewmodel, 'sales reports must have their own host'
assert 'IsExpenseCategoriesSelected' in main_viewmodel and 'views:ExpenseCategoriesView' in main_xaml, \
    'the expense categories item is implemented and must host its real screen'
assert 'IsExpenseInvoicesSelected' in main_viewmodel and 'views:ExpenseInvoicesView' in main_xaml, \
    'the expense invoices item is implemented and must host its real register screen'
assert 'docs/REPORTS_EXPENSES.md' in (root / 'README.md').read_text(encoding='utf-8'), \
    'README must document the menus-only categories'
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
assert 'SelectItem(salesCategory.Items[0]);' in main_viewmodel
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
    'HasInvoiceReduction',
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

# The public client sends selections and expected versions, never a refund amount or return price.
sale_posting = (root / 'sql/22_EnableSaleSettlement.sql').read_text(encoding='utf-8')
for proc in ('IssueSale', 'CollectSalePayment', 'AdjustSale', 'UpdateSaleNotes'):
    assert f'Post("dbo.{proc}"' in sales_repository
    assert f'GRANT EXECUTE ON OBJECT::dbo.{proc} TO [ProjectZClient]' in sale_posting
assert 'public List<ReturnExchangeRecord> LoadReturnExchanges()' in sales_repository
assert 'record ReturnLineRequest(int SalesInvoiceLineId, int GoodQuantity, int DamagedQuantity)' in sales_repository
assert '"@ExpectedVersion"' in sales_repository
for table in ('SalesPayments', 'SalesInvoices', 'SalesInvoiceLineUnits', 'ReturnExchanges', 'ReturnExchangeLines', 'ReturnExchangeUnits'):
    assert f'DENY INSERT, UPDATE, DELETE ON OBJECT::dbo.{table} TO [ProjectZClient]' in sale_posting
for invariant in ('WITH (UPDLOCK,HOLDLOCK)', 'RowVersion=@ExpectedVersion', 'GROUP BY p.FinancialAccountId',
                  'dbo.vwSalesInvoiceSummary', 'a.CurrentBalance>=p.Amount', 'SET AllocationStatus=1', 'dbo.SaleShare('):
    assert invariant in sale_posting, f'missing sale posting guard: {invariant}'

# Cross-screen wiring: the sales repository feeds the screen and invoice writes reload stock from SQL.
assert 'Invoices.StockReloadRequested = ReloadInventoryOnly;' in main_viewmodel
assert 'NotifyInventoryChanged?.Invoke' not in return_exchange_vm
assert 'notifyInventoryChanged?.Invoke();' in return_exchange_vm
assert 'salesRepository);' in main_viewmodel
assert "if (!ReturnExchange.RefreshFromDatabase())" in main_viewmodel

# Permissions script is idempotent and grants only the return/exchange tables.
assert 'GRANT SELECT ON OBJECT::dbo.ReturnExchanges TO [ProjectZClient];' in return_exchange_permissions
assert 'GRANT SELECT ON OBJECT::dbo.ReturnExchangeLines TO [ProjectZClient];' in return_exchange_permissions
assert 'GRANT SELECT ON OBJECT::dbo.ReturnExchangeUnits TO [ProjectZClient];' in return_exchange_permissions
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

# Purchase settlement is owned by the database. Never restore the old ad-hoc edit/payment path.
purchase_settlement = (root / 'sql/25_EnablePurchaseSettlement.sql').read_text(encoding='utf-8')
for proc in ('CreatePurchaseDocument','PayPurchaseDocument','EditPurchaseDocument','ReturnPurchaseDocument',
             'ApplySupplierCredit','ReceiveSupplierCredit','DeleteUnsettledPurchase'):
    assert f'PROCEDURE dbo.{proc} ' in purchase_settlement and f'Post("{proc}"' in purchase_repository
    assert f'GRANT EXECUTE ON OBJECT::dbo.{proc} TO [ProjectZClient]' in purchase_settlement
for token in ('INSERT dbo.PurchaseReturnLines','INSERT dbo.PurchaseReturnUnits','INSERT dbo.SupplierCreditNotes',
              'INSERT dbo.SupplierCreditApplications','INSERT dbo.SupplierCreditReceipts','EXEC dbo.LockPurchaseDocument'):
    assert token in purchase_settlement
assert 'SyncChannel' not in purchase_repository and 'INSERT dbo.PurchasePayments' not in purchase_repository
def installed_procedure(script, name):
    # 24/25 compile each module in its own child batch of one atomic installer.
    match = re.search(r"EXEC sys\.sp_executesql N'(CREATE OR ALTER PROCEDURE dbo\." + re.escape(name) + r"\b(?:''|[^'])*)';", script)
    assert match, f'missing installer definition: {name}'
    return match[1].replace("''", "'")

edit_proc = installed_procedure(purchase_settlement, 'EditPurchaseDocument')
assert 'WritePurchasePayment' not in edit_proc and 'Products SET CostPrice' not in edit_proc

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
for command in ('repo.PayInvoice', 'repo.ReturnToSupplier', 'repo.CancelInvoice', 'repo.ApplyCredit', 'repo.ReceiveCredit',
                'repo.DeleteInvoice', 'repo.UpdateInvoice', 'repository.LoadInvoicesInto'):
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
for table in ('PurchaseInvoices','PurchaseInvoiceLines','PurchaseInvoiceLineUnits','PurchasePayments','PurchaseReturns','PurchaseReturnLines'):
    assert f'GRANT SELECT ON OBJECT::dbo.{table} TO [ProjectZClient]' in purchase_permissions
    assert f'DENY INSERT,UPDATE,DELETE ON OBJECT::dbo.{table} TO [ProjectZClient]' in purchase_settlement
assert 'GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::dbo.Suppliers TO [ProjectZClient]' in purchase_permissions
# Every SQL parameter declared in a purchase statement must also be bound in C#, and every
# INSERT..OUTPUT inserted.Id must be read back. A missing binding makes SQL Server reject the whole
# save with 137 ("must declare the scalar variable"), which surfaced as the generic
# "تعذر حفظ فاتورة الشراء في قاعدة البيانات." message on the purchase entry screen.
def _unbound_parameters(source, path):
    import re as _re
    missing = []
    # SQL usually starts on the NEXT line inside a C# verbatim string (@"..."). Matching
    # only "SELECT... immediately after the quote silently skipped the broken ReceiveUnits
    # INSERT and let SQL 137 (undeclared @Invoice) reach production. Cover both string forms.
    for match in _re.finditer(r'@\$?"((?:[^"]|"")*)"|"(?:\\.|[^"\\])*"', source):
        statement = (match.group(1) if match.group(1) is not None else match.group(0)[1:-1]).replace('""', '"')
        if not _re.match(r'^\s*(?:SELECT|INSERT|UPDATE|DELETE|MERGE)\b', statement, _re.I):
            continue
        declared = set(_re.findall(r'@[A-Za-z_]\w*', statement))
        if not declared:
            continue
        tail = source[match.end():]
        execution = _re.search(r'Execute(?:NonQuery|Scalar|Reader)\s*\(', tail)
        body = tail[:execution.start()] if execution else tail
        bound = set(_re.findall(r'"(@\w+)"', body))
        for name in sorted(declared - bound):
            missing.append(f'{path}:{source[:match.start()].count(chr(10)) + 1} needs a binding for {name}')
    return missing

# Regression-test the checker itself: the old regex accepted both of these broken commands.
for broken in (
    'var cmd = NewCommand(@"\nINSERT dbo.T(x) VALUES(@Invoice);"); cmd.ExecuteNonQuery();',
    'var cmd = NewCommand("INSERT dbo.T(x) VALUES(@Invoice);"); cmd.ExecuteNonQuery();',
):
    assert any('@Invoice' in item for item in _unbound_parameters(broken, 'sample.cs')), \
        'parameter checker must inspect multiline verbatim SQL and ordinary SQL strings'
unbound = _unbound_parameters(purchase_repository, 'PurchaseRepository.cs')
assert not unbound, 'unbound SQL parameters break the purchase save: ' + '; '.join(unbound)
assert 'SET @Line=CONVERT(int,SCOPE_IDENTITY())' in purchase_settlement and 'INSERT dbo.PurchaseInvoiceLineUnits' in purchase_settlement
assert '137 or 8178' in purchase_repository, 'an unbound SQL parameter must be reported instead of a generic save error'
assert 'SET ARITHABORT ON;' in purchase_repository and 'SET NUMERIC_ROUNDABORT OFF;' in purchase_repository, \
    'purchase writes to filtered indexes must restore the required session options on pooled connections'
print('PASS: purchase SQL parameter checker covers multiline verbatim commands (SQL 137 regression)')
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
              'isBulkReload', 'ValidateWorkshopFieldLengths', 'MaintenancePosting.Deliver', 'RefreshFilteredOrders(selectedId)'):
    assert token in maintenance_actions_vm, f'MaintenanceActions view model missing expected token: {token}'
for token in ('DataConnectionBadgeText', 'LastSyncedDisplay'):
    assert token in maintenance_actions_xaml, f'MaintenanceActions UI is not SQL-backed: {token}'
assert 'TechnicianFilterOptions = new[] { "كل الفنيين" }' not in maintenance_actions_vm, \
    'the technician directory is hard-coded from the design samples again'
assert 'DatabaseRowVersion' in maintenance_order_model, 'MaintenanceOrderDesignItem.DatabaseRowVersion is missing'
for token in ('MaintenanceTicketKey.Open', 'key.Version is not { Length: 8 }', 'LoadTechnicianDirectory'):
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

# --- Spare-parts screen: SQL CRUD, suppliers, ledger and cross-screen links (sql/16) ---------
enable_spare_sql_path = root / 'sql/16_EnableMaintenanceSpareParts.sql'
assert enable_spare_sql_path.is_file(), 'missing sql/16_EnableMaintenanceSpareParts.sql'
enable_spare_sql = enable_spare_sql_path.read_text(encoding='utf-8')
for token in ('CREATE TABLE dbo.MaintenanceSparePartMovements', 'UX_MaintenanceSpareParts_Barcode',
              'UX_MaintenanceSpareParts_Name', 'IX_MaintenanceSparePartMovements_Part_Date',
              'IX_MaintenanceOrderParts_SparePart',
              'GRANT SELECT, INSERT ON OBJECT::dbo.MaintenanceSparePartMovements TO [ProjectZClient]',
              'GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::dbo.MaintenanceSpareParts TO [ProjectZClient]',
              'THROW 50077', 'THROW 50079', 'CK_MaintenanceOrderParts_Source',
              "COL_LENGTH(N'dbo.MaintenanceOrderParts', N'SparePartId')"):
    assert token in enable_spare_sql, f'sql/16 missing expected guard: {token}'
assert 'CREATE TABLE dbo.MaintenanceSpareParts' not in enable_spare_sql, 'sql/16 must upgrade, not recreate, dbo.MaintenanceSpareParts'
for column in ('SupplierId', 'Barcode', 'Unit', 'ReorderQuantity', 'StorageLocation', 'Notes',
               'LastPurchaseAtUtc', 'LastIssueAtUtc'):
    assert f"COL_LENGTH(N'dbo.MaintenanceSpareParts', N'{column}')" in enable_spare_sql, f'sql/16 does not add {column}'
assert 'dbo.MaintenanceSparePartMovements' in full_schema and 'dbo.MaintenanceSpareParts' in full_schema

spare_parts_repository = (root / 'src/ProjectZ/Data/MaintenanceRepository.cs').read_text(encoding='utf-8')
for operation in ('LoadSparePartsInto(', 'LoadSparePartSuppliers(', 'SaveSparePart(', 'DeleteSparePart(',
                  'IssuePart(', 'RemovePart(', 'RestockSparePart(',
                  'LoadSparePartMovementsInto(', 'InsertSparePartMovement('):
    assert operation in spare_parts_repository, f'missing spare-part SQL operation: {operation}'
for guard in ('BeginTransaction(IsolationLevel.Serializable)', 'UPDLOCK, HOLDLOCK',
              'dbo.MaintenanceSparePartMovements', 'dbo.ChangeMaintenancePart',
              'RowVersion', 'COALESCE(@Supplier, sp.SupplierId)',
              'MaintenanceSparePartMovementKind.Purchase'):
    assert guard in spare_parts_repository, f'missing spare-part persistence guard: {guard}'
maintenance_sql = (root / 'sql/24_EnableMaintenanceSettlement.sql').read_text(encoding='utf-8')
release_line = installed_procedure(maintenance_sql, 'ReleaseMaintenanceLine')
assert 'WHERE Id=@Spare;' in release_line and 'IsActive' not in release_line, 'return a saved line even on a stopped spare'
assert 'يوجد سجل صرف أو حركات مخزون' in spare_parts_repository, 'deleting a part with history must deactivate it'

spare_parts_vm = (root / 'src/ProjectZ/ViewModels/MaintenanceSparePartsViewModel.cs').read_text(encoding='utf-8')
for token in ('LoadSparePartsInto(', 'SaveSparePart(', 'RestockSparePart(', 'DeleteSparePart(',
              'LoadSparePartMovementsInto(', 'BarcodeText', 'SelectedSupplier', 'SupplierOptions',
              'RestockPartCommand', 'OpenSupplierCommand', 'OpenCustomerCommand', 'OpenOrderCommand',
              'FilteredMovements', 'InventorySummaryText', 'SelectPartFromExternal(', 'ConnectionBadgeText'):
    assert token in spare_parts_vm, f'spare-parts view model missing expected token: {token}'

for token in ('SupplierOptions', 'SelectedSupplier', 'BarcodeText', 'RestockPartCommand',
              'FilteredMovements', 'ConnectionBadgeText', 'MovementsTabTitle',
              'OpenSupplierCommand', 'OpenCustomerCommand', 'OpenOrderCommand', 'StockFilterOptions',
              'InventorySummaryText', 'SelectWorkspaceTabCommand'):
    assert token in maintenance_spare_parts_xaml, f'MaintenanceSpareParts UI missing expected token: {token}'
assert 'تصميم الواجهة' not in maintenance_spare_parts_xaml, 'the spare-parts screen still advertises a design-only badge'

suppliers_xaml_text = (root / 'src/ProjectZ/Views/SuppliersView.xaml').read_text(encoding='utf-8')
suppliers_vm_text = (root / 'src/ProjectZ/ViewModels/SuppliersViewModel.cs').read_text(encoding='utf-8')
for token in ('SelectedSupplierSpareParts', 'SelectedSparePartsTabTitle', 'OpenSparePartCommand',
              'SelectedSupplierSparePartsSummary'):
    assert token in suppliers_xaml_text, f'supplier screen is not linked to spare parts: {token}'
for token in ('maintenanceSpareParts', 'SetSpareParts(', 'SelectSupplierFromExternal('):
    assert token in suppliers_vm_text, f'supplier view model is not linked to spare parts: {token}'

customers_xaml_text = (root / 'src/ProjectZ/Views/CustomersView.xaml').read_text(encoding='utf-8')
customers_vm_text = (root / 'src/ProjectZ/ViewModels/CustomersViewModel.cs').read_text(encoding='utf-8')
for token in ('SelectedCustomerSpareParts', 'SelectedSparePartsTabTitle', 'OpenSparePartCommand',
              'SelectedSparePartsSummary'):
    assert token in customers_xaml_text, f'customer screen is not linked to spare parts: {token}'
for token in ('CustomerSparePartUsageRow', 'SelectedCustomerSpareParts', 'SelectCustomerFromExternal(',
              'OpenSparePartRequested'):
    assert token in customers_vm_text, f'customer view model is not linked to spare parts: {token}'

assert 'MaintenanceSpareParts.OpenSupplierRequested' in main_viewmodel
assert 'MaintenanceSpareParts.OpenCustomerRequested' in main_viewmodel
assert 'MaintenanceSpareParts.OpenOrderRequested' in main_viewmodel
assert 'GoToSparePartsCommand' in maintenance_actions_xaml_path.read_text(encoding='utf-8')
assert 'Customers.OpenSparePartRequested' in main_viewmodel
assert 'sharedMaintenanceSpareParts' in main_viewmodel
maintenance_actions_code = (root / 'src/ProjectZ/ViewModels/MaintenanceActionsViewModel.cs').read_text(encoding='utf-8')
for token in ('r.IssuePart(', 'r.RemovePart(', 'r.Cancel(', 'r.Reopen('):
    assert token in maintenance_actions_code, f'missing atomic workshop action: {token}'
for vm in ('MaintenanceInvoicesViewModel', 'MaintenanceInvoiceDetailsViewModel', 'MaintenanceActionsViewModel', 'CustomersViewModel'):
    caller = (root / f'src/ProjectZ/ViewModels/{vm}.cs').read_text(encoding='utf-8')
    assert 'MaintenancePosting.Deliver(' in caller and 'pendingPayments' not in caller, f'{vm} bypasses versioned settlement'
assert 'Order.Status =' not in maintenance_actions_dialogs and 'Order.FinalPaymentCashPaid +=' not in maintenance_actions_dialogs
for token in ('dbo.LockMaintenanceTicket', '@Saved<>@Version', 'dbo.PriceMaintenanceTicket', 'dbo.CheckMaintenanceStock',
              'dbo.ReleaseMaintenanceLine', 'RefundOfPaymentId', 'CurrentBalance>=@Amount', 'SYSUTCDATETIME()',
              'ProjectZMaintenanceSupervisor', 'XACT_ABORT ON'):
    assert token in maintenance_sql, f'missing server-side maintenance guard: {token}'
for table in ('MaintenanceOrders', 'MaintenanceOrderParts', 'MaintenancePayments'):
    assert f'DENY INSERT,UPDATE,DELETE ON OBJECT::dbo.{table} TO [ProjectZClient]' in maintenance_sql
print('PASS: spare-parts screen persists CRUD, suppliers, restock and the movement ledger to SQL and links every screen')

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
for token in ('BuildStatement', 'RunningBalance', 'DocumentedReturnValue', 'NetCashOutflow', 'RemainingPayable', 'AvailableCredit', 'DocumentDelta', 'i.Postings'):
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
    'AddVodafoneWalletCommand', 'AddInstaPayWalletCommand', 'WalletCardTemplate',
    'ItemsSource="{Binding VodafoneWallets}"', 'ItemsSource="{Binding InstapayWallets}"',
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

# --- «حساباتي» refresh: reloads must update the same account objects ---------------------------
# Regression: LoadAccountsInto cleared the collection and added new items, while the cash card binds
# CashAccount.BalanceDisplay, so the «النقدية» amount stayed frozen until the app restarted — even
# after «تحديث». Reloads now merge into the live objects and the screen re-notifies the cash card.
accounts_repository_text = (root / 'src/ProjectZ/Data/FinancialRepository.cs').read_text(encoding='utf-8')
assert 'FinancialAccountSnapshot.Apply(target, loaded);' in accounts_repository_text, \
    'account reloads must merge into the live rows, not replace them'
load_accounts_body = accounts_repository_text.split('public void LoadAccountsInto(', 1)[1].split('/// <summary>', 1)[0]
assert 'target.Clear();' not in load_accounts_body, \
    'clearing the collection detaches the objects the UI binds (the frozen cash balance)'
assert 'class FinancialAccountSnapshot' in accounts_model_path.read_text(encoding='utf-8'), \
    'missing the in-place account snapshot helper'
for token in ('public static void Apply(', 'CopyValues(existing, source)', 'target.Move(currentIndex, index)',
              'target.RemoveAt(target.Count - 1)'):
    assert token in accounts_model_path.read_text(encoding='utf-8'), f'the account snapshot helper is missing: {token}'
for token in ('CashAccount => Accounts.FirstOrDefault(a => a.IsCash)', 'HasCashAccount',
              'Changed(nameof(CashAccount))', 'EnsureSelectedAccountIsAttached',
              'RequireCashAccount', 'Accounts.Contains(SelectedAccount)',
              'Transactions.Count == 0 ? 1 : Transactions.Max'):
    assert token in accounts_vm_text, f'the accounts screen is missing the refresh fix: {token}'
assert 'if (ReferenceEquals(item, financeCategory.Items[0]))\n            MyAccounts.NotifyExternalDataChanged();' in main_viewmodel, \
    'opening «حساباتي» must reload the balances from SQL Server'
wpf_accounts_test = (root / 'tests/ProjectZ.Appearance.Wpf.Tests/Program.cs').read_text(encoding='utf-8')
for token in ('FinancialAccountSnapshot.Apply', '"Applying an account snapshot updates the same cash object instead of replacing it"',
              'The cash row raises BalanceDisplay so the cash card re-reads the new amount',
              'The accounts screen exposes the cash row the cash card binds to'):
    assert token in wpf_accounts_test, f'the WPF smoke test does not guard the cash refresh: {token}'
accounts_sql_project = (root / 'tests/ProjectZ.Purchase.Sql.Tests/ProjectZ.Purchase.Sql.Tests.csproj'
                        ).read_text(encoding='utf-8')
for token in ('FinancialRepository.cs', 'VodafoneTransferDesignModels.cs', 'InstapayTransferDesignModels.cs'):
    assert token in accounts_sql_project, f'the LocalDB harness must compile the accounts layer: {token}'
accounts_sql_test = (root / 'tests/ProjectZ.Purchase.Sql.Tests/Program.cs').read_text(encoding='utf-8')
for token in ('Reloading the accounts keeps the same cash object the screen already binds',
              'The reloaded cash object carries the new balance instead of staying frozen at the startup value'):
    assert token in accounts_sql_test, f'the LocalDB harness does not guard the cash refresh: {token}'
accounts_doc = (root / 'docs/DATABASE.md').read_text(encoding='utf-8')
for token in ('FinancialAccountSnapshot', 'CashAccount.BalanceDisplay', 'النقدية'):
    assert token in accounts_doc, f'docs/DATABASE.md must explain the accounts refresh: {token}'
assert '| 42 |' in (root / 'docs/ACCEPTANCE.md').read_text(encoding='utf-8'), \
    'ACCEPTANCE must gain a row for the frozen cash balance fix'
print('PASS: accounts reloads merge into the live rows so the cash card and the ledger refresh without a restart')

# --- records screens: a stopped/deleted line must not crash the edit/reverse flow ----------------
# Regression: pressing «تعديل» on a Vodafone Cash operation whose line was stopped or deleted in
# «حساباتي» crashed with System.InvalidOperationException "Sequence contains no matching element"
# (Enumerable.ThrowNoMatchException), because the records view models resolved the wallet with
# accounts.Accounts.First(w => w.Id == operation.WalletId). LoadAccountsInto only loads IsActive = 1
# rows, so a saved operation can outlive its wallet. Every lookup is FirstOrDefault plus an Arabic
# status message, and the nullable cash row is guarded the same way.
for records_name in ('VodafoneRecordsViewModel.cs', 'InstapayRecordsViewModel.cs'):
    records_text = (root / 'src/ProjectZ/ViewModels' / records_name).read_text(encoding='utf-8')
    assert 'Accounts.First(' not in records_text, f'{records_name} still uses First(...) on the account list'
    assert records_text.count('FirstOrDefault(w => w.Id == accountId)') == 1, \
        f'{records_name} must expose exactly one guarded wallet lookup'
    for token in ('const string MissingWalletMessage',
                  'const string MissingCashMessage',
                  'var oldWallet = FindWallet(operation.WalletId);',
                  'if (FindWallet(SelectedOperation.WalletId) is not { } wallet)',
                  'if (wallet is null) { error = MissingWalletMessage; return false; }',
                  'if (accounts.CashAccount is not { } cash) { error = MissingCashMessage; return false; }',
                  'if (cashDelta < 0m && cash.Balance - original.CashDelta < -cashDelta)',
                  'if (accounts.CashAccount is not { } cash) return MissingCashMessage;'):
        assert token in records_text, f'{records_name} is missing a guarded edit path: {token}'
    for token in ('RequestReverse(fromDelete: true)', 'RequestReverse(fromDelete: false)',
                  'if (wallet is null) { SetStatus(MissingWalletMessage, true); return; }',
                  'editTarget = new(SelectedOperation, SelectedOperation.RowVersion.ToArray())',
                  'UpdateCustomerTransfer(operation.Id, target.Version,',
                  'var version = operation.RowVersion.ToArray()', 'ReverseCustomerTransfer(operation.Id, version,'):
        assert token in records_text, f'{records_name} must bind edit and both reversal commands to the opened transfer: {token}'
for transfer_name in ('VodafoneTransferViewModel.cs', 'InstapayTransferViewModel.cs'):
    transfer_text = (root / 'src/ProjectZ/ViewModels' / transfer_name).read_text(encoding='utf-8')
    assert 'accounts.CashAccount.Balance' not in transfer_text, \
        f'{transfer_name} still dereferences the nullable cash account'
    assert transfer_text.count('accounts.CashAccount is not { } cash') == 2, \
        f'{transfer_name} must guard the preview and the transfer save against a missing cash row'
    assert 'const string MissingCashMessage' in transfer_text, \
        f'{transfer_name} must explain a missing cash account in Arabic'
wpf_records_test = (root / 'tests/ProjectZ.Appearance.Wpf.Tests/Program.cs').read_text(encoding='utf-8')
for token in ('VodafoneRecordsViewModel.MissingWalletMessage',
              'InstapayRecordsViewModel.MissingWalletMessage',
              'VodafoneRecordsViewModel.MissingCashMessage',
              'VodafoneTransferViewModel.MissingCashMessage',
              'Editing an operation whose line is gone explains it in Arabic instead of throwing',
              'Saving an edit for an operation whose line is gone reports it instead of crashing',
              'Reversing an operation whose line is gone reports it instead of crashing',
              'The cash preview shows a dash, not a crash, when the cash account is missing'):
    assert token in wpf_records_test, f'the WPF smoke test does not guard the records edit flow: {token}'
sql_records_test = (root / 'tests/ProjectZ.Purchase.Sql.Tests/Program.cs').read_text(encoding='utf-8')
for token in ('A stopped line disappears from the active accounts while its transfer stays in the register',
              'Editing a transfer of a stopped line reports the stopped line in Arabic, not a duplicate-data error',
              'Reversing a transfer of a stopped line is refused with an Arabic financial error',
              'Re-activating the line lets the very same edit succeed'):
    assert token in sql_records_test, f'the LocalDB harness does not guard the stopped-line edit: {token}'
records_repository_text = (root / 'src/ProjectZ/Data/FinancialRepository.cs').read_text(encoding='utf-8')
assert 'FinancialDataException("المحفظة المحددة غير متاحة' in records_repository_text, \
    'EnsureAccountById must stay the single owner of the unavailable-wallet message'
wallet_posting = (root / 'sql/23_EnableCustomerTransferCorrections.sql').read_text(encoding='utf-8')
assert 'INSERT dbo.FinancialAccounts' not in wallet_posting, 'a missing or stopped account must never be substituted'
for token in ('Id=@OldWallet AND AccountKind=@Provider AND IsActive=1', 'المحفظة الأصلية موقوفة',
              'Id=@NewWallet AND AccountKind=@Provider AND IsActive=1'):
    assert token in wallet_posting, f'posting must explicitly check both wallets: {token}'
records_doc = (root / 'docs/DATABASE.md').read_text(encoding='utf-8')
for token in ('ThrowNoMatchException', 'IsActive = 1', 'MissingWalletMessage'):
    assert token in records_doc, f'docs/DATABASE.md must explain the stopped-line edit guard: {token}'
assert '| 43 |' in (root / 'docs/ACCEPTANCE.md').read_text(encoding='utf-8'), \
    'ACCEPTANCE must gain a row for the records edit crash fix'
print('PASS: a stopped or deleted line reports in Arabic instead of crashing the records edit/reverse flow')

# --- «تعيين افتراضية» / «تفعيل إيقاف»: the buttons must persist to dbo.FinancialAccounts ----------
# Regression: both commands only mutated the in-memory list (SetSelectedAsDefault looped over the
# local rows, ToggleSelectedAccount flipped IsActive locally), so the choice was lost after «تحديث»
# or a restart and the default badge came back on the previous wallet.
default_vm_text = (root / 'src/ProjectZ/ViewModels/MyAccountsViewModel.cs').read_text(encoding='utf-8')
for token in ('repository.SetDefaultAccount(account.Id, account.Kind);',
              'repository.SetAccountActive(account.Id, activate);',
              'foreach (var other in Accounts.Where(a => a.Kind == account.Kind)) other.IsDefault = ReferenceEquals(other, account);',
              'RefreshFilteredWallets(); NotifyKpis();'):
    assert token in default_vm_text, f'the accounts screen does not persist the wallet buttons: {token}'
assert 'foreach (var account in Accounts.Where(a => a.Kind == SelectedAccount.Kind)) account.IsDefault' not in default_vm_text, \
    '«تعيين افتراضية» still only reorders the local list without writing IsDefault to SQL Server'
default_repo_text = (root / 'src/ProjectZ/Data/FinancialRepository.cs').read_text(encoding='utf-8')
for token in ('public void SetDefaultAccount(int accountId, FinancialAccountKind kind)',
              'public void SetAccountActive(int accountId, bool isActive)',
              'WHERE AccountKind = @Kind AND IsDefault = 1 AND Id <> @Id;',
              'IsActive = 0, IsDefault = 0'):
    assert token in default_repo_text, f'the repository is missing the wallet-button persistence: {token}'
assert default_repo_text.count('WHERE AccountKind = @Kind AND IsDefault = 1 AND Id <> @Id;') == 2, \
    'both SetDefaultAccount and the account editor must clear defaults per kind only'
wpf_default_test = (root / 'tests/ProjectZ.Appearance.Wpf.Tests/Program.cs').read_text(encoding='utf-8')
for token in ('«تعيين افتراضية» flags the chosen wallet',
              'Only one line stays default per provider',
              'Making a Vodafone line default keeps the InstaPay default',
              'A stopped wallet is refused as default with an Arabic message',
              '«تفعيل / إيقاف» stops the wallet',
              'The cash account cannot be stopped from the toggle'):
    assert token in wpf_default_test, f'the WPF smoke test does not guard the wallet buttons: {token}'
sql_default_test = (root / 'tests/ProjectZ.Purchase.Sql.Tests/Program.cs').read_text(encoding='utf-8')
for token in ('«تعيين افتراضية» writes IsDefault = 1 for the chosen line in dbo.FinancialAccounts',
              'Making a Vodafone line default keeps the cash and InstaPay defaults intact',
              '«تفعيل / إيقاف» writes IsActive = 0 for the stopped wallet',
              'Stopping the default wallet clears its default flag instead of hiding a default row',
              'The cash account refuses the stop toggle and stays active'):
    assert token in sql_default_test, f'the LocalDB harness does not guard the wallet buttons: {token}'
default_doc = (root / 'docs/DATABASE.md').read_text(encoding='utf-8')
for token in ('SetDefaultAccount', 'SetAccountActive', 'UX_FinancialAccounts_OneDefaultPerKind'):
    assert token in default_doc, f'docs/DATABASE.md must explain the wallet-button persistence: {token}'
assert '| 44 |' in (root / 'docs/ACCEPTANCE.md').read_text(encoding='utf-8'), \
    'ACCEPTANCE must gain a row for the default-wallet persistence fix'
print('PASS: the default-wallet and activate/stop buttons persist per kind in dbo.FinancialAccounts')

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
    root / 'src/ProjectZ/ViewModels/AddExpenseViewModel.cs',
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
    root / 'src/ProjectZ/ViewModels/MaintenanceSparePartRestockViewModel.cs',
    root / 'src/ProjectZ/ViewModels/MaintenanceReceiptPreviewViewModel.cs',
    root / 'src/ProjectZ/Views/PosView.xaml.cs',
    root / 'src/ProjectZ/Views/AddCustomerWindow.xaml.cs',
    root / 'src/ProjectZ/Views/CustomersView.xaml.cs',
    root / 'src/ProjectZ/Views/AddSupplierWindow.xaml.cs',
    root / 'src/ProjectZ/Views/SuppliersView.xaml.cs',
    root / 'src/ProjectZ/Views/MyAccountsView.xaml.cs',
    root / 'src/ProjectZ/Views/AddExpenseView.xaml.cs',
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
    root / 'src/ProjectZ/Views/MaintenanceSparePartRestockWindow.xaml.cs',
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
    root / 'src/ProjectZ/Resources/ThemePalette.xaml',
    root / 'src/ProjectZ/Resources/ThemeAliases.xaml',
    root / 'src/ProjectZ/Resources/Theme.xaml',
    root / 'src/ProjectZ/Resources/PosStyles.xaml',
    root / 'src/ProjectZ/Resources/ThemeControls.xaml',
    root / 'src/ProjectZ/Resources/ThemeBackdrop.xaml',
    root / 'src/ProjectZ/Resources/PrintIsolation.xaml',
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
# The cash row is nullable, so the transfer posts through the guarded local it resolved above.
assert 'if (accounts.CashAccount is not { } cash) { SetStatus(MissingCashMessage, true); return; }' in vodafone_vm
assert 'cash.Balance += cashDelta;' in vodafone_vm
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
for required_rule in ('wallet.Balance += IsDepositSelected ? -amount : amount;', 'IsDepositSelected ? amount + commission : commission - amount', 'incoming: !IsDepositSelected', 'ValidateLimits(SelectedWallet', 'cash.Balance += cashDelta;'):
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
for guard in ('SET TRANSACTION ISOLATION LEVEL SERIALIZABLE', 'UPDLOCK,HOLDLOCK', 'dbo.DocumentSequences',
              'dbo.SalesPayments', 'dbo.FinancialTransactions', 'dbo.StockMovements', 'dbo.AuditLog',
              'INSERT dbo.SalesInvoiceLineUnits', 'CountsTowardLimits'):
    assert guard in sale_posting, f'missing POS persistence guard: {guard}'
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

# --- Expense sections screen («تصنيفات المصروفات» + ExpenseCategoryRepository + sql/19) ------
expense_repository_path = root / 'src/ProjectZ/Data/ExpenseCategoryRepository.cs'
assert expense_repository_path.is_file(), 'missing src/ProjectZ/Data/ExpenseCategoryRepository.cs'
expense_repository = expense_repository_path.read_text(encoding='utf-8')
enable_expense_sql_path = root / 'sql/19_EnableExpenseCategories.sql'
assert enable_expense_sql_path.is_file(), 'missing sql/19_EnableExpenseCategories.sql'
enable_expense_sql = enable_expense_sql_path.read_text(encoding='utf-8')
for operation in ('LoadAll', 'Create', 'Rename', 'Delete'):
    assert f'{operation}(' in expense_repository, f'missing expense-section SQL operation: {operation}'
for guard in ('BeginTransaction(IsolationLevel.Serializable)', 'UPDLOCK, HOLDLOCK', 'dbo.ExpenseCategories',
              'INSERT dbo.AuditLog', 'UPDATE dbo.ExpenseCategories', 'DELETE dbo.ExpenseCategories',
              "N'ExpenseCategories'"):
    assert guard in expense_repository, f'missing expense-section persistence guard: {guard}'
assert 'مرتبط بمصروفات' in expense_repository, 'deleting a used expense section must be refused with a clear message'
assert 'sql/19_EnableExpenseCategories.sql' in expense_repository, 'a missing table must point at sql/19'
assert 'GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::dbo.ExpenseCategories TO [ProjectZClient]' in enable_expense_sql
assert 'CREATE TABLE dbo.ExpenseCategories' in enable_expense_sql
assert 'IF NOT EXISTS (SELECT 1 FROM dbo.ExpenseCategories)' in enable_expense_sql, \
    'starter sections must be inserted only into an empty table'
for seeded in ('إيجار', 'فواتير', 'مرتبات', 'مواصلات', 'صيانة', 'أخرى'):
    assert f"N'{seeded}'" in enable_expense_sql, f'sql/19 is missing the starter section: {seeded}'
assert 'THROW 50060' in enable_expense_sql and 'THROW 50061' in enable_expense_sql
expense_categories_vm = (root / 'src/ProjectZ/ViewModels/ExpenseCategoriesViewModel.cs').read_text(encoding='utf-8')
for wiring in ('repository.LoadAll(', 'repository.Create(', 'repository.Rename(', 'repository.Delete(',
               'RefreshFromDatabase()', 'OpenConfirmDialogRequested', 'CategoriesChanged'):
    assert wiring in expense_categories_vm, f'expense sections screen is not wired to SQL: {wiring}'
for token in ('ExpenseDesignSeedData.CreateSampleExpenseCategories()', 'SearchText', 'NewCategoryDescription',
              'EditingDescription', 'RefreshCategoriesCommand', 'EmptyStateText', 'WithDescriptionDisplay'):
    assert token in expense_categories_vm, f'expense sections view model is missing: {token}'
expense_categories_view = (root / 'src/ProjectZ/Views/ExpenseCategoriesView.xaml').read_text(encoding='utf-8')
for token in ('FilteredCategories', 'SearchText', 'NewCategoryDescription', 'EditingDescription',
              'RefreshCategoriesCommand', 'EmptyStateText', 'AddCategoryCommand', 'BeginEditCommand',
              'DeleteCommand'):
    assert token in expense_categories_view, f'expense sections UI is missing expected token: {token}'
assert 'متصل بقاعدة البيانات' in expense_categories_view and 'بدون Backend' not in expense_categories_view
assert 'views:ExpenseCategoriesView' in main_xaml and 'IsExpenseCategoriesSelected' in main_viewmodel
assert 'ExpenseCategories = new ExpenseCategoriesViewModel(expenseCategoryRepository);' in main_viewmodel
assert 'ReferenceEquals(item, expensesCategory.Items[2])' in main_viewmodel, \
    'opening «تصنيفات المصروفات» must reload the sections from SQL'
assert 'new ExpenseCategoryRepository(connectionSettings, user.Id)' in (root / 'src/ProjectZ/App.xaml.cs').read_text(encoding='utf-8')
expense_doc = (root / 'docs/EXPENSE_CATEGORIES.md')
assert expense_doc.is_file(), 'missing docs/EXPENSE_CATEGORIES.md'
assert 'docs/EXPENSE_CATEGORIES.md' in (root / 'README.md').read_text(encoding='utf-8'), \
    'README must link the expense sections documentation'
print('PASS: expense sections screen reads and writes dbo.ExpenseCategories with serialized writes, audit rows and a used-section delete guard')
# --- Add-expense screen (dbo.Expenses + ExpenseRepository + sql/20) ---------------------------
assert 'CREATE TABLE dbo.Expenses' in full_schema, 'sql/06 must create dbo.Expenses'
expenses_table = full_schema.split('CREATE TABLE dbo.Expenses', 1)[1].split('/*', 1)[0]
assert 'CONSTRAINT UQ_Expenses_Number UNIQUE' in expenses_table
assert 'CONSTRAINT FK_Expenses_Categories FOREIGN KEY REFERENCES dbo.ExpenseCategories(Id)' in expenses_table
assert 'CONSTRAINT CK_Expenses_Amount CHECK (Amount > 0)' in expenses_table
assert 'ExpenseNumber NVARCHAR(50)' in expenses_table and 'ExpenseDate DATE' in expenses_table
assert 'FinancialAccountId' not in expenses_table, 'the account moved to dbo.ExpensePayments'
expense_payments_table = full_schema.split('CREATE TABLE dbo.ExpensePayments', 1)[1].split('/*', 1)[0]
assert 'CONSTRAINT FK_ExpensePayments_Expenses FOREIGN KEY REFERENCES dbo.Expenses(Id)' in expense_payments_table
assert 'CONSTRAINT FK_ExpensePayments_Accounts FOREIGN KEY REFERENCES dbo.FinancialAccounts(Id)' in expense_payments_table
assert 'CONSTRAINT CK_ExpensePayments_Amount CHECK (Amount > 0)' in expense_payments_table
assert 'CONSTRAINT UQ_ExpensePayments_Expense_Account UNIQUE (ExpenseId, FinancialAccountId)' in expense_payments_table
assert 'IX_Expenses_Date' in full_schema and 'IX_Expenses_Category_Date' in full_schema
assert 'IX_ExpensePayments_Account' in full_schema
schema_verification_text = (root / 'sql/08_VerifyFullSchema.sql').read_text(encoding='utf-8')
assert "N'Expenses'" in schema_verification_text and "N'ExpensePayments'" in schema_verification_text, \
    'the schema verification script must expect dbo.Expenses and dbo.ExpensePayments'
enable_expenses_sql_path = root / 'sql/20_EnableExpenses.sql'
assert enable_expenses_sql_path.is_file(), 'missing sql/20_EnableExpenses.sql'
enable_expenses_sql = enable_expenses_sql_path.read_text(encoding='utf-8')
assert 'CREATE TABLE dbo.Expenses' in enable_expenses_sql
assert "IF OBJECT_ID(N'dbo.Expenses', N'U') IS NULL" in enable_expenses_sql
assert 'CREATE TABLE dbo.ExpensePayments' in enable_expenses_sql
assert 'GRANT SELECT, INSERT ON OBJECT::dbo.Expenses TO [ProjectZClient]' in enable_expenses_sql
assert 'GRANT SELECT, INSERT ON OBJECT::dbo.ExpensePayments TO [ProjectZClient]' in enable_expenses_sql
assert 'GRANT SELECT, UPDATE ON OBJECT::dbo.FinancialAccounts TO [ProjectZClient]' in enable_expenses_sql
assert 'GRANT INSERT ON OBJECT::dbo.AuditLog TO [ProjectZClient]' in enable_expenses_sql
assert 'THROW 50070' in enable_expenses_sql and 'THROW 50074' in enable_expenses_sql
assert 'IX_Expenses_Date' in enable_expenses_sql and 'IX_ExpensePayments_Account' in enable_expenses_sql

# Split-payment upgrade for databases that already ran the first version of sql/20.
enable_split_sql_path = root / 'sql/21_EnableExpenseSplitPayments.sql'
assert enable_split_sql_path.is_file(), 'missing sql/21_EnableExpenseSplitPayments.sql'
enable_split_sql = enable_split_sql_path.read_text(encoding='utf-8')
assert 'CREATE TABLE dbo.ExpensePayments' in enable_split_sql
assert "IF COL_LENGTH(N'dbo.Expenses', N'FinancialAccountId') IS NOT NULL" in enable_split_sql
assert 'INSERT dbo.ExpensePayments(ExpenseId, FinancialAccountId, Amount)' in enable_split_sql, \
    'existing single-account expenses must be migrated before the column is dropped'
assert 'ALTER TABLE dbo.Expenses DROP COLUMN FinancialAccountId;' in enable_split_sql
assert 'DROP INDEX IX_Expenses_Date ON dbo.Expenses;' in enable_split_sql
assert 'GRANT SELECT, INSERT ON OBJECT::dbo.ExpensePayments TO [ProjectZClient]' in enable_split_sql
assert 'THROW 50083' in enable_split_sql
# EXEC() accepts variables and literals only; a function call inside EXEC(...) is a parse error (Msg 102).
assert 'EXEC sp_executesql @drop_fk;' in enable_split_sql
assert "DECLARE @drop_fk NVARCHAR(400) = N''ALTER TABLE dbo.Expenses DROP CONSTRAINT '' + QUOTENAME(@fk);" in enable_split_sql, \
    'the FK drop must be built in a variable and run with sp_executesql'
assert "EXEC(N'ALTER TABLE dbo.Expenses DROP CONSTRAINT '" not in enable_split_sql
assert 'THROW 50084' in enable_split_sql, 'every expense must keep a payment row after the migration'
# The migration mentions dbo.Expenses.FinancialAccountId, which does not exist on a fresh database;
# a static batch would fail to compile there ("Invalid column name") before its IF could skip it,
# so the copy + drop live in dynamic SQL and only the guard stays at batch level.
assert "DECLARE @migrate NVARCHAR(MAX) = N'" in enable_split_sql, \
    'the legacy migration must run as dynamic SQL to stay safe on a fresh schema'
assert 'EXEC sp_executesql @migrate;' in enable_split_sql
assert enable_split_sql.find('e.FinancialAccountId') > enable_split_sql.find('DECLARE @migrate'), \
    'the legacy column may only be referenced inside the dynamic migration'

# A failed verification must never be followed by a success message in a later batch.
verify_then_success = [
    (enable_expenses_sql, 'SUCCESS: ProjectZClient can now record split-paid'),
    (enable_split_sql, 'SUCCESS: expenses now support split payments'),
    ((root / 'sql/19_EnableExpenseCategories.sql').read_text(encoding='utf-8'),
     'SUCCESS: ProjectZClient can now load, add, rename'),
]
for script_text, success in verify_then_success:
    assert f"ELSE\n    PRINT N'{success}" in script_text, \
        'a verification failure must print the error only, never SUCCESS'

# The disposable LocalDB harness runs the expense scripts and the split-payment repository,
# so a broken batch or grant fails CI before it reaches a customer machine.
purchase_sql_test = (root / 'tests/ProjectZ.Purchase.Sql.Tests/Program.cs').read_text(encoding='utf-8')
for token in ('19_EnableExpenseCategories.sql', '20_EnableExpenses.sql', '21_EnableExpenseSplitPayments.sql',
              'ExpenseRepository', 'ExpensePaymentRequest', 'dbo.ExpensePayments'):
    assert token in purchase_sql_test, f'the LocalDB harness does not cover the expense scripts: {token}'
purchase_sql_project = (root / 'tests/ProjectZ.Purchase.Sql.Tests/ProjectZ.Purchase.Sql.Tests.csproj'
                        ).read_text(encoding='utf-8')
for linked in ('Data/ExpenseRepository.cs', 'Data/ExpenseCategoryRepository.cs'):
    assert linked in purchase_sql_project, \
        f'the LocalDB harness project does not compile the linked source: {linked}'

expense_entry_repository_path = root / 'src/ProjectZ/Data/ExpenseRepository.cs'
assert expense_entry_repository_path.is_file(), 'missing src/ProjectZ/Data/ExpenseRepository.cs'
expense_entry_repository = expense_entry_repository_path.read_text(encoding='utf-8')
for operation in ('LoadCategories(', 'SavedExpense Save(', 'NextDocumentNumber('):
    assert operation in expense_entry_repository, f'missing expense SQL operation: {operation}'
for guard in ('BeginTransaction(IsolationLevel.Serializable)', 'transaction.Commit()', 'transaction.Rollback()',
              'UPDLOCK, HOLDLOCK', 'dbo.Expenses', 'dbo.ExpensePayments', 'ExpensePaymentRequest',
              'UPDATE dbo.FinancialAccounts SET CurrentBalance = CurrentBalance - @Amount',
              'INSERT dbo.FinancialTransactions', "N'Expense'", 'INSERT dbo.AuditLog', "N'CreateExpense'",
              'private const string ExpenseSequence = "Expense";', '"EXP"'):
    assert guard in expense_entry_repository, f'missing expense persistence guard: {guard}'
assert 'sql/20_EnableExpenses.sql' in expense_entry_repository and 'sql/21_EnableExpenseSplitPayments.sql' in expense_entry_repository, \
    'a missing table must point at sql/20 and sql/21'
assert 'payment.Amount > balance' in expense_entry_repository, 'each payment line must fit its account balance'
assert 'NormalizePayments(' in expense_entry_repository and 'IReadOnlyList<ExpensePaymentRequest>' in expense_entry_repository
assert 'orderby' in expense_entry_repository.lower(), 'accounts must be locked in a deterministic order'
assert 'لا يساوي مبلغ المصروف' in expense_entry_repository, 'the split must add up to the expense amount'
assert 'date > DateTime.Today' in expense_entry_repository or 'expenseDate > DateTime.Today' in expense_entry_repository, \
    'a future expense date must be refused'
assert 'SqlDbType.Decimal' in expense_entry_repository and 'Precision = 19' in expense_entry_repository
assert 'System.Data' in expense_entry_repository and 'Microsoft.Data.SqlClient' in expense_entry_repository

expense_entry_vm = (root / 'src/ProjectZ/ViewModels/AddExpenseViewModel.cs').read_text(encoding='utf-8')
for wiring in ('repository.LoadCategories(', 'repository.Save(new SaveExpenseRequest(',
               'financialRepository.LoadAccountsInto(', 'ExpenseDesignSeedData.CreateSampleExpenseCategories()',
               'FinanceDesignSeedData.CreateAccounts()', 'RefreshFromDatabase()', 'ExpenseSaved', 'IsDatabaseBacked'):
    assert wiring in expense_entry_vm, f'add-expense screen is not wired to SQL: {wiring}'
for token in ('HasInsufficientBalance', 'InsufficientBalanceText', 'SelectedCategory',
              'AmountText', 'DescriptionText', 'ExpenseDate', 'SaveCommand', 'ClearFormCommand', 'HasNoCategories',
              'HasNoAccounts', 'LastSavedNumber', 'IsErrorStatus', 'ExpensePaymentLineViewModel',
              'ObservableCollection<ExpensePaymentLineViewModel> Payments', 'AddPaymentCommand', 'RemovePayment(',
              'HasAllocationGap', 'IsAllocationBalanced', 'AllocationMessage', 'PaymentsAllocated',
              'PaymentMethodsSummary', 'SinglePaymentHint', 'MaxPayments'):
    assert token in expense_entry_vm, f'add-expense view model is missing: {token}'
assert 'SyncSinglePayment();' in expense_entry_vm, 'a single payment line must carry the whole amount'
assert 'PosFormatting.FormatCurrency' in expense_entry_vm
assert 'FinancialAccountDesignItem' in expense_entry_vm

expense_entry_view = (root / 'src/ProjectZ/Views/AddExpenseView.xaml').read_text(encoding='utf-8')
for token in ('ItemsSource="{Binding Categories}"', 'SelectedItem="{Binding SelectedCategory, Mode=TwoWay}"',
              'ItemsSource="{Binding Payments}"', 'ItemsSource="{Binding Accounts}"',
              'SelectedItem="{Binding SelectedAccount, Mode=TwoWay}"',
              'Text="{Binding AmountText', 'Text="{Binding DescriptionText', 'SelectedDate="{Binding ExpenseDate',
              'Command="{Binding SaveCommand}"', 'Command="{Binding AddPaymentCommand}"',
              'Command="{Binding ClearFormCommand}"', 'Command="{Binding RefreshCommand}"',
              'StatusMessage', 'IsErrorStatus', 'HasInsufficientBalance', 'HasLastSaved', 'PosComboBoxStyle',
              'IsAllocationBalanced', 'HasAllocationGap', 'PaymentMethodsSummary', 'RemoveCommand'):
    assert token in expense_entry_view, f'add-expense UI is missing expected token: {token}'
assert 'وسائل الدفع' in expense_entry_view and 'إضافة وسيلة دفع' in expense_entry_view, \
    'the payment methods block must sit in the expense data card'
assert expense_entry_view.index('وسائل الدفع') < expense_entry_view.index('تصنيف المصروف'), \
    'payment methods render beside the expense category'
assert expense_entry_view.count('{DynamicResource Theme.') >= 20, 'every colour must follow the active theme'
assert '{StaticResource PosRtlTextStyle}' in expense_entry_view and 'FlowDirection="LeftToRight"' in expense_entry_view
assert 'متصل بقاعدة البيانات' in expense_entry_view and 'وضع العرض التجريبي' in expense_entry_view
assert (root / 'src/ProjectZ/Views/AddExpenseView.xaml.cs').is_file(), 'missing AddExpenseView code-behind'
assert 'InitializeComponent();' in (root / 'src/ProjectZ/Views/AddExpenseView.xaml.cs').read_text(encoding='utf-8')

assert 'AddExpense = new AddExpenseViewModel(expenseRepository, financialRepository);' in main_viewmodel
assert 'public AddExpenseViewModel AddExpense { get; }' in main_viewmodel
assert 'IsAddExpenseSelected => ReferenceEquals(selectedItem, expensesCategory.Items[0])' in main_viewmodel
assert '|| IsUsersSelected || IsAddExpenseSelected || IsExpenseInvoicesSelected || IsExpenseCategoriesSelected' in main_viewmodel
assert 'Changed(nameof(IsAddExpenseSelected));' in main_viewmodel
assert 'ReferenceEquals(item, expensesCategory.Items[0])' in main_viewmodel, \
    'opening the add-expense screen must reload the sections and the balances'
assert 'AddExpense.ExpenseSaved = () =>' in main_viewmodel and 'MyAccounts.NotifyExternalDataChanged();' in main_viewmodel, \
    'a saved expense must refresh the accounts screen'
assert 'ExpenseInvoices.NotifyExternalDataChanged();' in main_viewmodel, \
    'a saved expense must append its row to the register immediately'
assert 'ExpenseCategories.CategoriesChanged = () =>' in main_viewmodel \
    and 'AddExpense.NotifyExternalDataChanged();' in main_viewmodel, \
    'the sections screen must refresh the add-expense pickers'
assert 'IsAddExpenseSelected' in main_xaml and 'views:AddExpenseView' in main_xaml
assert 'DataContext="{Binding AddExpense}"' in main_xaml
app_cs_text = (root / 'src/ProjectZ/App.xaml.cs').read_text(encoding='utf-8')
assert 'new ExpenseRepository(connectionSettings, user.Id)' in app_cs_text
expenses_doc_path = root / 'docs/EXPENSES.md'
assert expenses_doc_path.is_file(), 'missing docs/EXPENSES.md'
expenses_doc = expenses_doc_path.read_text(encoding='utf-8')
for token in ('dbo.Expenses', 'sql/20_EnableExpenses.sql', 'EXP-YYYY-0000', 'FinancialTransactions',
              'HasInsufficientBalance', 'خطوات القبول'):
    assert token in expenses_doc, f'docs/EXPENSES.md is missing: {token}'
assert 'docs/EXPENSES.md' in (root / 'README.md').read_text(encoding='utf-8'), \
    'README must link the add-expense documentation'
assert '| 40 |' in (root / 'docs/ACCEPTANCE.md').read_text(encoding='utf-8'), \
    'ACCEPTANCE must gain a row for the add-expense screen'
print('PASS: add-expense screen splits the amount across accounts with per-account ledger rows, document number and audit row')

# --- Expense invoices screen (read-only register over dbo.Expenses + dbo.ExpensePayments) -----
for query in ('public List<ExpenseInvoiceRecord> LoadInvoices(DateTime? fromDate = null, DateTime? toDate = null)',
              'FROM dbo.Expenses AS e', 'JOIN dbo.ExpenseCategories AS c ON c.Id = e.ExpenseCategoryId',
              'JOIN dbo.Users AS u ON u.Id = e.ActorUserId',
              'LEFT JOIN dbo.ExpensePayments AS p ON p.ExpenseId = e.Id',
              'LEFT JOIN dbo.FinancialAccounts AS a ON a.Id = p.FinancialAccountId',
              'ORDER BY e.ExpenseDate DESC, e.Id DESC, p.Id',
              '(@FromDate IS NULL OR e.ExpenseDate >= @FromDate)'):
    assert query in expense_entry_repository, f'the expense register query is missing: {query}'
assert 'Payments = item.Payments' in expense_entry_repository, \
    'the register must return every payment line with its expense'
expense_invoices_vm_path = root / 'src/ProjectZ/ViewModels/ExpenseInvoicesViewModel.cs'
assert expense_invoices_vm_path.is_file(), 'missing src/ProjectZ/ViewModels/ExpenseInvoicesViewModel.cs'
expense_invoices_vm = expense_invoices_vm_path.read_text(encoding='utf-8')
for token in ('repository.LoadInvoices()', 'ExpenseDesignSeedData.CreateSampleExpenseInvoices()',
              'SelectDatePresetCommand', 'ClearFiltersCommand', 'OpenDetailsCommand', 'CreateCsvLines',
              'OpenDetailsDialogRequested', 'OpenAddExpenseRequested', 'SelectedAccountFilter',
              'SelectedPaymentFilter', 'SelectedUserFilter', 'SelectedSort', 'MinimumAmountText',
              'MaximumAmountText', 'ShownTotalDisplay', 'ShownAverageDisplay', 'SplitCountDisplay',
              'TopCategoryDisplay', 'EmptyStateText', 'NotifyExternalDataChanged'):
    assert token in expense_invoices_vm, f'expense invoices view model is missing: {token}'
for forbidden in ('INSERT INTO', 'UPDATE dbo.', 'DELETE FROM', 'new SqlConnection', 'SaveExpenseRequest'):
    assert forbidden not in expense_invoices_vm, \
        f'the expense register is a review screen and must not write: {forbidden}'
expense_invoices_view_path = root / 'src/ProjectZ/Views/ExpenseInvoicesView.xaml'
assert expense_invoices_view_path.is_file(), 'missing src/ProjectZ/Views/ExpenseInvoicesView.xaml'
expense_invoices_view = expense_invoices_view_path.read_text(encoding='utf-8')
for token in ('FilteredInvoices', 'ShownTotalDisplay', 'ShownAverageDisplay', 'SplitCountDisplay',
              'TopCategoryDisplay', 'PaymentBreakdown', 'OpenDetailsCommand', 'ClearFiltersCommand',
              'SelectedCategoryFilter', 'SelectedAccountFilter', 'SelectedUserFilter',
              'SelectedPaymentFilter', 'EmptyStateText'):
    assert token in expense_invoices_view, f'expense invoices UI is missing expected token: {token}'
assert expense_invoices_view.count('{DynamicResource Theme.') >= 25, \
    'every colour must follow the active theme'
assert 'FlowDirection="LeftToRight"' in expense_invoices_view
expense_invoices_code_behind = (root / 'src/ProjectZ/Views/ExpenseInvoicesView.xaml.cs').read_text(encoding='utf-8')
for token in ('SaveFileDialog', 'PrintDialog', 'new UTF8Encoding(true)', 'ExpenseInvoiceDetailsWindow',
              'OpenDetailsDialogRequested'):
    assert token in expense_invoices_code_behind, f'expense invoices code-behind is missing: {token}'
details_window_path = root / 'src/ProjectZ/Views/ExpenseInvoiceDetailsWindow.xaml'
assert details_window_path.is_file(), 'missing src/ProjectZ/Views/ExpenseInvoiceDetailsWindow.xaml'
assert 'Payments' in details_window_path.read_text(encoding='utf-8')
assert (root / 'src/ProjectZ/Views/ExpenseInvoiceDetailsWindow.xaml.cs').is_file()
assert 'ExpenseInvoices = new ExpenseInvoicesViewModel(expenseRepository);' in main_viewmodel
assert 'IsExpenseInvoicesSelected => ReferenceEquals(selectedItem, expensesCategory.Items[1])' in main_viewmodel
assert 'ReferenceEquals(item, expensesCategory.Items[1])' in main_viewmodel, \
    'opening «فواتير المصروفات» must reload the register from SQL'
assert 'DataContext="{Binding ExpenseInvoices}"' in main_xaml
assert 'Changed(nameof(IsExpenseInvoicesSelected));' in main_viewmodel
wpf_smoke_test = (root / 'tests/ProjectZ.Appearance.Wpf.Tests/Program.cs').read_text(encoding='utf-8')
for token in ('ExpenseInvoicesViewModel', 'ExpenseInvoicesView', 'IsExpenseInvoicesSelected',
              'OpenDetailsDialogRequested', 'The expense-invoices screen starts with the five sample expenses'):
    assert token in wpf_smoke_test, f'the WPF smoke test does not cover the register: {token}'
for token in ('ExpenseRepository.LoadInvoices returns the saved expense once',
              'The register date filter is applied inside SQL Server',
              'The register lists a migrated legacy expense with its copied account share'):
    assert token in purchase_sql_test, f'the LocalDB harness does not cover the register: {token}'
expense_invoices_doc_path = root / 'docs/EXPENSE_INVOICES.md'
assert expense_invoices_doc_path.is_file(), 'missing docs/EXPENSE_INVOICES.md'
expense_invoices_doc = expense_invoices_doc_path.read_text(encoding='utf-8')
for token in ('dbo.Expenses', 'dbo.ExpensePayments', 'LoadInvoices', 'EXP-YYYY-0000', 'CSV',
              'خطوات القبول'):
    assert token in expense_invoices_doc, f'docs/EXPENSE_INVOICES.md is missing: {token}'
assert 'docs/EXPENSE_INVOICES.md' in (root / 'README.md').read_text(encoding='utf-8'), \
    'README must link the expense invoices documentation'
assert '| 41 |' in (root / 'docs/ACCEPTANCE.md').read_text(encoding='utf-8'), \
    'ACCEPTANCE must gain a row for the expense invoices screen'
print('PASS: expense invoices screen reads dbo.Expenses + dbo.ExpensePayments with full filters, totals, CSV, print and details')

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
# --- General settings screen: store identity is the program name (SystemSettings + sql/17) ---------
settings_repository_path = root / 'src/ProjectZ/Data/SettingsRepository.cs'
store_profile_path = root / 'src/ProjectZ/Models/StoreProfile.cs'
store_identity_path = root / 'src/ProjectZ/Services/StoreIdentity.cs'
settings_view_path = root / 'src/ProjectZ/Views/GeneralSettingsView.xaml'
settings_vm_path = root / 'src/ProjectZ/ViewModels/GeneralSettingsViewModel.cs'
enable_settings_sql_path = root / 'sql/17_EnableGeneralSettings.sql'
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
assert 'CREATE TABLE' not in enable_settings_sql, 'sql/17 must not create tables'
for seeded in ('StoreName', 'StoreTagline', 'StoreAddress', 'StorePhone', 'StorePhoneAlt', 'StoreEmail',
               'ServiceCenterName', 'PrintFooterNote', 'DefaultLowStockAlert', 'CurrencyNameAr', 'CurrencyCode'):
    assert f"IF NOT EXISTS (SELECT 1 FROM dbo.SystemSettings WHERE SettingKey = N'{seeded}')" in enable_settings_sql, \
        f'sql/17 must seed the missing {seeded} key without overwriting a value'

# One live identity object drives every surface.
store_identity = store_identity_path.read_text(encoding='utf-8')
for member in ('INotifyPropertyChanged', 'public static StoreIdentity Current', 'public static void Apply(',
               'PropertyChanged?.Invoke(instance, new PropertyChangedEventArgs(null))', 'public string WindowTitle',
               'public string Headline', 'public string ContactLine', 'public string ServiceCenterHeader'):
    assert member in store_identity, f'store identity missing: {member}'
assert 'PosFormatting.CurrencyLabel = saved.CurrencyLabel;' in store_identity, \
    'amount formatting must follow the configured currency label'
disconnect_body = store_identity.split('public static void Disconnect(', 1)[-1]
assert 'Disconnected?.Invoke' not in disconnect_body, \
    'Disconnect must not re-raise the close event from inside its own handler (recursion)'
main_vm_text = (root / 'src/ProjectZ/ViewModels/MainViewModel.cs').read_text(encoding='utf-8')
assert 'Current.Disconnected -= OnStoreIdentityDisconnected;' in main_vm_text, \
    'the main window must detach its close handler from the store identity'
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
    receipt_without_types = re.sub(r'x:Class="[^"]*"', '', receipt_text)
    receipt_without_types = receipt_without_types.replace('pack://application:,,,/ProjectZ;component', '')
    assert 'ProjectZ' not in receipt_without_types, f'{receipt}: hard-coded brand text remains'

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

# General settings and backup used to share one Grid with two separate visibility triggers. Both
# views were visible at once, so one screen drew on top of the other. Each host must be independent.
general_trigger = main_xaml_text.find('IsGeneralSettingsSelected')
general_view = main_xaml_text.find('GeneralSettingsView', general_trigger)
backup_trigger = main_xaml_text.find('IsBackupSelected')
backup_view = main_xaml_text.find('BackupView', backup_trigger)
assert general_trigger >= 0 and general_view > general_trigger, 'general settings needs its own host Grid'
assert backup_trigger >= 0 and backup_view > backup_trigger, 'backup needs its own host Grid'
assert general_view < backup_trigger, 'backup host must start after the general-settings host'
assert 'BackupView' not in main_xaml_text[general_trigger:general_view], \
    'backup must not be hosted inside the general-settings Grid'
assert 'GeneralSettingsView' not in main_xaml_text[backup_trigger:backup_view], \
    'general settings must not be hosted inside the backup Grid'

# Backup screen: server-side paths, a master connection for restore, fresh file names and a busy guard.
backup_repository = (root / 'src/ProjectZ/Data/BackupRepository.cs').read_text(encoding='utf-8')
backup_vm = (root / 'src/ProjectZ/ViewModels/BackupViewModel.cs').read_text(encoding='utf-8')
backup_xaml = (root / 'src/ProjectZ/Views/BackupView.xaml').read_text(encoding='utf-8')
for guard in ('InitialCatalog = "master"', 'LastBackupAt', 'SET MULTI_USER', '3201',
              'QuoteIdentifier', 'builder.InitialCatalog'):
    assert guard in backup_repository, f'backup repository missing guard: {guard}'
assert 'private const string DatabaseName' not in backup_repository, \
    'backup must follow the configured Database, not a hard-coded catalog name'
for guard in ('CreateDefaultBackupFileName', 'NewBackupFileNameCommand', 'IsBusy', 'EmptyBackupsMessage'):
    assert guard in backup_vm, f'backup view-model missing guard: {guard}'
assert 'File.Exists(RestoreFilePath)' not in backup_vm, 'restore path must be validated by SQL Server, not this client'
for token in ('BackupFolderPath', 'BackupFileName', 'RestoreFilePath', 'AvailableBackups',
              'IsIdle', 'NewBackupFileNameCommand', 'BackupCommand', 'RestoreCommand'):
    assert token in backup_xaml, f'backup UI missing expected token: {token}'

# Restore privileges belong exclusively to the pre-provisioned backup administrator, never the till.
backup_grant = (root / 'sql/18_EnableBackupRestore.sql').read_text(encoding='utf-8')
for role in ('dbcreator', 'db_owner'):
    assert f'[{role}] ADD MEMBER [ProjectZBackupAdmin]' in backup_grant
assert not re.search(r'ADD\s+MEMBER\s+\[?ProjectZClient\b', backup_grant, re.I)
assert 'docs/BACKUP.md' in backup_repository
assert 'SqlConnection.ClearAllPools()' in backup_repository, \
    'a permission error should discard the pooled token before retry'
assert 'sql/18_EnableBackupRestore.sql' in (root / 'docs/BACKUP.md').read_text(encoding='utf-8'), \
    'backup docs must point at the permission script'

# The add-product form starts from the configured default low-stock alert.
assert 'StoreIdentity.Current.DefaultLowStockAlert' in add_product_vm_text
print('PASS: general settings save the store identity in dbo.SystemSettings and rename the program everywhere')
print('PASS: general settings and backup have separate hosts, and backup uses master-side RESTORE with fresh file names')
# Wallet transfers: the ledger row must name its source transfer id, because dbo.FinancialTransactions
# carries CK_FinancialTransactions_Source ((type IS NULL AND id IS NULL) OR (type and id both set)) and
# CK_FinancialTransactions_Amount (Amount > 0). Writing N'CustomerTransfer' with a NULL id made every
# Vodafone Cash / InstaPay deposit, withdrawal, edit and reversal fail with SQL 547, surfaced to the user as
# "لا يمكن إتمام العملية لارتباط السجل بحساب أو حركة أخرى".
financial_repository = (root / 'src/ProjectZ/Data/FinancialRepository.cs').read_text(encoding='utf-8')
for token in ("N'CustomerTransfer',@Id", 'WHERE Delta<>0', 'FROM dbo.CustomerTransfers WITH (UPDLOCK,HOLDLOCK)',
              '@Version<>@ExpectedVersion', '@OldStatus<>0', 'CorrectsTransferId', 'INSERT dbo.AuditLog',
              'DENY INSERT, UPDATE, DELETE ON OBJECT::dbo.CustomerTransfers',
              'DENY EXECUTE ON OBJECT::dbo.WriteCustomerTransferEffect', 'GRANT EXECUTE ON OBJECT::dbo.PostCustomerTransfer'):
    assert token in wallet_posting, f'missing customer transfer posting invariant: {token}'
assert 'UPDATE dbo.CustomerTransfers' not in financial_repository and 'INSERT dbo.CustomerTransfers' not in financial_repository
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
# A C# string literal must open and close on one line (a normal or interpolated string cannot span lines).
# Two separate merges shipped a build break of exactly this shape, so guard it instead of trusting review.
def unbalanced_string_lines(source: str) -> list[int]:
    offending: list[int] = []
    in_verbatim = False
    for number, line in enumerate(source.splitlines(), 1):
        text = line if in_verbatim else re.sub(r'//.*', '', line)
        stripped = re.sub(r'\\.', '', text)  # \" does not close the literal
        if in_verbatim:
            if stripped.count('"') % 2 == 1:
                in_verbatim = False
            continue
        if re.search(r'@\$?"|\$@"', stripped):
            if stripped.count('"') % 2 == 1:
                in_verbatim = True  # @"..." may legitimately continue on the next line
            continue
        if stripped.count('"') % 2 == 1:
            offending.append(number)
    return offending

string_breaks = [
    f'{path.relative_to(root)}:{line}'
    for path in sorted((root / 'src').rglob('*.cs'))
    for line in unbalanced_string_lines(path.read_text(encoding='utf-8'))
]
assert not string_breaks, 'unterminated C# string literal (build breaker): ' + ', '.join(string_breaks)
print('PASS: no C# string literal is left open across lines anywhere in src/ProjectZ')

# Appearance studio: device-local persistence, live resources, custom colors and print isolation.
appearance_model = (root / 'src/ProjectZ/Models/AppearanceModels.cs').read_text(encoding='utf-8')
for theme_id in ('glassmorphism', 'liquid-glass', 'dark-glass', 'aurora', 'holographic', 'fui', 'chrome', 'fluent', 'custom'):
    assert f'"{theme_id}"' in appearance_model, f'missing theme preset: {theme_id}'
appearance_store = (root / 'src/ProjectZ/Services/AppearanceStore.cs').read_text(encoding='utf-8')
for guard in ('LocalApplicationData', 'appearance.json', 'File.Replace', 'Flush(flushToDisk: true)', 'ThemeCatalog.Validate', '.bak'):
    assert guard in appearance_store, f'appearance persistence missing guard: {guard}'
assert 'SqlConnection' not in appearance_store and 'SystemSettings' not in appearance_store, 'appearance must stay independent of SQL'
assert 'themeManager.Initialize();' in app_cs
assert app_cs.index('themeManager.Initialize();') < app_cs.index('ShowLogin();'), 'saved theme must load before the login window'
assert 'NativeTitleBar.Apply' in app_cs and 'EventManager.RegisterClassHandler(typeof(Window)' in app_cs
assert (root / 'src/ProjectZ/Services/NativeTitleBar.cs').is_file()
assert 'new AppearanceViewModel(themeManager)' in app_cs
assert 'IsAppearanceSelected' in main_viewmodel_text and 'Appearance = appearance ?? new AppearanceViewModel();' in main_viewmodel_text
assert 'IsAppearanceSelected' in main_xaml_text and 'AppearanceView DataContext="{Binding Appearance}"' in main_xaml_text
# Regression: every screen-visibility flag must be notified by SelectItem. A missing notification
# leaves the host Grid Collapsed while the placeholder hides, so the screen renders empty/white
# (this shipped once for IsAppearanceSelected).
main_vm_class = main_viewmodel_text.split('public sealed class NavigationMenuCategoryViewModel', 1)[0]
select_item_body = main_vm_class.split('public void SelectItem(', 1)[1]
for screen_flag in re.findall(r'public bool (Is\w+Selected)\s*=>', main_vm_class):
    assert f'Changed(nameof({screen_flag}))' in select_item_body, \
        f'SelectItem must notify {screen_flag}, otherwise its screen never becomes visible'
print('PASS: SelectItem notifies every MainViewModel screen flag (appearance white-screen regression guard)')
appearance_view = (root / 'src/ProjectZ/Views/AppearanceView.xaml').read_text(encoding='utf-8')
for token in ('SelectedTheme', 'SaveCommand', 'RestoreSavedCommand', 'ResetCustomCommand', 'OpacityPercent', 'CornerRadius', 'ContrastChecks', 'PickColor_Click'):
    assert token in appearance_view, f'appearance studio missing wiring: {token}'
theme_manager = (root / 'src/ProjectZ/Services/ThemeManager.cs').read_text(encoding='utf-8')
assert 'EnsureTextContrast' in theme_manager and 'ClientAreaAnimation' in theme_manager
assert 'resources[key] = resources["Theme.Brush." + role]' in theme_manager, 'legacy aliases must be replaced with the live role brush'
assert ('["PosInkBrush"] = "Text"' in theme_manager
        and 'resources["Accent"] = resources["Theme.Brush.Accent"]' in theme_manager), \
    'POS brush keys and the legacy Accent key must follow the live theme'
appearance_vm_text = (root / 'src/ProjectZ/ViewModels/AppearanceViewModel.cs').read_text(encoding='utf-8')
for role in ('HeadingText', 'SidebarText', 'SidebarMutedText', 'ButtonText'):
    assert role in appearance_model and role in appearance_vm_text, \
        f'custom studio must expose independent text role: {role}'
assert 'Schema v1' in appearance_store and 'CurrentVersion = 2' in appearance_model, \
    'v1 appearance files must migrate to independent text roles'
assert 'Theme.Brush.NavigationInk' in main_xaml_text and 'Theme.Brush.NavigationMuted' in main_xaml_text, \
    'sidebar menu text must use the independent sidebar text roles'
assert 'effective Foreground' in (root / 'tests/ProjectZ.Appearance.Wpf.Tests/TextColorTests.cs').read_text(encoding='utf-8'), \
    'WPF regression must assert effective text colors, not only resource values'
print_isolation = (root / 'src/ProjectZ/Resources/PrintIsolation.xaml').read_text(encoding='utf-8')
assert 'DynamicResource Theme.' not in print_isolation, 'print isolation must not follow the live theme'
for print_name in ('InvoicePrintPreviewWindow.xaml', 'PurchaseInvoicePrintPreviewWindow.xaml', 'MaintenanceReceiptPreviewWindow.xaml', 'BarcodePrintPreviewWindow.xaml'):
    print_text = (root / 'src/ProjectZ/Views' / print_name).read_text(encoding='utf-8')
    assert '/Resources/PrintIsolation.xaml' in print_text, f'{print_name} must isolate print colors'
    assert 'ThemeBackdropStyle' not in print_text and 'DynamicResource Theme.' not in print_text, f'{print_name} must not be themed'
for xaml_path in (root / 'src/ProjectZ').rglob('*.xaml'):
    if any(part in xaml_path.name for part in ('Print', 'Receipt', 'ThemePalette', 'ColorPicker')):
        continue
    assert not re.search(r'#[0-9A-Fa-f]{6,8}\b', xaml_path.read_text(encoding='utf-8')), f'unthemed color literal remains in {xaml_path.name}'
assert (root / 'tests/ProjectZ.Appearance.Tests/Program.cs').is_file()
assert (root / 'tests/ProjectZ.Appearance.Wpf.Tests/Program.cs').is_file()
wpf_smoke = (root / 'tests/ProjectZ.Appearance.Wpf.Tests/Program.cs').read_text(encoding='utf-8')
assert 'mainVm.SelectItem(appearanceItem);' in wpf_smoke and 'IsAppearanceSelected' in wpf_smoke, \
    'WPF smoke test must navigate to the appearance screen and verify its host becomes visible'
workflow_text = (root / '.github/workflows/windows-build.yml').read_text(encoding='utf-8')
assert 'ProjectZ.Appearance.Tests' in workflow_text and 'ProjectZ.Appearance.Wpf.Tests' in workflow_text
assert 'docs/THEMES.md' in (root / 'README.md').read_text(encoding='utf-8')
print('PASS: appearance studio has eight presets plus twelve-role custom colors, independent text groups, atomic device-local save, live whole-app resources and isolated print colors')
# --- wallet records screens list the whole wallet ledger, accounts split, one save button, header clock ---
# Regression set: (1)/(2) «سجلات فودافون كاش» and «سجلات إنستاباي» read dbo.CustomerTransfers only, so every
# sale, purchase, deferred settlement, return, maintenance payment, expense and wallet transfer was missing;
# (3) «حساباتي» mixed both providers in one list; (4) «حفظ وإضافة صنف آخر» did exactly what «حفظ وتسجيل الصنف»
# did; (5) the title bar had no Egypt date/time next to «مرحبًا» and «تسجيل خروج».
ledger_repo = (root / 'src/ProjectZ/Data/FinancialRepository.cs').read_text(encoding='utf-8')
for token in ('public void LoadWalletMovementsInto(',
              'public void LoadVodafoneWalletMovementsInto(',
              'public void LoadInstapayWalletMovementsInto(',
              'FROM dbo.FinancialTransactions t',
              "LEFT JOIN dbo.CustomerTransfers ct",
              'ct.FinancialAccountId=t.FinancialAccountId', 'SELECT MAX(ap.Id)',
              "ap.Category IN (N'تحويل للعميل',N'سحب للعميل',N'تعديل سجل')", 'ct.RowVersion, ct.CorrectsTransferId',
              'WHERE a.AccountKind = @Kind',
              "SELECT t.Id, t.FinancialAccountId",
              'ORDER BY t.OccurredAtUtc DESC, t.Id DESC;'):
    assert token in ledger_repo, f'FinancialRepository must load the whole wallet ledger: {token}'
assert 'reader.IsDBNull(14)' in ledger_repo and 'hasTransfer ?' in ledger_repo, \
    'every CustomerTransfers column of the LEFT JOIN must be read through IsDBNull'

for model_name, model_path in (('Vodafone', 'src/ProjectZ/Models/VodafoneTransferDesignModels.cs'),
                               ('InstaPay', 'src/ProjectZ/Models/InstapayTransferDesignModels.cs')):
    model_text = (root / model_path).read_text(encoding='utf-8')
    for token in ('public int MovementId', 'public string SourceTitle', 'public string CategoryTitle',
                  'public bool IsCustomerTransfer', 'public bool IsIncoming',
                  'public bool CanEditOrDelete => IsCustomerTransfer && !IsReversed;'):
        assert token in model_text, f'{model_name} records model must describe ledger movements: {token}'
    assert 'public string OperationTitle => IsCustomerTransfer ?' in model_text, \
        f'{model_name} rows of another screen take the source label, not the deposit/withdraw title'

for vm_path in ('src/ProjectZ/ViewModels/VodafoneRecordsViewModel.cs',
                'src/ProjectZ/ViewModels/InstapayRecordsViewModel.cs'):
    vm_text = (root / vm_path).read_text(encoding='utf-8')
    for token in ('private readonly ObservableCollection<', 'allOperations.Clear();',
                  'allOperations.Where(Matches)',
                  'public const string ReadOnlyMovementMessage =',
                  'if (!SelectedOperation.IsCustomerTransfer) { SetStatus(ReadOnlyMovementMessage, true); return; }',
                  'RebuildKindFilters();'):
        assert token in vm_text, f'{vm_path} must show and protect the whole wallet ledger: {token}'
    assert 'transfers.Operations.Where(Matches)' not in vm_text, \
        f'{vm_path} still filters the customer transfers only instead of the whole wallet history'

instapay_records_vm_ascii = (root / 'src/ProjectZ/ViewModels/InstapayRecordsViewModel.cs').read_text(encoding='utf-8')
assert 'LoadInstapayWalletMovementsInto' in instapay_records_vm_ascii, \
    'the InstaPay records screen must load its own wallet history through the InstaPay loader'

accounts_view_text = (root / 'src/ProjectZ/Views/MyAccountsView.xaml').read_text(encoding='utf-8')
for token in ('x:Key="WalletCardTemplate"', 'ItemsSource="{Binding VodafoneWallets}"',
              'ItemsSource="{Binding InstapayWallets}"', 'Text="{Binding VodafoneWalletsSubtext}"',
              'Text="{Binding InstapayWalletsSubtext}"', 'HasNoVodafoneWallets', 'HasNoInstapayWallets'):
    assert token in accounts_view_text, f'«حساباتي» must show the two wallet sections side by side: {token}'
for token in ('VodafoneWallets', 'InstapayWallets'):
    assert token in (root / 'src/ProjectZ/ViewModels/MyAccountsViewModel.cs').read_text(encoding='utf-8'), \
        f'MyAccountsViewModel must fill the {token} section'

add_product_view_text = (root / 'src/ProjectZ/Views/AddProductView.xaml').read_text(encoding='utf-8')
add_product_vm_text = (root / 'src/ProjectZ/ViewModels/AddProductViewModel.cs').read_text(encoding='utf-8')
assert 'SaveAndKeepCategoryCommand' not in add_product_view_text and 'SaveAndKeepCategoryCommand' not in add_product_vm_text, \
    'the duplicate «حفظ وإضافة صنف آخر» button and command must be gone'
for token in ('SaveProductCommand', 'SaveProduct(keepCategoryAndUnit: true)'):
    assert token in add_product_vm_text, f'one save button must remain on «إضافة منتج»: {token}'

main_window_text = (root / 'src/ProjectZ/Views/MainWindow.xaml').read_text(encoding='utf-8')
main_vm_text = (root / 'src/ProjectZ/ViewModels/MainViewModel.cs').read_text(encoding='utf-8')
for token in ('Text="{Binding HeaderTime}"', 'Text="{Binding HeaderDate}"'):
    assert token in main_window_text, f'the title bar must show Egypt date and time: {token}'
for token in ('public string HeaderDate', 'public string HeaderTime',
              'EgyptTimeZone = ResolveEgyptTimeZone()', 'TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, EgyptTimeZone)',
              'DispatcherTimer', 'egyptClockTimer.Start();'):
    assert token in main_vm_text, f'MainViewModel must run the Egypt clock: {token}'
assert 'DateTime.Now.ToString' not in main_vm_text, \
    'the header clock must convert to Egypt time, not print the machine clock blindly'

records_doc_text = (root / 'docs/DATABASE.md').read_text(encoding='utf-8')
for token in ('LoadWalletMovementsInto', 'ReadOnlyMovementMessage', 'LoadInstapayWalletMovementsInto'):
    assert token in records_doc_text, f'docs/DATABASE.md must explain the whole-ledger records screens: {token}'
assert '| 45 |' in (root / 'docs/ACCEPTANCE.md').read_text(encoding='utf-8'), \
    'ACCEPTANCE must gain the row for the five edits'
print('PASS: records screens list every wallet movement, accounts split by provider, one save button and an Egypt header clock')
print('NOT RUN locally: .NET build, C# tests, WPF execution, SQL Server 2016 SP1, LAN acceptance, EXE publish (Windows CI covers .NET build/tests, publish and isolated LocalDB integration)')
