
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Ocore.desktop.Models;
using Ocore.desktop.Services;

namespace Ocore.desktop.Views
{
    public partial class ProductsManagementWindow : Window
    {
        private readonly int currentUserId;
        private readonly ProductService productService;

        private List<Product> allProducts;
        private bool isManager;

        public ProductsManagementWindow(int userId)
        {
            InitializeComponent();

            currentUserId = userId;
            productService = new ProductService();
            allProducts = new List<Product>();

            Loaded += ProductsManagementWindow_Loaded;

            SearchTextBox.TextChanged += SearchTextBox_TextChanged;

            ArchiveFilterComboBox.SelectionChanged +=
                ArchiveFilterComboBox_SelectionChanged;

            ProductsDataGrid.SelectionChanged +=
                ProductsDataGrid_SelectionChanged;

            AddProductButton.Click += AddProductButton_Click;
            EditProductButton.Click += EditProductButton_Click;
            ManageStockButton.Click += ManageStockButton_Click;
            SetDiscountLimitButton.Click += SetDiscountLimitButton_Click;
            ArchiveProductButton.Click += ArchiveProductButton_Click;
            DeleteProductButton.Click += DeleteProductButton_Click;
            RefreshButton.Click += RefreshButton_Click;
            CloseButton.Click += CloseButton_Click;

            EditProductButton.IsEnabled = false;
            ManageStockButton.IsEnabled = false;
            SetDiscountLimitButton.IsEnabled = false;
            ArchiveProductButton.IsEnabled = false;
            DeleteProductButton.IsEnabled = false;
        }

        private void ProductsManagementWindow_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            try
            {
                isManager = productService.IsManager(currentUserId);

                EditProductButton.Visibility =
                    isManager ? Visibility.Visible : Visibility.Collapsed;

                ArchiveProductButton.Visibility =
                    isManager ? Visibility.Visible : Visibility.Collapsed;

                DeleteProductButton.Visibility =
                    isManager ? Visibility.Visible : Visibility.Collapsed;

                SetDiscountLimitButton.Visibility =
                    isManager ? Visibility.Visible : Visibility.Collapsed;

                LoadProducts();
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
                    "حدث خطأ أثناء فتح إدارة الأصناف:\n" + ex.Message,
                    "خطأ",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                Close();
            }
        }

        private void LoadProducts()
        {
            try
            {
                allProducts =
                    productService.GetProductsForManagement(currentUserId);

                ApplySearchFilter();
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
                    "حدث خطأ أثناء تحميل الأصناف:\n" + ex.Message,
                    "خطأ",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void ApplySearchFilter()
        {
            string searchText = SearchTextBox.Text.Trim();

            int filterIndex = ArchiveFilterComboBox.SelectedIndex;

            IEnumerable<Product> filteredProducts = allProducts;

            if (filterIndex == 0)
            {
                filteredProducts = filteredProducts.Where(
                    product => !product.IsArchived);
            }
            else if (filterIndex == 1)
            {
                filteredProducts = filteredProducts.Where(
                    product => product.IsArchived);
            }

            if (!string.IsNullOrWhiteSpace(searchText))
            {
                filteredProducts = filteredProducts.Where(product =>
                    ContainsText(product.ProductCode, searchText) ||
                    ContainsText(product.Name, searchText) ||
                    ContainsText(product.Barcode, searchText) ||
                    ContainsText(product.UnitName, searchText) ||
                    ContainsText(product.SmallUnitName, searchText));
            }

            ProductsDataGrid.ItemsSource = filteredProducts.ToList();

            ProductsDataGrid.SelectedItem = null;

            UpdateActionButtons();
        }

        private bool ContainsText(string source, string search)
        {
            return !string.IsNullOrEmpty(source) &&
                   source.IndexOf(
                       search,
                       StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private Product GetSelectedProduct()
        {
            return ProductsDataGrid.SelectedItem as Product;
        }

        private void UpdateActionButtons()
        {
            Product selectedProduct = GetSelectedProduct();

            bool hasSelection = selectedProduct != null;

            ManageStockButton.IsEnabled =
                hasSelection && !selectedProduct.IsArchived;

            EditProductButton.IsEnabled =
                isManager && hasSelection;

            SetDiscountLimitButton.IsEnabled =
                isManager && hasSelection;

            ArchiveProductButton.IsEnabled =
                isManager && hasSelection &&
                !selectedProduct.IsArchived;

            DeleteProductButton.IsEnabled =
                isManager && hasSelection;
        }

        private void ProductsDataGrid_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            UpdateActionButtons();
        }

        private void SearchTextBox_TextChanged(
            object sender,
            TextChangedEventArgs e)
        {
            ApplySearchFilter();
        }

        private void ArchiveFilterComboBox_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (allProducts != null)
                ApplySearchFilter();
        }

        private void AddProductButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            try
            {
                var window = new AddProductWindow(currentUserId);

                window.Owner = this;

                bool? result = window.ShowDialog();

                if (result == true)
                    LoadProducts();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "حدث خطأ أثناء فتح شاشة إضافة الصنف:\n" + ex.Message,
                    "خطأ",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void EditProductButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (!isManager)
            {
                MessageBox.Show(
                    "تعديل بيانات الأصناف متاح للمدير فقط.",
                    "الصلاحيات",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            Product selectedProduct = GetSelectedProduct();

            if (selectedProduct == null)
            {
                MessageBox.Show(
                    "يرجى تحديد الصنف المراد تعديله.",
                    "تنبيه",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            try
            {
                var window = new AddProductWindow(
                    currentUserId,
                    selectedProduct);

                window.Owner = this;

                bool? result = window.ShowDialog();

                if (result == true)
                    LoadProducts();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "حدث خطأ أثناء فتح شاشة تعديل الصنف:\n" + ex.Message,
                    "خطأ",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void ManageStockButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            try
            {
                Product selectedProduct = GetSelectedProduct();

                if (selectedProduct == null)
                {
                    MessageBox.Show(
                        "يرجى تحديد صنف أولاً.",
                        "تنبيه",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    return;
                }

                if (selectedProduct.IsArchived)
                {
                    MessageBox.Show(
                        "لا يمكن إدارة رصيد صنف مؤرشف.",
                        "تنبيه",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    return;
                }

                var window = new ManageStockWindow(
                    currentUserId,
                    selectedProduct);

                window.Owner = this;
                window.ShowDialog();

                LoadProducts();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "حدث خطأ أثناء فتح إدارة التشغيلات:\n" + ex.Message,
                    "خطأ",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void SetDiscountLimitButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (!isManager)
            {
                MessageBox.Show(
                    "تحديد الحد الأقصى للخصم متاح للمدير فقط.",
                    "الصلاحيات",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            Product selectedProduct = GetSelectedProduct();

            if (selectedProduct == null)
            {
                MessageBox.Show(
                    "يرجى تحديد الصنف أولاً.",
                    "تنبيه",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            try
            {
                double currentLimit =
                    selectedProduct.MaxDiscountPercentage;

                Window dialog = new Window
                {
                    Title = "تحديد الحد الأقصى للخصم",
                    Width = 390,
                    SizeToContent = SizeToContent.Height,
                    ResizeMode = ResizeMode.NoResize,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Owner = this,
                    FlowDirection = FlowDirection.RightToLeft,
                    Background = Brushes.White,
                    ShowInTaskbar = false
                };

                Grid grid = new Grid
                {
                    Margin = new Thickness(18)
                };

                grid.RowDefinitions.Add(
                    new RowDefinition { Height = GridLength.Auto });

                grid.RowDefinitions.Add(
                    new RowDefinition { Height = GridLength.Auto });

                grid.RowDefinitions.Add(
                    new RowDefinition { Height = GridLength.Auto });

                grid.RowDefinitions.Add(
                    new RowDefinition { Height = GridLength.Auto });

                TextBlock productText = new TextBlock
                {
                    Text = "الصنف: " + selectedProduct.Name +
                           "\nالكود: " + selectedProduct.ProductCode,
                    FontSize = 14,
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, 0, 0, 14),
                    TextWrapping = TextWrapping.Wrap
                };

                Grid.SetRow(productText, 0);
                grid.Children.Add(productText);

                TextBlock instructionText = new TextBlock
                {
                    Text = "أقصى نسبة خصم مسموح بها (من 0 إلى 100):",
                    FontSize = 13,
                    Margin = new Thickness(0, 0, 0, 7)
                };

                Grid.SetRow(instructionText, 1);
                grid.Children.Add(instructionText);

                TextBox percentageTextBox = new TextBox
                {
                    Text = currentLimit.ToString(
                        "0.##",
                        CultureInfo.CurrentCulture),
                    FontSize = 16,
                    Height = 36,
                    Padding = new Thickness(8),
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 12)
                };

                Grid.SetRow(percentageTextBox, 2);
                grid.Children.Add(percentageTextBox);

                StackPanel buttonsPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Center
                };

                Button saveButton = new Button
                {
                    Content = "حفظ",
                    Width = 100,
                    Height = 36,
                    Margin = new Thickness(5),
                    Background = new SolidColorBrush(
                        Color.FromRgb(22, 134, 93)),
                    Foreground = Brushes.White,
                    FontWeight = FontWeights.Bold,
                    IsDefault = true
                };

                Button cancelButton = new Button
                {
                    Content = "إلغاء",
                    Width = 100,
                    Height = 36,
                    Margin = new Thickness(5),
                    IsCancel = true
                };

                saveButton.Click += (s, args) =>
                {
                    string input = percentageTextBox.Text.Trim();

                    double newLimit;

                    bool valid =
                        double.TryParse(
                            input,
                            NumberStyles.Float,
                            CultureInfo.CurrentCulture,
                            out newLimit) ||
                        double.TryParse(
                            input,
                            NumberStyles.Float,
                            CultureInfo.InvariantCulture,
                            out newLimit);

                    if (!valid ||
                        double.IsNaN(newLimit) ||
                        double.IsInfinity(newLimit) ||
                        newLimit < 0 ||
                        newLimit > 100)
                    {
                        MessageBox.Show(
                            "أدخل نسبة صحيحة من 0 إلى 100.",
                            "قيمة غير صحيحة",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);

                        percentageTextBox.Focus();
                        percentageTextBox.SelectAll();
                        return;
                    }

                    try
                    {
                        productService.SetProductMaxDiscountPercentage(
                            currentUserId,
                            selectedProduct.Id,
                            newLimit);

                        dialog.DialogResult = true;
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(
                            "تعذر حفظ حد الخصم:\n" + ex.Message,
                            "خطأ",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                    }
                };

                buttonsPanel.Children.Add(saveButton);
                buttonsPanel.Children.Add(cancelButton);

                Grid.SetRow(buttonsPanel, 3);
                grid.Children.Add(buttonsPanel);

                dialog.Content = grid;

                bool? result = dialog.ShowDialog();

                if (result == true)
                {
                    MessageBox.Show(
                        "تم تحديث الحد الأقصى للخصم بنجاح.",
                        "نجاح",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    LoadProducts();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "حدث خطأ أثناء تحديد حد الخصم:\n" + ex.Message,
                    "خطأ",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void ArchiveProductButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (!isManager)
                return;

            Product selectedProduct = GetSelectedProduct();

            if (selectedProduct == null)
            {
                MessageBox.Show("يرجى تحديد صنف أولاً.");
                return;
            }

            MessageBoxResult result = MessageBox.Show(
                "هل تريد أرشفة الصنف التالي؟\n\n" +
                selectedProduct.Name +
                "\nالكود: " + selectedProduct.ProductCode +
                "\n\nسيختفي الصنف من قوائم الأصناف النشطة، " +
                "مع الاحتفاظ بالفواتير والحركات السابقة.",
                "تأكيد أرشفة الصنف",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                productService.ArchiveProduct(
                    currentUserId,
                    selectedProduct.Id);

                MessageBox.Show(
                    "تمت أرشفة الصنف بنجاح.",
                    "نجاح",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                LoadProducts();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "تعذر أرشفة الصنف:\n" + ex.Message,
                    "خطأ",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void DeleteProductButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (!isManager)
                return;

            Product selectedProduct = GetSelectedProduct();

            if (selectedProduct == null)
            {
                MessageBox.Show("يرجى تحديد صنف أولاً.");
                return;
            }

            MessageBoxResult result = MessageBox.Show(
                "تنبيه: الحذف النهائي لا يمكن التراجع عنه.\n\n" +
                "الصنف: " + selectedProduct.Name +
                "\nالكود: " + selectedProduct.ProductCode +
                "\n\nلن يُسمح بالحذف إذا كان الصنف مرتبطًا " +
                "بتشغيلات أو فواتير أو مرتجعات.\n\n" +
                "هل تريد المتابعة؟",
                "تأكيد الحذف النهائي",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                productService.DeleteProduct(
                    currentUserId,
                    selectedProduct.Id);

                MessageBox.Show(
                    "تم حذف الصنف نهائيًا.",
                    "نجاح",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                LoadProducts();
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(
                    ex.Message +
                    "\n\nيمكنك أرشفة الصنف بدلًا من حذفه.",
                    "لا يمكن حذف الصنف",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "تعذر حذف الصنف:\n" + ex.Message,
                    "خطأ",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void RefreshButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            SearchTextBox.Clear();
            ArchiveFilterComboBox.SelectedIndex = 0;
            LoadProducts();
        }

        private void CloseButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            Close();
        }
    }
}
