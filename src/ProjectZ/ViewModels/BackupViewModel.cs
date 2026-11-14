using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using ProjectZ.Data;
using ProjectZ.Helpers;

namespace ProjectZ.ViewModels;

/// <summary>
/// شاشة النسخ الاحتياطي والاسترجاع لقاعدة بيانات ProjectZ.
/// جميع العمليات تُنفذ مباشرة على SQL Server عبر BackupRepository، والواجهة تُعلم بالنتيجة بالعربية.
/// </summary>
public sealed class BackupViewModel : ObservableObject
{
    private readonly BackupRepository? repository;
    private readonly string cashierName;
    private string statusMessage = "";
    private bool isErrorStatus;

    public ObservableCollection<BackupRepository.BackupFileInfo> AvailableBackups { get; } = [];
    public BackupRepository.DatabaseInfo? DatabaseInfo { get; private set; }

    public bool IsDatabaseBacked => repository is not null;
    public string ConnectionBadgeText => IsDatabaseBacked ? "متصل بقاعدة البيانات" : "عرض تجريبي بدون قاعدة بيانات";

    private string backupFolderPath = "";
    public string BackupFolderPath
    {
        get => backupFolderPath;
        set { if (backupFolderPath == value) return; backupFolderPath = value; Changed(); }
    }

    private string backupFileName = $"ProjectZ_{DateTime.Now:yyyyMMdd_HHmmss}.bak";
    public string BackupFileName
    {
        get => backupFileName;
        set { if (backupFileName == value) return; backupFileName = value; Changed(); }
    }

    private string restoreFilePath = "";
    public string RestoreFilePath
    {
        get => restoreFilePath;
        set { if (restoreFilePath == value) return; restoreFilePath = value; Changed(); }
    }

    public bool HasAvailableBackups => AvailableBackups.Count > 0;

    public string StatusMessage
    {
        get => statusMessage;
        private set
        {
            if (statusMessage == value) return;
            statusMessage = value;
            Changed();
            Changed(nameof(HasStatusMessage));
        }
    }

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);
    public bool IsErrorStatus => isErrorStatus;

    public ActionCommand BrowseBackupFolderCommand { get; }
    public ActionCommand BrowseRestoreFileCommand { get; }
    public AsyncCommand BackupCommand { get; }
    public AsyncCommand RestoreCommand { get; }
    public ActionCommand RefreshCommand { get; }

    /// <summary>تُنفذ من العرض لفتح اختيار مجلد الحفظ، وتُعيد المسار المختار عبر الكولباك.</summary>
    public Action<Action<string>>? BrowseBackupFolderRequested { get; set; }
    /// <summary>تُنفذ من العرض لفتح اختيار ملف النسخة للاسترجاع، وتُعيد المسار المختار عبر الكولباك.</summary>
    public Action<Action<string>>? BrowseRestoreFileRequested { get; set; }
    /// <summary>تُنفذ من العرض لفتح نافذة تأكيد قبل الاسترجاع.</summary>
    public Action<ConfirmActionViewModel, Action>? OpenConfirmDialogRequested { get; set; }

    public BackupViewModel(string cashierName = "المشرف", BackupRepository? repository = null)
    {
        this.cashierName = cashierName;
        this.repository = repository;

        BrowseBackupFolderCommand = new ActionCommand(BrowseBackupFolder);
        BrowseRestoreFileCommand = new ActionCommand(BrowseRestoreFile);
        BackupCommand = new AsyncCommand(ExecuteBackupAsync);
        RestoreCommand = new AsyncCommand(ExecuteRestoreAsync);
        RefreshCommand = new ActionCommand(RefreshFromDatabase);

        if (repository is null)
        {
            StatusMessage = "وضع العرض التجريبي: لا يوجد اتصال بقاعدة البيانات، ولن تُنفذ أي نسخة أو استرجاع.";
        }
        else
        {
            RefreshFromDatabase();
        }
    }

    /// <summary>يعيد تحميل معلومات القاعدة من SQL وقائمة ملفات النسخ في المجلد المحدد.</summary>
    public void RefreshFromDatabase()
    {
        if (repository is null) return;

        try
        {
            DatabaseInfo = repository.GetDatabaseInfo();
            Changed(nameof(DatabaseInfo));
            Changed(nameof(HasDatabaseInfo));
        }
        catch (Exception ex)
        {
            SetStatus(DescribeError(ex, "تعذر قراءة معلومات قاعدة البيانات."), true);
            DatabaseInfo = null;
            Changed(nameof(DatabaseInfo));
            Changed(nameof(HasDatabaseInfo));
            return;
        }

        RefreshAvailableBackups();
        SetStatus($"تم تحميل معلومات قاعدة بيانات ProjectZ (الحجم: {DatabaseInfo?.SizeDisplay ?? "غير معروف"}).", false);
    }

    private void RefreshAvailableBackups()
    {
        if (string.IsNullOrWhiteSpace(BackupFolderPath)) return;

        try
        {
            var files = repository is null ? [] : repository.GetAvailableBackups(BackupFolderPath);
            AvailableBackups.Clear();
            foreach (var file in files)
                AvailableBackups.Add(file);
            Changed(nameof(HasAvailableBackups));
        }
        catch { /* تُعرض القائمة فارغة إذا تعذر قراءة المجلد. */ }
    }

    private void BrowseBackupFolder()
    {
        if (repository is null)
        {
            SetStatus("وضع العرض التجريبي: لا يمكن اختيار مجلد بدون قاعدة بيانات.", true);
            return;
        }

        BrowseBackupFolderRequested?.Invoke(selectedFolder =>
        {
            if (!string.IsNullOrWhiteSpace(selectedFolder))
            {
                BackupFolderPath = selectedFolder;
                RefreshAvailableBackups();
                SetStatus($"تم تحديد مجلد النسخ الاحتياطي: {selectedFolder}", false);
            }
        });
    }

    private void BrowseRestoreFile()
    {
        if (repository is null)
        {
            SetStatus("وضع العرض التجريبي: لا يمكن اختيار ملف نسخ بدون قاعدة بيانات.", true);
            return;
        }

        BrowseRestoreFileRequested?.Invoke(selectedFile =>
        {
            if (!string.IsNullOrWhiteSpace(selectedFile))
            {
                RestoreFilePath = selectedFile;
                SetStatus($"تم تحديد ملف النسخة للاسترجاع: {Path.GetFileName(selectedFile)}", false);
            }
        });
    }

    private async Task ExecuteBackupAsync()
    {
        if (repository is null) return;

        var folder = BackupFolderPath.Trim();
        var fileName = BackupFileName.Trim();

        if (string.IsNullOrWhiteSpace(folder) || string.IsNullOrWhiteSpace(fileName))
        {
            SetStatus("يرجى تحديد مجلد الحفظ واسم ملف النسخة الاحتياطية أولاً.", true);
            return;
        }

        if (!Directory.Exists(folder))
        {
            SetStatus("المجلد المحدد غير موجود. اختر مجلدًا صحيحًا.", true);
            return;
        }

        var extension = Path.GetExtension(fileName);
        if (!string.Equals(extension, ".bak", StringComparison.OrdinalIgnoreCase))
        {
            SetStatus("يجب أن ينتهي اسم ملف النسخة الاحتياطية بامتداد .bak.", true);
            return;
        }

        var fullPath = Path.Combine(folder, fileName);

        try
        {
            await repository.BackupDatabaseAsync(fullPath);
        }
        catch (Exception ex)
        {
            SetStatus(DescribeError(ex, "تعذر أخذ النسخة الاحتياطية."), true);
            return;
        }

        RefreshAvailableBackups();
        RefreshFromDatabase();
        SetStatus($"تم حفظ النسخة الاحتياطية بنجاح في:
{fullPath}", false);
    }

    private async Task ExecuteRestoreAsync()
    {
        if (repository is null) return;

        if (string.IsNullOrWhiteSpace(RestoreFilePath))
        {
            SetStatus("يرجى اختيار ملف نسخة احتياطية (.bak) للاسترجاع أولاً.", true);
            return;
        }

        if (!File.Exists(RestoreFilePath))
        {
            SetStatus("ملف النسخة الاحتياطية غير موجود. اختر ملفًا صحيحًا.", true);
            return;
        }

        var fileToRestore = Path.GetFileName(RestoreFilePath);
        var confirm = new ConfirmActionViewModel(
            "تأكيد استرجاع النسخة الاحتياطية",
            fileToRestore,
            "سيؤدي استرجاع هذه النسخة إلى استبدال قاعدة بيانات ProjectZ الحالية ببيانات الملف المحدد، وستفقد أي بيانات أُضيفت بعد تاريخ النسخة. تأكد من إغلاق جميع الشاشات التي تستخدم القاعدة قبل المتابعة.",
            "استرجاع النسخة");

        if (OpenConfirmDialogRequested is null)
        {
            SetStatus("تعذر فتح نافذة التأكيد. أعد فتح الشاشة وحاول مرة أخرى.", true);
            return;
        }

        OpenConfirmDialogRequested(confirm, async () =>
        {
            try
            {
                await repository.RestoreDatabaseAsync(RestoreFilePath);
            }
            catch (Exception ex)
            {
                SetStatus(DescribeError(ex, "تعذر استرجاع النسخة الاحتياطية."), true);
                return;
            }

            RefreshFromDatabase();
            RestoreFilePath = "";
            Changed(nameof(RestoreFilePath));
            SetStatus($"تم استرجاع النسخة الاحتياطية "{fileToRestore}" بنجاح. تمت إعادة تحميل بيانات القاعدة.", false);
        });
    }

    private void SetStatus(string message, bool isError)
    {
        isErrorStatus = isError;
        StatusMessage = message;
        Changed(nameof(IsErrorStatus));
    }

    private static string DescribeError(Exception exception, string fallback)
        => exception is BackupDataException data ? data.Message : fallback;

    public bool HasDatabaseInfo => DatabaseInfo is not null;
}
