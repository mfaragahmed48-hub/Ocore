
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Ocore.desktop.Services;
using Ocore.desktop.Models;

namespace Ocore.desktop.Views
{
    public partial class AuditLogsWindow : Window
    {
        private readonly AuditLogService auditLogService;
        private readonly PermissionService permissionService;

        private readonly int currentUserId;
        private readonly string currentRole;

        private List<AuditLog> allLogs =
            new List<AuditLog>();

        private List<AuditLog> filteredLogs =
            new List<AuditLog>();

        public AuditLogsWindow(int userId, string role)
        {
            InitializeComponent();

            currentUserId = userId;
            currentRole = role;

            permissionService = new PermissionService();

            if (!permissionService.HasPermission(
                currentRole,
                "Audit.View"))
            {
                MessageBox.Show(
                    "ليس لديك صلاحية عرض سجل النشاطات.",
                    "رفض الوصول",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                Close();
                return;
            }

            auditLogService = new AuditLogService();

            LoadLogs();
        }

        private void LoadLogs()
        {
            try
            {
                allLogs = auditLogService.GetAllLogs();
                FilterLogs();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "تعذر تحميل سجل النشاطات: " + ex.Message,
                    "خطأ",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void FilterLogs()
        {
            if (SearchUserTextBox == null ||
                ActionFilterComboBox == null ||
                StartDatePicker == null ||
                EndDatePicker == null ||
                LogsDataGrid == null ||
                LogsCountText == null)
                return;

            string searchText = SearchUserTextBox.Text.Trim();
            string selectedAction = "";

            if (ActionFilterComboBox.SelectedItem
                is ComboBoxItem selectedItem)
            {
                selectedAction =
                    selectedItem.Tag?.ToString() ?? "";
            }

            DateTime? startDate =
                StartDatePicker.SelectedDate?.Date;

            DateTime? endDate =
                EndDatePicker.SelectedDate?.Date;

            filteredLogs = allLogs
                .Where(log =>
                    (
                        string.IsNullOrWhiteSpace(searchText) ||
                        (!string.IsNullOrEmpty(log.Username) &&
                         log.Username.IndexOf(
                             searchText,
                             StringComparison.OrdinalIgnoreCase) >= 0)
                    )
                    &&
                    (
                        string.IsNullOrWhiteSpace(selectedAction) ||
                        log.ActionType ==
                        GetActionDisplayName(selectedAction)
                    )
                    &&
                    (
                        !startDate.HasValue ||
                        log.CreatedAt.Date >= startDate.Value
                    )
                    &&
                    (
                        !endDate.HasValue ||
                        log.CreatedAt.Date <= endDate.Value
                    )
                )
                .ToList();

            LogsDataGrid.ItemsSource = filteredLogs;

            LogsCountText.Text =
                "عدد العمليات: " + filteredLogs.Count;
        }

        private string GetActionDisplayName(string action)
        {
            switch (action)
            {
                case "Create": return "إنشاء";
                case "Update": return "تعديل";
                case "Activate": return "تنشيط حساب";
                case "Deactivate": return "إيقاف حساب";
                case "Delete": return "حذف";
                case "Login": return "تسجيل دخول";
                case "Logout": return "تسجيل خروج";
                default: return action;
            }
        }

        private void SearchUserTextBox_TextChanged(
            object sender, TextChangedEventArgs e)
        {
            FilterLogs();
        }

        private void ActionFilterComboBox_SelectionChanged(
            object sender, SelectionChangedEventArgs e)
        {
            FilterLogs();
        }

        private void DateFilter_Changed(
            object sender, SelectionChangedEventArgs e)
        {
            FilterLogs();
        }

        private void ClearDates_Click(
            object sender, RoutedEventArgs e)
        {
            StartDatePicker.SelectedDate = null;
            EndDatePicker.SelectedDate = null;
            FilterLogs();
        }

        private void Refresh_Click(
            object sender, RoutedEventArgs e)
        {
            LoadLogs();
        }

        private void ExportExcel_Click(
            object sender, RoutedEventArgs e)
        {
            try
            {
                if (filteredLogs == null ||
                    filteredLogs.Count == 0)
                {
                    MessageBox.Show(
                        "لا توجد عمليات لتصديرها.",
                        "تنبيه",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                var dialog = new SaveFileDialog
                {
                    Title = "حفظ سجل النشاطات",
                    Filter = "CSV Excel Files (*.csv)|*.csv",
                    FileName = "OCORE_AuditLogs_" +
                        DateTime.Now.ToString("yyyyMMdd_HHmmss") +
                        ".csv",
                    DefaultExt = ".csv",
                    AddExtension = true
                };

                if (dialog.ShowDialog() != true)
                    return;

                var csv = new StringBuilder();

                csv.AppendLine(
                    "رقم,المستخدم,العملية,نوع العنصر,رقم العنصر,التفاصيل,الجهاز,التاريخ والوقت");

                foreach (var log in filteredLogs)
                {
                    csv.AppendLine(string.Join(",",
                        CsvField(log.Id.ToString()),
                        CsvField(log.Username),
                        CsvField(log.ActionType),
                        CsvField(log.EntityType),
                        CsvField(log.EntityId?.ToString() ?? ""),
                        CsvField(log.Details),
                        CsvField(log.DeviceName),
                        CsvField(log.CreatedAt.ToString(
                            "yyyy/MM/dd HH:mm:ss"))
                    ));
                }

                var encoding = new UTF8Encoding(true);

                System.IO.File.WriteAllText(
                    dialog.FileName,
                    csv.ToString(),
                    encoding);

                MessageBox.Show(
                    "تم تصدير سجل النشاطات بنجاح.\nعدد العمليات: " +
                    filteredLogs.Count,
                    "تم التصدير",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "تعذر تصدير سجل النشاطات: " + ex.Message,
                    "خطأ",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private string CsvField(string value)
        {
            if (value == null)
                value = "";

            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        private void Close_Click(
            object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
