
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;

namespace Ocore.desktop.Data
{
    public class DatabaseBackupService
    {
        private readonly string databasePath;
        private readonly string backupFolder;
        private readonly string pendingRestoreFile;

        public DatabaseBackupService()
        {
            var databaseManager = new DatabaseManager();

            databasePath = databaseManager.GetDatabasePath();

            string dataFolder = Path.GetDirectoryName(databasePath);

            if (string.IsNullOrWhiteSpace(dataFolder))
            {
                throw new InvalidOperationException(
                    "تعذر تحديد مجلد قاعدة البيانات.");
            }

            string applicationDataFolder =
                Directory.GetParent(dataFolder)?.FullName;

            if (string.IsNullOrWhiteSpace(applicationDataFolder))
            {
                throw new InvalidOperationException(
                    "تعذر تحديد مجلد بيانات البرنامج.");
            }

            backupFolder = Path.Combine(
                applicationDataFolder,
                "Backups");

            pendingRestoreFile = Path.Combine(
                backupFolder,
                "OCORE_Restore_Pending.txt");

            Directory.CreateDirectory(backupFolder);
        }

        // عرض النسخ الاحتياطية المتاحة
        public List<string> GetAvailableBackups()
        {
            if (!Directory.Exists(backupFolder))
                return new List<string>();

            return Directory.GetFiles(backupFolder, "*.db")
                .Where(IsSupportedBackup)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .ToList();
        }

        private bool IsSupportedBackup(string path)
        {
            string name = Path.GetFileName(path);

            return
                name.StartsWith(
                    "OCORE_Daily_",
                    StringComparison.OrdinalIgnoreCase)
                ||
                name.StartsWith(
                    "OCORE_Backup_Before_Update_",
                    StringComparison.OrdinalIgnoreCase)
                ||
                name.StartsWith(
                    "OCORE_Before_Restore_",
                    StringComparison.OrdinalIgnoreCase);
        }

        // التحقق من أن الملف داخل مجلد النسخ الاحتياطية
        private bool IsInsideBackupFolder(string path)
        {
            string fullPath = Path.GetFullPath(path);

            string directory = Path.GetDirectoryName(fullPath)
                .TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);

            string expectedDirectory = Path.GetFullPath(backupFolder)
                .TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);

            return string.Equals(
                directory,
                expectedDirectory,
                StringComparison.OrdinalIgnoreCase);
        }

        // قراءة إصدار قاعدة البيانات الحالية
        private int GetCurrentDatabaseVersion()
        {
            if (!File.Exists(databasePath))
            {
                throw new FileNotFoundException(
                    "قاعدة البيانات الحالية غير موجودة.");
            }

            using (var connection = new SQLiteConnection(
                "Data Source=" + databasePath +
                ";Version=3;Read Only=True;Pooling=False;"))
            {
                connection.Open();

                using (var command = new SQLiteCommand(
                    "PRAGMA user_version;",
                    connection))
                {
                    return Convert.ToInt32(
                        command.ExecuteScalar());
                }
            }
        }

        // فحص سلامة قاعدة البيانات وتوافق إصدارها
        private void ValidateDatabase(string path)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    "ملف قاعدة البيانات غير موجود.");
            }

            if (new FileInfo(path).Length == 0)
            {
                throw new Exception(
                    "ملف قاعدة البيانات فارغ.");
            }

            int currentVersion = GetCurrentDatabaseVersion();

            using (var connection = new SQLiteConnection(
                "Data Source=" + path +
                ";Version=3;Read Only=True;Pooling=False;"))
            {
                connection.Open();

                using (var command = new SQLiteCommand(
                    "PRAGMA integrity_check;",
                    connection))
                {
                    string result = Convert.ToString(
                        command.ExecuteScalar());

                    if (!string.Equals(
                        result,
                        "ok",
                        StringComparison.OrdinalIgnoreCase))
                    {
                        throw new Exception(
                            "فشل فحص سلامة قاعدة البيانات: " +
                            result);
                    }
                }

                using (var command = new SQLiteCommand(
                    "PRAGMA user_version;",
                    connection))
                {
                    int version = Convert.ToInt32(
                        command.ExecuteScalar());

                    if (version > currentVersion)
                    {
                        throw new Exception(
                            "إصدار قاعدة البيانات المختارة (" +
                            version +
                            ") أحدث من إصدار قاعدة البيانات الحالية (" +
                            currentVersion +
                            "). لا يمكن استعادتها بهذا الإصدار من البرنامج.");
                    }
                }
            }
        }

        // إنشاء نسخة SQLite متسقة باستخدام BackupDatabase
        private void CreateDatabaseSnapshot(
            string sourcePath,
            string destinationPath)
        {
            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException(
                    "قاعدة البيانات المصدر غير موجودة.");
            }

            if (string.Equals(
                Path.GetFullPath(sourcePath),
                Path.GetFullPath(destinationPath),
                StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "لا يمكن إنشاء نسخة احتياطية فوق قاعدة البيانات الأصلية.");
            }

            if (File.Exists(destinationPath))
            {
                throw new IOException(
                    "ملف الوجهة موجود بالفعل.");
            }

            SQLiteConnection.CreateFile(destinationPath);

            try
            {
                using (var source = new SQLiteConnection(
                    "Data Source=" + sourcePath +
                    ";Version=3;Pooling=False;"))
                using (var destination = new SQLiteConnection(
                    "Data Source=" + destinationPath +
                    ";Version=3;Pooling=False;"))
                {
                    source.Open();
                    destination.Open();

                    source.BackupDatabase(
                        destination,
                        "main",
                        "main",
                        -1,
                        null,
                        0);
                }

                if (!File.Exists(destinationPath) ||
                    new FileInfo(destinationPath).Length == 0)
                {
                    throw new Exception(
                        "ملف النسخة الناتج فارغ أو غير صالح.");
                }
            }
            catch
            {
                try
                {
                    if (File.Exists(destinationPath))
                        File.Delete(destinationPath);
                }
                catch
                {
                }

                throw;
            }
        }

        // تصدير قاعدة البيانات كاملة إلى مسار يختاره المستخدم
        public string ExportDatabase(string destinationPath)
        {
            string temporaryPath = null;

            try
            {
                if (string.IsNullOrWhiteSpace(destinationPath))
                {
                    throw new ArgumentException(
                        "يرجى تحديد مكان حفظ النسخة.");
                }

                string fullDestinationPath =
                    Path.GetFullPath(destinationPath);

                if (string.Equals(
                    fullDestinationPath,
                    Path.GetFullPath(databasePath),
                    StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "لا يمكن تصدير النسخة فوق قاعدة البيانات الأصلية.");
                }

                string destinationFolder =
                    Path.GetDirectoryName(fullDestinationPath);

                if (string.IsNullOrWhiteSpace(destinationFolder) ||
                    !Directory.Exists(destinationFolder))
                {
                    throw new DirectoryNotFoundException(
                        "مجلد حفظ النسخة غير موجود.");
                }

                temporaryPath = Path.Combine(
                    destinationFolder,
                    ".OCORE_Export_Temp_" +
                    Guid.NewGuid().ToString("N") +
                    ".db");

                CreateDatabaseSnapshot(
                    databasePath,
                    temporaryPath);

                ValidateDatabase(temporaryPath);

                // اعتماد الملف النهائي بعد نجاح التصدير والتحقق
                if (File.Exists(fullDestinationPath))
                {
                    File.Replace(
                        temporaryPath,
                        fullDestinationPath,
                        null);
                }
                else
                {
                    File.Move(
                        temporaryPath,
                        fullDestinationPath);
                }

                temporaryPath = null;

                return
                    "تم تصدير قاعدة البيانات كاملة بنجاح.\n\n" +
                    "مسار الملف:\n" +
                    fullDestinationPath;
            }
            catch (Exception ex)
            {
                return
                    "تعذر تصدير قاعدة البيانات:\n\n" +
                    ex.Message;
            }
            finally
            {
                try
                {
                    if (temporaryPath != null &&
                        File.Exists(temporaryPath))
                    {
                        File.Delete(temporaryPath);
                    }
                }
                catch
                {
                }
            }
        }

        // إنشاء نسخة أمان من قاعدة البيانات الحالية
        private string CreateSafetyBackup()
        {
            if (!File.Exists(databasePath))
            {
                throw new Exception(
                    "قاعدة البيانات الحالية غير موجودة.");
            }

            string backupName =
                "OCORE_Before_Restore_" +
                DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") +
                ".db";

            string backupPath = Path.Combine(
                backupFolder,
                backupName);

            try
            {
                CreateDatabaseSnapshot(
                    databasePath,
                    backupPath);

                ValidateDatabase(backupPath);

                return backupPath;
            }
            catch
            {
                try
                {
                    if (File.Exists(backupPath))
                        File.Delete(backupPath);
                }
                catch
                {
                }

                throw;
            }
        }

        // تجهيز نسخة للاستعادة دون تغيير قاعدة البيانات الحالية
        // النسخ الداخلية فقط تقبل أسماء ومسارات النسخ المعروفة
        public string PrepareRestore(string selectedBackupPath)
        {
            return PrepareRestoreCore(
                selectedBackupPath,
                true);
        }

        // تجهيز استعادة من ملف خارجي يختاره المستخدم
        public string PrepareRestoreExternal(string externalDatabasePath)
        {
            return PrepareRestoreCore(
                externalDatabasePath,
                false);
        }

        private string PrepareRestoreCore(
            string selectedBackupPath,
            bool requireInternalBackup)
        {
            string stagedPath = null;

            try
            {
                if (string.IsNullOrWhiteSpace(selectedBackupPath))
                {
                    return "يرجى اختيار ملف قاعدة البيانات.";
                }

                if (File.Exists(pendingRestoreFile))
                {
                    return
                        "توجد عملية استعادة معلقة بالفعل.\n" +
                        "أغلق البرنامج وافتحه لإكمالها أولاً.";
                }

                string fullPath = Path.GetFullPath(
                    selectedBackupPath);

                if (string.Equals(
                    fullPath,
                    Path.GetFullPath(databasePath),
                    StringComparison.OrdinalIgnoreCase))
                {
                    return "لا يمكن استعادة قاعدة البيانات من نفسها.";
                }

                if (requireInternalBackup)
                {
                    if (!IsInsideBackupFolder(fullPath))
                    {
                        return "مسار النسخة الاحتياطية غير مسموح.";
                    }

                    if (!IsSupportedBackup(fullPath))
                    {
                        return "نوع النسخة الاحتياطية غير مدعوم.";
                    }
                }
                else
                {
                    if (!File.Exists(fullPath))
                    {
                        return "ملف قاعدة البيانات الخارجية غير موجود.";
                    }

                    if (!string.Equals(
                        Path.GetExtension(fullPath),
                        ".db",
                        StringComparison.OrdinalIgnoreCase))
                    {
                        return "يجب اختيار ملف قاعدة بيانات بامتداد .db.";
                    }
                }

                // فحص الملف الأصلي قبل إنشاء النسخة المرحلية
                ValidateDatabase(fullPath);

                string stagedName =
                    "OCORE_Restore_Staged_" +
                    DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") +
                    ".db";

                stagedPath = Path.Combine(
                    backupFolder,
                    stagedName);

                CreateDatabaseSnapshot(
                    fullPath,
                    stagedPath);

                // فحص النسخة المرحلية مرة أخرى
                ValidateDatabase(stagedPath);

                string markerTemporary =
                    pendingRestoreFile + ".tmp";

                if (File.Exists(markerTemporary))
                    File.Delete(markerTemporary);

                File.WriteAllText(
                    markerTemporary,
                    stagedPath);

                File.Move(
                    markerTemporary,
                    pendingRestoreFile);

                return
                    "تم تجهيز قاعدة البيانات للاستعادة بنجاح.\n\n" +
                    "الملف المختار:\n" +
                    Path.GetFileName(fullPath) +
                    "\n\n" +
                    "لم يتم تغيير قاعدة البيانات الحالية حتى الآن.\n\n" +
                    "أغلق البرنامج بالكامل ثم افتحه مرة أخرى " +
                    "لإتمام الاستعادة.";
            }
            catch (Exception ex)
            {
                try
                {
                    if (stagedPath != null &&
                        File.Exists(stagedPath))
                    {
                        File.Delete(stagedPath);
                    }
                }
                catch
                {
                }

                return
                    "تعذر تجهيز قاعدة البيانات للاستعادة:\n\n" +
                    ex.Message;
            }
        }

        // معرفة ما إذا كانت هناك عملية استعادة معلقة
        public bool HasPendingRestore()
        {
            return File.Exists(pendingRestoreFile);
        }

        // تنفيذ الاستعادة عند بدء البرنامج
        public string ApplyPendingRestore()
        {
            if (!File.Exists(pendingRestoreFile))
                return null;

            string stagedPath = File.ReadAllText(
                pendingRestoreFile).Trim();

            if (string.IsNullOrWhiteSpace(stagedPath))
            {
                throw new Exception(
                    "ملف طلب الاستعادة فارغ.");
            }

            stagedPath = Path.GetFullPath(stagedPath);

            if (!IsInsideBackupFolder(stagedPath))
            {
                throw new Exception(
                    "مسار ملف الاستعادة المجهز غير مسموح.");
            }

            if (!Path.GetFileName(stagedPath).StartsWith(
                "OCORE_Restore_Staged_",
                StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception(
                    "اسم ملف الاستعادة المجهز غير صحيح.");
            }

            ValidateDatabase(stagedPath);

            if (!File.Exists(databasePath))
            {
                throw new Exception(
                    "قاعدة البيانات الحالية غير موجودة.");
            }

            // إنشاء نسخة أمان قبل استبدال قاعدة البيانات
            string safetyBackupPath = CreateSafetyBackup();

            SQLiteConnection.ClearAllPools();

            File.Replace(
                stagedPath,
                databasePath,
                null);

            File.Delete(pendingRestoreFile);

            return
                "تمت استعادة قاعدة البيانات بنجاح.\n\n" +
                "تم الاحتفاظ بنسخة أمان من البيانات السابقة في:\n" +
                safetyBackupPath;
        }
    }
}
