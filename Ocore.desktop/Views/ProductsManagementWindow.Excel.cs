
using Microsoft.Win32;
using Ocore.desktop.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace Ocore.desktop.Views
{
    public partial class ProductsManagementWindow : Window
    {
        private void InitializeExcelActions()
        {
            ExportProductsExcelButton.Click += ExportProductsExcelButton_Click;
            ImportProductsExcelButton.Click += ImportProductsExcelButton_Click;
        }

        private void ExportProductsExcelButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            try
            {
                var dialog = new SaveFileDialog
                {
                    Title = "تصدير بيانات الأصناف",
                    Filter = "Excel Workbook (*.xlsx)|*.xlsx",
                    FileName = "OCORE_Products.xlsx",
                    DefaultExt = ".xlsx",
                    AddExtension = true
                };

                if (dialog.ShowDialog(this) != true)
                    return;

                var excelService = new ProductExcelService();

                // allProducts تحتوي الأصناف النشطة والمؤرشفة.
                excelService.ExportProducts(
                    dialog.FileName,
                    allProducts);

                MessageBox.Show(
                    "تم تصدير بيانات الأصناف والمخزون بنجاح.\n\n" +
                    "بيانات الرصيد داخل الملف للعرض فقط ولا يتم استيرادها.",
                    "تصدير Excel",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (UnauthorizedAccessException ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "الصلاحيات",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                Ocore.desktop.ErrorDialog.Show("تصدير بيانات الأصناف إلى Excel", ex);
            }
        }

        private void ImportProductsExcelButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            try
            {
                var dialog = new OpenFileDialog
                {
                    Title = "اختيار ملف الأصناف للاستيراد",
                    Filter = "Excel Workbook (*.xlsx)|*.xlsx",
                    CheckFileExists = true,
                    Multiselect = false
                };

                if (dialog.ShowDialog(this) != true)
                    return;

                var excelService = new ProductExcelService();

                var rows = excelService.ReadImportFile(
                    dialog.FileName);

                if (rows == null || rows.Count == 0)
                {
                    MessageBox.Show(
                        "الملف لا يحتوي على أصناف قابلة للاستيراد.",
                        "استيراد Excel",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    return;
                }

                var importService = new ProductExcelImportService();

                var preview = importService.PreviewImport(
                    currentUserId,
                    rows);

                bool hasErrors = preview.Any(x => x.HasError);

                bool confirmed = ShowExcelImportPreview(
                    preview,
                    hasErrors);

                if (!confirmed || hasErrors)
                    return;

                var result = importService.Import(
                    currentUserId,
                    rows);

                LoadProducts();

                MessageBox.Show(
                    "تم استيراد البيانات بنجاح.\n\n" +
                    "الأصناف الجديدة: " + result.AddedCount + "\n" +
                    "الأصناف التي تم تحديثها: " + result.UpdatedCount + "\n\n" +
                    "لم يتم تغيير أرصدة المخزون أو التشغيلات أو البيانات الضريبية.",
                    "اكتمل الاستيراد",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (UnauthorizedAccessException ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "الصلاحيات",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                Ocore.desktop.ErrorDialog.Show("استيراد بيانات الأصناف من Excel", ex);
            }
        }

        private bool ShowExcelImportPreview(
            IList<ProductExcelImportPreviewRow> rows,
            bool hasErrors)
        {
            int added = rows.Count(
                x => !x.HasError &&
                     x.Action == "إضافة صنف جديد");

            int updated = rows.Count(
                x => !x.HasError &&
                     x.Action == "تحديث صنف موجود");

            int errors = rows.Count(x => x.HasError);

            var window = new Window
            {
                Title = "معاينة استيراد الأصناف",
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Width = 1050,
                Height = 650,
                MinWidth = 800,
                MinHeight = 450,
                FlowDirection = FlowDirection.RightToLeft,
                Background = Brushes.White,
                ResizeMode = ResizeMode.CanResize
            };

            var root = new Grid
            {
                Margin = new Thickness(14)
            };

            root.RowDefinitions.Add(
                new RowDefinition { Height = GridLength.Auto });

            root.RowDefinitions.Add(
                new RowDefinition { Height = GridLength.Auto });

            root.RowDefinitions.Add(
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            root.RowDefinitions.Add(
                new RowDefinition { Height = GridLength.Auto });

            var title = new TextBlock
            {
                Text = "مراجعة بيانات الأصناف قبل الاستيراد",
                FontSize = 19,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(
                    Color.FromRgb(24, 63, 54)),
                Margin = new Thickness(0, 0, 0, 12)
            };

            Grid.SetRow(title, 0);
            root.Children.Add(title);

            var summary = new TextBlock
            {
                Text =
                    "أصناف جديدة: " + added +
                    "     |     أصناف سيتم تحديثها: " + updated +
                    "     |     أخطاء: " + errors,
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 12),
                Foreground = hasErrors
                    ? Brushes.DarkRed
                    : Brushes.DarkGreen
            };

            Grid.SetRow(summary, 1);
            root.Children.Add(summary);

            var grid = new DataGrid
            {
                ItemsSource = rows,
                AutoGenerateColumns = false,
                IsReadOnly = true,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                SelectionMode = DataGridSelectionMode.Single,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                RowHeight = 34,
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 12)
            };

            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "رقم الصف",
                Binding = new Binding("RowNumber"),
                Width = 75
            });

            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "كود الصنف",
                Binding = new Binding("ProductCode"),
                Width = 115
            });

            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "اسم الصنف",
                Binding = new Binding("ProductName"),
                Width = new DataGridLength(2, DataGridLengthUnitType.Star)
            });

            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "الإجراء",
                Binding = new Binding("Action"),
                Width = 145
            });

            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "الخطأ أو الملاحظة",
                Binding = new Binding("Error"),
                Width = new DataGridLength(2, DataGridLengthUnitType.Star)
            });

            Grid.SetRow(grid, 2);
            root.Children.Add(grid);

            var footer = new DockPanel
            {
                LastChildFill = false,
                Margin = new Thickness(0, 8, 0, 0)
            };

            var cancelButton = new Button
            {
                Content = hasErrors ? "إغلاق وتصحيح الملف" : "إلغاء",
                Width = 155,
                Height = 38,
                Margin = new Thickness(5),
                IsCancel = true
            };

            cancelButton.Click += (s, e) =>
            {
                window.DialogResult = false;
                window.Close();
            };

            DockPanel.SetDock(cancelButton, Dock.Left);
            footer.Children.Add(cancelButton);

            var confirmButton = new Button
            {
                Content = "تأكيد الاستيراد",
                Width = 145,
                Height = 38,
                Margin = new Thickness(5),
                Background = new SolidColorBrush(
                    Color.FromRgb(33, 115, 70)),
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                IsEnabled = !hasErrors
            };

            confirmButton.Click += (s, e) =>
            {
                window.DialogResult = true;
                window.Close();
            };

            DockPanel.SetDock(confirmButton, Dock.Left);
            footer.Children.Add(confirmButton);

            var note = new TextBlock
            {
                Text = hasErrors
                    ? "لن يتم الاستيراد قبل تصحيح جميع الأخطاء."
                    : "راجع البيانات ثم اضغط تأكيد الاستيراد.",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8),
                Foreground = hasErrors
                    ? Brushes.DarkRed
                    : Brushes.Gray
            };

            DockPanel.SetDock(note, Dock.Right);
            footer.Children.Add(note);

            Grid.SetRow(footer, 3);
            root.Children.Add(footer);

            window.Content = root;

            return window.ShowDialog() == true;
        }
    }
}
