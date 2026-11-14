
﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Ocore.desktop.Models;

namespace Ocore.desktop.Views
{
    public partial class BatchSelectionWindow : Window
    {
        private const double QuantityEpsilon = 0.000001;

        private readonly double requiredQuantity;

        private readonly ObservableCollection<SaleInvoiceBatchAllocation>
            availableBatchAllocations;

        private int previouslySelectedBatchId;

        public List<SaleInvoiceBatchAllocation> ConfirmedAllocations
        {
            get;
            private set;
        }

        public BatchSelectionWindow(
            SaleInvoiceItem invoiceItem,
            List<ProductBatch> availableBatches)
        {
            InitializeComponent();

            if (invoiceItem == null)
                throw new ArgumentNullException(nameof(invoiceItem));

            requiredQuantity = invoiceItem.PackageQuantity;

            availableBatchAllocations =
                new ObservableCollection<SaleInvoiceBatchAllocation>();

            ProductNameText.Text = invoiceItem.ItemName;

            RequiredQuantityText.Text =
                requiredQuantity.ToString("0.###");

            SummaryRequiredText.Text =
                requiredQuantity.ToString("0.###");

            previouslySelectedBatchId = 0;

            if (invoiceItem.BatchAllocations != null &&
                invoiceItem.BatchAllocations.Count == 1)
            {
                previouslySelectedBatchId =
                    invoiceItem.BatchAllocations[0].BatchId;
            }

            BuildAvailableBatches(availableBatches);

            BatchesDataGrid.ItemsSource =
                availableBatchAllocations;

            UpdateSummary();

            Loaded += BatchSelectionWindow_Loaded;
        }

        private void BatchSelectionWindow_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            if (previouslySelectedBatchId > 0)
            {
                SaleInvoiceBatchAllocation previous =
                    availableBatchAllocations.FirstOrDefault(
                        x => x.BatchId == previouslySelectedBatchId);

                if (previous != null)
                {
                    BatchesDataGrid.SelectedItem = previous;
                    BatchesDataGrid.ScrollIntoView(previous);
                    return;
                }
            }

            BatchesDataGrid.SelectedItem = null;
            UpdateSummary();
        }

        private void BuildAvailableBatches(
            List<ProductBatch> availableBatches)
        {
            HashSet<int> addedIds = new HashSet<int>();

            if (availableBatches == null)
                return;

            foreach (ProductBatch batch in availableBatches)
            {
                if (batch == null ||
                    batch.Id <= 0 ||
                    !addedIds.Add(batch.Id))
                    continue;

                availableBatchAllocations.Add(
                    new SaleInvoiceBatchAllocation
                    {
                        BatchId = batch.Id,
                        BatchNumber = batch.BatchNumber,
                        ExpiryDate = batch.ExpiryDate,
                        AvailableQuantity = batch.Quantity,
                        AllocatedQuantity = 0
                    });
            }
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) &&
                   !double.IsInfinity(value);
        }

        private static bool TryGetExpiryDate(
            string expiryDate,
            out DateTime parsedDate)
        {
            parsedDate = DateTime.MinValue;

            if (string.IsNullOrWhiteSpace(expiryDate))
                return false;

            return DateTime.TryParseExact(
                expiryDate.Trim(),
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out parsedDate);
        }

        private static bool IsExpiredOrInvalid(
            string expiryDate)
        {
            // عدم وجود تاريخ صلاحية يعني أن الصنف بلا صلاحية،
            // وبالتالي لا يمنع بيعه.
            if (string.IsNullOrWhiteSpace(expiryDate))
                return false;

            DateTime parsedDate;

            if (!TryGetExpiryDate(expiryDate, out parsedDate))
                return true;

            // البيع مسموح في يوم انتهاء الصلاحية نفسه.
            return parsedDate.Date < DateTime.Today;
        }

        private SaleInvoiceBatchAllocation GetSelectedBatch()
        {
            return BatchesDataGrid.SelectedItem
                as SaleInvoiceBatchAllocation;
        }

        private void UpdateSummary()
        {
            SaleInvoiceBatchAllocation selected =
                GetSelectedBatch();

            SummaryRequiredText.Text =
                requiredQuantity.ToString("0.###");

            if (selected == null)
            {
                SummaryAllocatedText.Text = "0";
                RemainingQuantityText.Text =
                    requiredQuantity.ToString("0.###");

                AllocationStatusText.Text =
                    "لم يتم اختيار تشغيلة";

                AllocationStatusText.Foreground =
                    Brushes.DarkOrange;

                return;
            }

            SummaryAllocatedText.Text =
                requiredQuantity.ToString("0.###");

            RemainingQuantityText.Text = "0";

            if (!IsFinite(requiredQuantity) ||
                requiredQuantity <= 0)
            {
                AllocationStatusText.Text =
                    "الكمية المطلوبة غير صحيحة";

                AllocationStatusText.Foreground =
                    Brushes.DarkRed;

                return;
            }

            if (!IsFinite(selected.AvailableQuantity) ||
                selected.AvailableQuantity < 0)
            {
                AllocationStatusText.Text =
                    "رصيد التشغيلة غير صحيح";

                AllocationStatusText.Foreground =
                    Brushes.DarkRed;

                return;
            }

            if (IsExpiredOrInvalid(selected.ExpiryDate))
            {
                AllocationStatusText.Text =
                    "التشغيلة منتهية أو تاريخها غير صحيح";

                AllocationStatusText.Foreground =
                    Brushes.DarkRed;

                return;
            }

            if (requiredQuantity >
                selected.AvailableQuantity + QuantityEpsilon)
            {
                AllocationStatusText.Text =
                    "الرصيد المتاح غير كافٍ";

                AllocationStatusText.Foreground =
                    Brushes.DarkRed;

                return;
            }

            AllocationStatusText.Text =
                "التشغيلة جاهزة للاختيار";

            AllocationStatusText.Foreground =
                Brushes.ForestGreen;
        }

        private bool ValidateSelectedBatch(
            SaleInvoiceBatchAllocation selected,
            out string errorMessage)
        {
            errorMessage = string.Empty;

            if (selected == null)
            {
                errorMessage =
                    "اختر تشغيلة واحدة من الجدول أولاً.";

                return false;
            }

            if (!IsFinite(requiredQuantity) ||
                requiredQuantity <= 0)
            {
                errorMessage =
                    "الكمية المطلوبة للصنف غير صحيحة.";

                return false;
            }

            if (selected.BatchId <= 0)
            {
                errorMessage =
                    "بيانات التشغيلة المختارة غير صحيحة.";

                return false;
            }

            if (!IsFinite(selected.AvailableQuantity) ||
                selected.AvailableQuantity < 0)
            {
                errorMessage =
                    "الرصيد المتاح للتشغيلة غير صحيح.";

                return false;
            }

            if (IsExpiredOrInvalid(selected.ExpiryDate))
            {
                errorMessage =
                    "لا يمكن البيع من تشغيلة منتهية الصلاحية " +
                    "أو تاريخ صلاحيتها غير صحيح.\n\n" +
                    "التشغيلة: " + selected.BatchNumber +
                    "\nالصلاحية: " + selected.ExpiryDate;

                return false;
            }

            if (requiredQuantity >
                selected.AvailableQuantity + QuantityEpsilon)
            {
                errorMessage =
                    "الكمية المطلوبة أكبر من رصيد التشغيلة.\n\n" +
                    "الكمية المطلوبة: " +
                    requiredQuantity.ToString("0.###") +
                    "\nالرصيد المتاح: " +
                    selected.AvailableQuantity.ToString("0.###");

                return false;
            }

            return true;
        }

        private void BatchesDataGrid_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            UpdateSummary();
        }

        private void ConfirmButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            SaleInvoiceBatchAllocation selected =
                GetSelectedBatch();

            string errorMessage;

            if (!ValidateSelectedBatch(
                selected,
                out errorMessage))
            {
                MessageBox.Show(
                    errorMessage,
                    "اختيار التشغيلة",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                UpdateSummary();
                return;
            }

            ConfirmedAllocations =
                new List<SaleInvoiceBatchAllocation>
                {
                    new SaleInvoiceBatchAllocation
                    {
                        BatchId = selected.BatchId,
                        BatchNumber = selected.BatchNumber,
                        ExpiryDate = selected.ExpiryDate,
                        AvailableQuantity =
                            selected.AvailableQuantity,
                        AllocatedQuantity = requiredQuantity
                    }
                };

            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            ConfirmedAllocations = null;

            DialogResult = false;
            Close();
        }

        private void Window_PreviewKeyDown(
            object sender,
            KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;

                ConfirmedAllocations = null;
                DialogResult = false;
                Close();

                return;
            }

            if (e.Key == Key.Enter)
            {
                e.Handled = true;

                ConfirmButton_Click(
                    sender,
                    new RoutedEventArgs(Button.ClickEvent));
            }
        }
    }
}
