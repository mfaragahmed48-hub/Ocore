
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Ocore.desktop.Models;
using Ocore.desktop.Services;

namespace Ocore.desktop.Views
{
    public partial class ManageStockWindow : Window
    {
        private readonly int currentUserId;
        private readonly Product selectedProduct;
        private readonly BatchService batchService;

        private List<BatchDisplayInfo> allBatches;

        public ManageStockWindow(int userId, Product product)
        {
            InitializeComponent();

            if (product == null)
                throw new ArgumentNullException(nameof(product));

            currentUserId = userId;
            selectedProduct = product;

            batchService = new BatchService();
            AdjustStockButton.Visibility =
    batchService.IsManager(currentUserId)
        ? Visibility.Visible
        : Visibility.Collapsed;
            allBatches = new List<BatchDisplayInfo>();

            ProductNameTextBlock.Text =
                "الصنف: " + selectedProduct.Name +
                " | الكود: " + selectedProduct.ProductCode;

            ExpiryDatePicker.SelectedDate =
                DateTime.Today.AddMonths(6);

            LoadBatches();
        }

        private void LoadBatches()
        {
            try
            {
                var batches = batchService.GetProductBatches(
                    currentUserId,
                    selectedProduct.Id);

                allBatches = batches.Select(batch =>
                    new BatchDisplayInfo
                    {
                        Id = batch.Id,
                        BatchNumber = batch.BatchNumber,
                        ExpiryDate = batch.ExpiryDate,
                        PurchasePrice = batch.PurchasePrice,
                        Quantity = batch.Quantity
                    }).ToList();

                UpdateSummary();
                ApplyBatchFilter();
            }
            catch (UnauthorizedAccessException ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "الصلاحيات",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "حدث خطأ أثناء تحميل الدفعات:\n" + ex.Message,
                    "خطأ",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void UpdateSummary()
        {
            DateTime today = DateTime.Today;

            // إجمالي الكمية الصالحة للبيع فقط.
            double totalQuantity = allBatches
                .Where(batch =>
                    batch.Quantity > 0 &&
                    batch.ExpiryDate.Date >= today)
                .Sum(batch => batch.Quantity);

            int batchCount = allBatches.Count(
                batch => batch.Quantity > 0);

            int expiredCount = allBatches.Count(
                batch =>
                    batch.Quantity > 0 &&
                    batch.ExpiryDate.Date < today);

            TotalQuantityTextBlock.Text =
                totalQuantity.ToString(
                    "N4",
                    CultureInfo.CurrentCulture);

            BatchCountTextBlock.Text =
                batchCount.ToString();

            ExpiredBatchCountTextBlock.Text =
                expiredCount.ToString();
        }

        private void ApplyBatchFilter()
        {
            if (allBatches == null)
                return;

            bool showAll =
                BatchFilterComboBox.SelectedIndex == 1;

            if (showAll)
            {
                BatchesDataGrid.ItemsSource = allBatches;
            }
            else
            {
                BatchesDataGrid.ItemsSource =
                    allBatches.Where(
                        batch => batch.Quantity > 0
                    ).ToList();
            }
        }

        private void BatchFilterComboBox_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            ApplyBatchFilter();
        }

        private void SaveBatchButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            try
            {
                if (!ExpiryDatePicker.SelectedDate.HasValue)
                {
                    MessageBox.Show(
                        "يرجى تحديد تاريخ الصلاحية.",
                        "تنبيه",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    return;
                }

                double purchasePrice;
                double quantity;

                if (!double.TryParse(
                    PurchasePriceTextBox.Text.Trim(),
                    NumberStyles.Any,
                    CultureInfo.CurrentCulture,
                    out purchasePrice))
                {
                    MessageBox.Show(
                        "يرجى إدخال سعر شراء صحيح.",
                        "تنبيه",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    return;
                }

                if (!double.TryParse(
                    QuantityTextBox.Text.Trim(),
                    NumberStyles.Any,
                    CultureInfo.CurrentCulture,
                    out quantity))
                {
                    MessageBox.Show(
                        "يرجى إدخال كمية صحيحة.",
                        "تنبيه",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    return;
                }

                batchService.AddBatch(
                    currentUserId,
                    selectedProduct.Id,
                    BatchNumberTextBox.Text,
                    ExpiryDatePicker.SelectedDate.Value,
                    purchasePrice,
                    quantity);

                MessageBox.Show(
                    "تمت إضافة الدفعة بنجاح.",
                    "نجاح",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                BatchNumberTextBox.Clear();
                PurchasePriceTextBox.Clear();
                QuantityTextBox.Clear();

                ExpiryDatePicker.SelectedDate =
                    DateTime.Today.AddMonths(6);

                LoadBatches();
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
                MessageBox.Show(
                    "تعذر إضافة الدفعة:\n" + ex.Message,
                    "خطأ",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }


        private void AdjustStockButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            var selectedBatch =
                BatchesDataGrid.SelectedItem as BatchDisplayInfo;

            if (selectedBatch == null)
            {
                MessageBox.Show(
                    "يرجى تحديد التشغيلة التي تريد تسوية رصيدها.",
                    "تنبيه",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            Window dialog = new Window
            {
                Title = "تسوية رصيد المخزون",
                Width = 430,
                Height = 350,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize,
                FlowDirection = FlowDirection.RightToLeft,
                Background = System.Windows.Media.Brushes.White
            };

            StackPanel panel = new StackPanel
            {
                Margin = new Thickness(20)
            };

            panel.Children.Add(new TextBlock
            {
                Text = "الصنف: " + selectedProduct.Name,
                FontSize = 15,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 8)
            });

            panel.Children.Add(new TextBlock
            {
                Text = "رقم التشغيلة: " + selectedBatch.BatchNumber,
                Margin = new Thickness(0, 0, 0, 8)
            });

            panel.Children.Add(new TextBlock
            {
                Text = "الرصيد المسجل حاليًا: " +
                       selectedBatch.Quantity.ToString("N4"),
                Foreground = System.Windows.Media.Brushes.DarkBlue,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 12)
            });

            panel.Children.Add(new TextBlock
            {
                Text = "الرصيد الفعلي الجديد:"
            });

            TextBox quantityBox = new TextBox
            {
                Text = selectedBatch.Quantity.ToString(
                    "0.####",
                    CultureInfo.CurrentCulture),
                Height = 35,
                Padding = new Thickness(7),
                Margin = new Thickness(0, 5, 0, 12)
            };

            panel.Children.Add(quantityBox);

            panel.Children.Add(new TextBlock
            {
                Text = "سبب التسوية:"
            });

            TextBox reasonBox = new TextBox
            {
                Height = 55,
                Padding = new Thickness(7),
                Margin = new Thickness(0, 5, 0, 15),
                TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = true
            };

            panel.Children.Add(reasonBox);

            Button saveButton = new Button
            {
                Content = "حفظ التسوية",
                Height = 40,
                Background = System.Windows.Media.Brushes.DarkOrange,
                Foreground = System.Windows.Media.Brushes.White,
                FontWeight = FontWeights.Bold
            };

            saveButton.Click += (s, args) =>
            {
                double newQuantity;

                bool validQuantity =
                    double.TryParse(
                        quantityBox.Text.Trim(),
                        NumberStyles.Any,
                        CultureInfo.CurrentCulture,
                        out newQuantity) ||
                    double.TryParse(
                        quantityBox.Text.Trim(),
                        NumberStyles.Any,
                        CultureInfo.InvariantCulture,
                        out newQuantity);

                if (!validQuantity ||
                    double.IsNaN(newQuantity) ||
                    double.IsInfinity(newQuantity) ||
                    newQuantity < 0)
                {
                    MessageBox.Show(
                        "يرجى إدخال رصيد صحيح لا يقل عن صفر.");
                    return;
                }

                if (string.IsNullOrWhiteSpace(reasonBox.Text))
                {
                    MessageBox.Show("يرجى كتابة سبب التسوية.");
                    return;
                }

                try
                {
                    batchService.SetBatchActualQuantity(
                        currentUserId,
                        selectedProduct.Id,
                        selectedBatch.Id,
                        newQuantity,
                        reasonBox.Text.Trim());

                    dialog.DialogResult = true;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        "تعذر تنفيذ التسوية:\n" + ex.Message,
                        "خطأ",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            };

            panel.Children.Add(saveButton);
            dialog.Content = panel;

            if (dialog.ShowDialog() == true)
            {
                MessageBox.Show(
                    "تمت تسوية رصيد التشغيلة بنجاح.",
                    "نجاح",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                LoadBatches();
            }
        }

        private void RefreshButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            LoadBatches();
        }

        private void CloseButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            Close();
        }
    }

    public class BatchDisplayInfo
    {
        public int Id { get; set; }

        public string BatchNumber { get; set; }

        public DateTime ExpiryDate { get; set; }

        public double PurchasePrice { get; set; }

        public double Quantity { get; set; }

        public bool IsExpired
        {
            get
            {
                return ExpiryDate.Date < DateTime.Today;
            }
        }

        public bool IsNearExpiry
        {
            get
            {
                return Quantity > 0 &&
                       ExpiryDate.Date >= DateTime.Today &&
                       ExpiryDate.Date <= DateTime.Today.AddDays(90);
            }
        }

        public bool IsDepleted
        {
            get
            {
                return Quantity <= 0;
            }
        }
    }
}
