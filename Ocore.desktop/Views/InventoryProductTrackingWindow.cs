using System;
using System.Data;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Ocore.desktop.Services;

namespace Ocore.desktop.Views
{
    public sealed class InventoryProductTrackingWindow : Window
    {
        private readonly int managerId;
        private readonly InventoryProductTrackingService trackingService;

        private DataGrid productsGrid;
        private CheckBox trackBatchCheckBox;
        private CheckBox trackExpiryCheckBox;
        private CheckBox confirmStockCheckBox;
        private TextBox reasonTextBox;
        private TextBlock stockText;

        public InventoryProductTrackingWindow(int userId)
        {
            managerId = userId;
            trackingService = new InventoryProductTrackingService();

            // التحقق قبل إنشاء الشاشة أو تحميل بيانات الأرصدة.
            trackingService.EnsureManagerAccess(managerId);

            Title = "إعدادات تتبع الأصناف";
            Width = 1050;
            Height = 720;
            MinWidth = 850;
            MinHeight = 600;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            FlowDirection = FlowDirection.RightToLeft;
            Background = new SolidColorBrush(
                Color.FromRgb(243, 246, 248));

            Content = BuildLayout();
            LoadProducts(0);
        }

        private UIElement BuildLayout()
        {
            var root = new Grid
            {
                Margin = new Thickness(16)
            };

            root.RowDefinitions.Add(
                new RowDefinition { Height = GridLength.Auto });

            root.RowDefinitions.Add(
                new RowDefinition
                {
                    Height = new GridLength(1, GridUnitType.Star)
                });

            root.RowDefinitions.Add(
                new RowDefinition { Height = GridLength.Auto });

            var heading = new StackPanel
            {
                Margin = new Thickness(0, 0, 0, 12)
            };

            heading.Children.Add(new TextBlock
            {
                Text = "إعدادات تتبع رقم التشغيلة وتاريخ الصلاحية",
                FontSize = 22,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(
                    Color.FromRgb(18, 61, 53))
            });

            heading.Children.Add(new TextBlock
            {
                Text =
                    "اختر الصنف ثم حدد نوع التتبع. هذه الإعدادات للمدير فقط، " +
                    "ولا تحذف التشغيلات أو تكاليف الاستلامات السابقة.",
                Margin = new Thickness(0, 5, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brushes.DimGray
            });

            Grid.SetRow(heading, 0);
            root.Children.Add(heading);

            productsGrid = new DataGrid
            {
                AutoGenerateColumns = false,
                IsReadOnly = true,
                CanUserAddRows = false,
                SelectionMode = DataGridSelectionMode.Single,
                SelectionUnit = DataGridSelectionUnit.FullRow,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                GridLinesVisibility =
                    DataGridGridLinesVisibility.Horizontal,
                Margin = new Thickness(0, 0, 0, 12),
                Background = Brushes.White
            };

            productsGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "كود الصنف",
                Binding = new Binding("ProductCode"),
                Width = new DataGridLength(130)
            });

            productsGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "اسم الصنف",
                Binding = new Binding("ProductName"),
                Width = new DataGridLength(
                    1,
                    DataGridLengthUnitType.Star)
            });

            productsGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "الرصيد الحالي",
                Binding = new Binding("CurrentStock")
                {
                    StringFormat = "0.########"
                },
                Width = new DataGridLength(150)
            });

            productsGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "آخر تحديث",
                Binding = new Binding("UpdatedAt"),
                Width = new DataGridLength(170)
            });

            productsGrid.SelectionChanged +=
                ProductsGrid_SelectionChanged;

            Grid.SetRow(productsGrid, 1);
            root.Children.Add(productsGrid);

            var settings = new StackPanel
            {
                Background = Brushes.White,
                Margin = new Thickness(0),
                Orientation = Orientation.Vertical
            };

            var trackingOptions = new WrapPanel
            {
                Margin = new Thickness(10, 8, 10, 4)
            };

            trackBatchCheckBox = new CheckBox
            {
                Content = "تتبع رقم التشغيلة",
                Margin = new Thickness(8),
                FontSize = 15
            };

            trackExpiryCheckBox = new CheckBox
            {
                Content = "تتبع تاريخ الصلاحية",
                Margin = new Thickness(8),
                FontSize = 15
            };

            trackingOptions.Children.Add(trackBatchCheckBox);
            trackingOptions.Children.Add(trackExpiryCheckBox);

            settings.Children.Add(trackingOptions);

            stockText = new TextBlock
            {
                Text = "الرصيد الحالي: —",
                Margin = new Thickness(12, 2, 12, 6),
                FontWeight = FontWeights.SemiBold
            };

            settings.Children.Add(stockText);

            confirmStockCheckBox = new CheckBox
            {
                Content = new TextBlock
                {
                    Text =
                        "أؤكد أنني راجعت أثر تغيير التتبع على الرصيد المخزني الحالي",
                    TextWrapping = TextWrapping.Wrap
                },
                Margin = new Thickness(12, 2, 12, 8)
            };

            settings.Children.Add(confirmStockCheckBox);

            reasonTextBox = new TextBox
            {
                Margin = new Thickness(10, 4, 10, 8),
                Height = 38,
                Padding = new Thickness(8),
                VerticalContentAlignment =
                    VerticalAlignment.Center,
                ToolTip = "سبب تغيير إعدادات التتبع (إلزامي)"
            };

            settings.Children.Add(reasonTextBox);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(8)
            };

            var saveButton = new Button
            {
                Content = "حفظ إعدادات التتبع",
                Width = 180,
                Height = 40,
                Margin = new Thickness(5),
                Background = new SolidColorBrush(
                    Color.FromRgb(22, 134, 93)),
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold
            };

            saveButton.Click += SaveButton_Click;
            buttons.Children.Add(saveButton);

            var closeButton = new Button
            {
                Content = "إغلاق",
                Width = 110,
                Height = 40,
                Margin = new Thickness(5)
            };

            closeButton.Click += (sender, args) => Close();
            buttons.Children.Add(closeButton);

            settings.Children.Add(buttons);

            Grid.SetRow(settings, 2);
            root.Children.Add(settings);

            return root;
        }

        private void LoadProducts(int preferredProductId)
        {
            // إعادة التحقق قبل كل تحميل للبيانات.
            trackingService.EnsureManagerAccess(managerId);

            DataTable table =
                trackingService.GetAllSettings(managerId);

            productsGrid.ItemsSource = table.DefaultView;

            if (table.Rows.Count == 0)
            {
                trackBatchCheckBox.IsChecked = false;
                trackExpiryCheckBox.IsChecked = false;
                confirmStockCheckBox.IsChecked = false;
                stockText.Text = "لا توجد أصناف نشطة.";
                reasonTextBox.Clear();
                return;
            }

            DataRowView selected = null;

            foreach (var item in productsGrid.Items)
            {
                var row = item as DataRowView;

                if (row != null &&
                    Convert.ToInt32(row["ProductId"]) ==
                    preferredProductId)
                {
                    selected = row;
                    break;
                }
            }

            productsGrid.SelectedItem =
                selected ?? productsGrid.Items[0];

            if (productsGrid.SelectedItem != null)
                UpdateSelectedProduct();
        }

        private void ProductsGrid_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            UpdateSelectedProduct();
        }

        private void UpdateSelectedProduct()
        {
            var row = productsGrid.SelectedItem as DataRowView;

            if (row == null)
                return;

            trackBatchCheckBox.IsChecked =
                Convert.ToInt32(row["TrackBatchNumber"]) == 1;

            trackExpiryCheckBox.IsChecked =
                Convert.ToInt32(row["TrackExpiryDate"]) == 1;

            double stock =
                Convert.ToDouble(row["CurrentStock"]);

            stockText.Text =
                "الرصيد الحالي: " +
                stock.ToString(
                    "0.########",
                    System.Globalization.CultureInfo.InvariantCulture);

            confirmStockCheckBox.IsChecked = false;
            reasonTextBox.Clear();
        }

        private void SaveButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            var row = productsGrid.SelectedItem as DataRowView;

            if (row == null)
            {
                MessageBox.Show(
                    this,
                    "اختر صنفًا أولًا.",
                    "إعدادات التتبع",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            if (string.IsNullOrWhiteSpace(reasonTextBox.Text))
            {
                MessageBox.Show(
                    this,
                    "اكتب سبب تغيير إعدادات التتبع.",
                    "بيانات مطلوبة",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                reasonTextBox.Focus();
                return;
            }

            int productId =
                Convert.ToInt32(row["ProductId"]);

            try
            {
                trackingService.SaveSettings(
                    managerId,
                    productId,
                    trackBatchCheckBox.IsChecked == true,
                    trackExpiryCheckBox.IsChecked == true,
                    reasonTextBox.Text,
                    confirmStockCheckBox.IsChecked == true);

                MessageBox.Show(
                    this,
                    "تم حفظ إعدادات التتبع.",
                    "إعدادات التتبع",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                LoadProducts(productId);
            }
            catch (UnauthorizedAccessException ex)
            {
                MessageBox.Show(
                    this,
                    ex.Message,
                    "صلاحيات المستخدم",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                Ocore.desktop.ErrorDialog.Show("حفظ إعدادات تتبع الصنف", ex);
            }
        }
    }
}