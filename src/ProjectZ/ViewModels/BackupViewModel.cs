using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using ProjectZ.Data;
using ProjectZ.Helpers;

namespace ProjectZ.ViewModels;

/// <summary>
/// شاشة النسخ الاحتياطي والاسترجاع لقاعدة البيانات المضبوطة في connectionsettings.json.
/// جميع العمليات تُنفذ مباشرة على SQL Server عبر BackupRepository، والواجهة تُعلم بالنتيجة بالعربية.
/// مسارات الملفات تُقيَّم على جهاز SQL Server، وليس على جهاز العميل.
/// </summary>
public sealed class BackupViewModel : ObservableObject
{
    private readonly BackupRepository? repository;
    private string statusMessage = "";
    private bool isErrorStatus;
    private bool isBusy;

    public ObservableCollection<BackupRepository.BackupFileInfo> AvailableBackups { get; } = [];
    public BackupRepository.DatabaseInfo? DatabaseInfo { get; private set; }

    public bool IsDatabaseBacked => repository is not null;
    public string ConnectionBadgeText => IsDatabaseBacked ? "متصل بقاعدة البيانات" : "عرض تجريبي بدون قاعدة بيانات";

    private string backupFolderPath = "";
    public string BackupFolderPath
    {
        get => backupFolderPath;
        set
        {
            var clean = value ?? "";
            if (backupFolderPath == clean) return;
            backupFolderPath = clean;
            Changed();
            Changed(nameof(HasBackupFolder));
            Changed(nameof(EmptyBackupsMessage));
            // A different folder invalidates the old list; the user refreshes or browses to repopulate it.
            ClearAvailableBackups();
        }
    }

    public bool HasBackupFolder => !string.IsNullOrWhiteSpace(BackupFolderPath);

    private string backupFileName = CreateDefaultBackupFileName();
    public string BackupFileName
    {
        get => backupFileName;
        set
        {
            var clean = value ?? "";
            if (backupFileName == clean) return;
            backupFileName = clean;
            Changed();
        }
    }

    private string restoreFilePath = "";
    public string RestoreFilePath
    {
        get => restoreFilePath;
        set
        {
            var clean = value ?? "";
            if (restoreFilePath == clean) return;
            restoreFilePath = clean;
            Changed();
        }
    }

    public bool HasAvailableBackups => AvailableBackups.Count > 0;

    /// <summary>رسالة واضحة عندما لا تكون القائمة محلية أو لا توجد ملفات في المسار.</summary>
    public string EmptyBackupsMessage => string.IsNullOrWhiteSpace(BackupFolderPath)
        ? "اختر مجلد النسخ أولًا لعرض الملفات الموجودة فيه."
        : "لا توجد ملفات .bak في هذا المسار من جهاز العميل. إذا كان SQL Server على جهاز آخر، اكتب مسار الخادم يدويًا أو استخدم مشاركة UNC.";

    /// <summary>True while a backup/restore command is running; the form is disabled to prevent overlap.</summary>
    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (isBusy == value) return;
            isBusy = value;
            Changed();
            Changed(nameof(IsIdle));
        }
    }

    public bool IsIdle => !IsBusy;

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
    public ActionCommand NewBackupFileNameCommand { get; }
    public AsyncCommand BackupCommand { get; }
    public AsyncCommand RestoreCommand { get; }
    public ActionCommand RefreshCommand { get; }

    /// <summary>تُنفذ من العرض لفتح اختيار مجلد الحفظ، وتُعيد المسار المختار عبر الكولباك.</summary>
    public Action<Action<string>>? BrowseBackupFolderRequested { get; set; }
    /// <summary>تُنفذ من العرض لفتح اختيار ملف النسخة للاسترجاع، وتُعيد المسار المختار عبر الكولباك.</summary>
    public Action<Action<string>>? BrowseRestoreFileRequested { get; set; }
    /// <summary>تُنفذ من العرض لفتح نافذة تأكيد قبل الاسترجاع.</summary>
    public Action<ConfirmActionViewModel, Action>? OpenConfirmDialogRequested { get; set; }
    /// <summary>Raised after a successful restore so the shell can reload data read from the replaced database.</summary>
    public Action? DatabaseRestored { get; set; }

    public BackupViewModel(BackupRepository? repository = null)
    {
        this.repository = repository;

        BrowseBackupFolderCommand = new ActionCommand(BrowseBackupFolder);
        BrowseRestoreFileCommand = new ActionCommand(BrowseRestoreFile);
        NewBackupFileNameCommand = new ActionCommand(() => BackupFileName = CreateDefaultBackupFileName());
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
        var folderNote = string.IsNullOrWhiteSpace(BackupFolderPath)
            ? " اختر مجلد النسخ لعرض الملفات الموجودة."
            : " يمكنك تحديث قائمة الملفات من الزر أعلاه.";
        SetStatus($"تم تحميل معلومات قاعدة البيانات (الحجم: {DatabaseInfo?.SizeDisplay ?? "غير معروف"}).{folderNote}", false);
    }

    private void RefreshAvailableBackups()
    {
        AvailableBackups.Clear();

        if (repository is null || string.IsNullOrWhiteSpace(BackupFolderPath))
        {
            Changed(nameof(HasAvailableBackups));
            return;
        }

        try
        {
            foreach (var file in repository.GetAvailableBackups(BackupFolderPath))
                AvailableBackups.Add(file);
        }
        catch
        {
            // القائمة المحلية قد لا تكون متاحة إذا كان الخادم بعيدًا؛ لا نُخفي بقية الشاشة.
        }

        Changed(nameof(HasAvailableBackups));
    }

    private void ClearAvailableBackups()
    {
        if (AvailableBackups.Count == 0) return;
        AvailableBackups.Clear();
        Changed(nameof(HasAvailableBackups));
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
        if (repository is null)
        {
            SetStatus("وضع العرض التجريبي: لا يمكن أخذ نسخة بدون قاعدة بيانات.", true);
            return;
        }

        if (IsBusy) return;

        var folder = BackupFolderPath.Trim();
        var fileName = BackupFileName.Trim();

        if (string.IsNullOrWhiteSpace(folder) || string.IsNullOrWhiteSpace(fileName))
        {
            SetStatus("يرجى تحديد مجلد الحفظ واسم ملف النسخة الاحتياطية أولاً.", true);
            return;
        }

        if (fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || fileName.Contains('/') || fileName.Contains('\\'))
        {
            SetStatus("اسم الملف لا يجب أن يحتوي على فواصل مسارات أو رموز غير مسموح بها. اكتب اسم الملف فقط.", true);
            return;
        }

        var extension = Path.GetExtension(fileName);
        if (!string.Equals(extension, ".bak", StringComparison.OrdinalIgnoreCase))
        {
            SetStatus("يجب أن ينتهي اسم ملف النسخة الاحتياطية بامتداد .bak.", true);
            return;
        }

        // The path is sent to SQL Server as-is; SQL Server may be on another machine. Do not call
        // Directory.Exists here, because a valid server path often does not exist on this client.
        string fullPath;
        try
        {
            fullPath = Path.Combine(folder, fileName);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            SetStatus("مسار الحفظ غير صالح. راجع اسم المجلد واسم الملف.", true);
            return;
        }

        IsBusy = true;
        try
        {
            await repository.BackupDatabaseAsync(fullPath);
        }
        catch (Exception ex)
        {
            SetStatus(DescribeError(ex, "تعذر أخذ النسخة الاحتياطية."), true);
            return;
        }
        finally
        {
            IsBusy = false;
        }

        // Never reuse the timestamp of the previous backup: WITH INIT would overwrite that file.
        BackupFileName = CreateDefaultBackupFileName();
        RefreshAvailableBackups();
        RefreshFromDatabase();
        SetStatus($"تم حفظ النسخة الاحتياطية بنجاح في: {fullPath}", false);
    }

    private async Task ExecuteRestoreAsync()
    {
        if (repository is null)
        {
            SetStatus("وضع العرض التجريبي: لا يمكن استرجاع نسخة بدون قاعدة بيانات.", true);
            return;
        }

        if (IsBusy) return;

        var path = RestoreFilePath.Trim();
        if (string.IsNullOrWhiteSpace(path))
        {
            SetStatus("يرجى اختيار ملف نسخة احتياطية (.bak) للاسترجاع أولاً.", true);
            return;
        }

        if (!string.Equals(Path.GetExtension(path), ".bak", StringComparison.OrdinalIgnoreCase))
        {
            SetStatus("يجب أن يكون ملف الاسترجاع بامتداد .bak.", true);
            return;
        }

        // Deliberately no File.Exists: the file is read by SQL Server, which may not be this machine.
        var fileToRestore = Path.GetFileName(path);
        var confirm = new ConfirmActionViewModel(
            "تأكيد استرجاع النسخة الاحتياطية",
            fileToRestore,
            "سيؤدي استرجاع هذه النسخة إلى استبدال قاعدة البيانات الحالية ببيانات الملف المحدد، وستفقد أي بيانات أُضيفت بعد تاريخ النسخة. تأكد من إغلاق جميع الشاشات التي تستخدم القاعدة قبل المتابعة.",
            "استرجاع النسخة");

        if (OpenConfirmDialogRequested is null)
        {
            SetStatus("تعذر فتح نافذة التأكيد. أعد فتح الشاشة وحاول مرة أخرى.", true);
            return;
        }

        OpenConfirmDialogRequested(confirm, async () =>
        {
            if (IsBusy) return;

            IsBusy = true;
            var succeeded = false;
            try
            {
                await repository.RestoreDatabaseAsync(path);
                succeeded = true;
            }
            catch (Exception ex)
            {
                SetStatus(DescribeError(ex, "تعذر استرجاع النسخة الاحتياطية."), true);
            }
            finally
            {
                IsBusy = false;
            }

            if (!succeeded) return;

            RestoreFilePath = "";
            RefreshFromDatabase();
            DatabaseRestored?.Invoke();
            SetStatus($"تم استرجاع النسخة الاحتياطية «{fileToRestore}» بنجاح. تمت إعادة تحميل بيانات القاعدة؛ أعد تحميل بقية الشاشات من أزرار التحديث.", false);
        });
    }

    private static string CreateDefaultBackupFileName()
        => $"ProjectZ_{DateTime.Now:yyyyMMdd_HHmmss}.bak";

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
