using System;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Ocore.desktop.Services;

namespace Ocore.desktop.Views
{
    public sealed class InventoryAuditWindow : Window
    {
        private readonly int currentUserId;
        private readonly InventoryAuditService auditService;
        private readonly InventoryAuditQueryService queryService;
        private DataGrid sessionsGrid, itemsGrid, pendingGrid, eventsGrid;
        private TextBox reasonBox, pQuantityBox, uQuantityBox, noteBox, barcodeSearchBox;
        private DataView itemsView;
        private TextBlock progressText, selectedSessionText, pUnitLabel, uUnitLabel;
        private int selectedSessionId;

        public InventoryAuditWindow(int userId, string role)
        {
            if (!string.Equals(role, "Manager", StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("شاشة الجرد متاحة للمدير فقط.");

            currentUserId = userId;
            auditService = new InventoryAuditService();
            queryService = new InventoryAuditQueryService();
            Title = "إدارة الجرد ومراجعة الفروق";
            Width = 1320;
            Height = 850;
            MinWidth = 1050;
            MinHeight = 700;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            FlowDirection = FlowDirection.RightToLeft;
            Background = new SolidColorBrush(Color.FromRgb(243, 246, 248));
            Content = BuildLayout();
            Loaded += (sender, args) => barcodeSearchBox.Focus();
            LoadSessions();
        }

        private UIElement BuildLayout()
        {
            var root = new Grid { Margin = new Thickness(9), Background = new SolidColorBrush(Color.FromRgb(241, 244, 246)) };
            for (int i = 0; i < 7; i++)
                root.RowDefinitions.Add(new RowDefinition { Height = i == 4 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });

            var primary = new SolidColorBrush(Color.FromRgb(23, 76, 67));
            var accent = new SolidColorBrush(Color.FromRgb(22, 134, 93));
            var border = new SolidColorBrush(Color.FromRgb(220, 229, 225));
            var muted = new SolidColorBrush(Color.FromRgb(113, 129, 124));

            var header = new Border
            {
                Background = primary,
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(11, 8, 11, 8),
                Margin = new Thickness(0, 0, 0, 6)
            };
            var headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var titleStack = new StackPanel();
            titleStack.Children.Add(new TextBlock { Text = "فاتورة جرد المخزون", FontSize = 18, FontWeight = FontWeights.Bold, Foreground = Brushes.White });
            titleStack.Children.Add(new TextBlock { Text = "إدخال الكميات الفعلية ومراجعة كل تشغيلة وتاريخ صلاحية", FontSize = 10, Foreground = new SolidColorBrush(Color.FromRgb(213, 231, 225)), Margin = new Thickness(0, 2, 0, 0) });
            Grid.SetColumn(titleStack, 0);
            headerGrid.Children.Add(titleStack);
            var brand = new TextBlock { Text = "O CORE", FontSize = 13, FontWeight = FontWeights.Bold, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 4, 0) };
            Grid.SetColumn(brand, 1);
            headerGrid.Children.Add(brand);
            header.Child = headerGrid;
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            var sessionCard = new Border { Background = Brushes.White, BorderBrush = border, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(6), Margin = new Thickness(0, 0, 0, 5) };
            var sessionLayout = new Grid();
            sessionLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star) });
            sessionLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2.2, GridUnitType.Star) });
            sessionLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var sessionInfo = new StackPanel { Margin = new Thickness(3, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            sessionInfo.Children.Add(new TextBlock { Text = "بيانات جلسة الجرد", FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = muted });
            selectedSessionText = new TextBlock { Text = "اختر جلسة جرد أو ابدأ جلسة جديدة", FontSize = 12, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(38, 58, 54)), Margin = new Thickness(0, 3, 0, 0), TextWrapping = TextWrapping.Wrap };
            sessionInfo.Children.Add(selectedSessionText);
            Grid.SetColumn(sessionInfo, 0);
            sessionLayout.Children.Add(sessionInfo);
            progressText = new TextBlock { Text = "التقدم: —", FontSize = 12, FontWeight = FontWeights.Bold, Foreground = accent, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(5) };
            Grid.SetColumn(progressText, 1);
            sessionLayout.Children.Add(progressText);
            reasonBox = new TextBox { Width = 175, Height = 29, Margin = new Thickness(3), Padding = new Thickness(7, 2, 7, 2), FontSize = 11, VerticalContentAlignment = VerticalAlignment.Center, ToolTip = "سبب بدء الجرد أو سبب إلغاء الجلسة" };
            Grid.SetColumn(reasonBox, 2);
            sessionLayout.Children.Add(reasonBox);
            sessionCard.Child = sessionLayout;
            Grid.SetRow(sessionCard, 1);
            root.Children.Add(sessionCard);

            var sessionActions = new WrapPanel { Margin = new Thickness(0, 0, 0, 5) };
            sessionActions.Children.Add(MakeButton("بدء جرد شامل", CreateFullSession));
            sessionActions.Children.Add(MakeButton("بدء جرد محدد", CreateSelectedSession));
            sessionActions.Children.Add(MakeButton("تحديث", RefreshAll));
            sessionActions.Children.Add(MakeButton("تقارير الجرد", OpenInventoryReports));
            sessionActions.Children.Add(MakeButton("إعدادات التتبع", OpenTrackingSettings));
            Grid.SetRow(sessionActions, 2);
            root.Children.Add(sessionActions);

            sessionsGrid = CreateGrid();
            sessionsGrid.Height = 68;
            sessionsGrid.MinHeight = 54;
            sessionsGrid.MaxHeight = 78;
            sessionsGrid.Margin = new Thickness(0);
            sessionsGrid.SelectionChanged += SessionsGrid_SelectionChanged;
            sessionsGrid.Background = Brushes.White;
            sessionsGrid.AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(246, 249, 247));
            sessionsGrid.BorderBrush = border;
            sessionsGrid.BorderThickness = new Thickness(1);
            var sessionsCard = new Border { Background = Brushes.White, BorderBrush = border, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), Padding = new Thickness(4), Margin = new Thickness(0, 0, 0, 5), Child = sessionsGrid };
            Grid.SetRow(sessionsCard, 3);
            root.Children.Add(sessionsCard);

            var invoiceBody = new Grid();
            invoiceBody.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            invoiceBody.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            invoiceBody.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var entryCard = new Border { Background = Brushes.White, BorderBrush = border, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), Padding = new Thickness(6), Margin = new Thickness(0, 0, 0, 5) };
            var entryGrid = new Grid();
            entryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.7, GridUnitType.Star) });
            entryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            entryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(85) });
            entryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            entryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(85) });
            entryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            entryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            entryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            barcodeSearchBox = new TextBox { Height = 32, Margin = new Thickness(2), Padding = new Thickness(8, 4, 8, 4), FontSize = 12, VerticalContentAlignment = VerticalAlignment.Center, ToolTip = "ابحث باسم الصنف أو الكود أو الباركود" };
            barcodeSearchBox.TextChanged += BarcodeSearchBox_TextChanged;
            barcodeSearchBox.KeyDown += (sender, args) =>
            {
                if (args.Key == System.Windows.Input.Key.Enter && itemsGrid != null && itemsGrid.Items.Count > 0)
                {
                    itemsGrid.SelectedIndex = 0;
                    pQuantityBox.Focus();
                    pQuantityBox.SelectAll();
                    args.Handled = true;
                }
            };
            Grid.SetColumn(barcodeSearchBox, 0);
            entryGrid.Children.Add(barcodeSearchBox);

            pUnitLabel = new TextBlock { Text = "P", FontSize = 11, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(7, 0, 2, 0) };
            Grid.SetColumn(pUnitLabel, 1);
            entryGrid.Children.Add(pUnitLabel);
            pQuantityBox = new TextBox { Height = 32, Margin = new Thickness(2), Padding = new Thickness(6, 3, 6, 3), FontSize = 13, TextAlignment = TextAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, ToolTip = "العدد الفعلي للوحدة الأساسية P" };
            pQuantityBox.KeyDown += (sender, args) =>
            {
                if (args.Key == System.Windows.Input.Key.Enter)
                {
                    uQuantityBox.Focus();
                    uQuantityBox.SelectAll();
                    args.Handled = true;
                }
            };
            Grid.SetColumn(pQuantityBox, 2);
            entryGrid.Children.Add(pQuantityBox);

            uUnitLabel = new TextBlock { Text = "U", FontSize = 11, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(7, 0, 2, 0) };
            Grid.SetColumn(uUnitLabel, 3);
            entryGrid.Children.Add(uUnitLabel);
            uQuantityBox = new TextBox { Height = 32, Margin = new Thickness(2), Padding = new Thickness(6, 3, 6, 3), FontSize = 13, TextAlignment = TextAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, ToolTip = "العدد الفعلي للوحدات الصغيرة U" };
            uQuantityBox.KeyDown += (sender, args) =>
            {
                if (args.Key == System.Windows.Input.Key.Enter)
                {
                    SaveSelectedCount();
                    args.Handled = true;
                }
            };
            Grid.SetColumn(uQuantityBox, 4);
            entryGrid.Children.Add(uQuantityBox);

            var saveButton = MakeButton("حفظ العد", SaveSelectedCount);
            Grid.SetColumn(saveButton, 5);
            entryGrid.Children.Add(saveButton);
            var addButton = MakeButton("إضافة أصناف", AddItemsToOpenSession);
            Grid.SetColumn(addButton, 6);
            entryGrid.Children.Add(addButton);
            var clearSearchButton = MakeButton("مسح البحث", () => { barcodeSearchBox.Clear(); barcodeSearchBox.Focus(); });
            Grid.SetColumn(clearSearchButton, 7);
            entryGrid.Children.Add(clearSearchButton);
            entryCard.Child = entryGrid;
            Grid.SetRow(entryCard, 0);
            invoiceBody.Children.Add(entryCard);

            itemsGrid = CreateGrid();
            itemsGrid.SelectionMode = DataGridSelectionMode.Extended;
            itemsGrid.SelectionChanged += ItemsGrid_SelectionChanged;
            itemsGrid.MinHeight = 180;
            itemsGrid.RowHeight = 34;
            itemsGrid.FontSize = 11;
            itemsGrid.Background = Brushes.White;
            itemsGrid.Foreground = new SolidColorBrush(Color.FromRgb(38, 58, 54));
            itemsGrid.BorderBrush = new SolidColorBrush(Color.FromRgb(200, 213, 207));
            itemsGrid.BorderThickness = new Thickness(1);
            itemsGrid.HorizontalGridLinesBrush = border;
            itemsGrid.VerticalGridLinesBrush = border;
            itemsGrid.AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(246, 249, 247));
            itemsGrid.GridLinesVisibility = DataGridGridLinesVisibility.All;
            var itemsPanel = new DockPanel();
            var itemsTitle = new TextBlock { Text = "أصناف فاتورة الجرد — كل تشغيلة / تاريخ صلاحية في صف مستقل", FontSize = 12, FontWeight = FontWeights.Bold, Foreground = primary, Margin = new Thickness(3, 2, 3, 5) };
            DockPanel.SetDock(itemsTitle, Dock.Top);
            itemsPanel.Children.Add(itemsTitle);
            itemsPanel.Children.Add(itemsGrid);
            Grid.SetRow(itemsPanel, 1);
            invoiceBody.Children.Add(itemsPanel);

            var reviewCard = new Border { Background = Brushes.White, BorderBrush = border, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), Padding = new Thickness(5), Margin = new Thickness(0, 5, 0, 0) };
            var reviewActions = new WrapPanel();
            reviewActions.Children.Add(MakeButton("إرسال للمراجعة", SubmitSelectedSession));
            reviewActions.Children.Add(MakeButton("اعتماد البند", ApproveSelectedItem));
            reviewActions.Children.Add(MakeButton("اعتماد المحدد", ApproveSelectedItems));
            reviewActions.Children.Add(MakeButton("إلغاء البنود", CancelSelectedItems));
            reviewActions.Children.Add(MakeButton("طلب إعادة عد", RequestSelectedRecount));
            reviewActions.Children.Add(MakeButton("إعادة مراجعة", ReopenSelectedItems));
            reviewActions.Children.Add(MakeButton("إلغاء المراجعة", CancelReopenedSelectedItems));
            reviewActions.Children.Add(MakeButton("إغلاق الجلسة", CloseSelectedSession));
            reviewActions.Children.Add(MakeButton("إلغاء الجلسة", CancelSelectedSession));
            reviewActions.Children.Add(MakeButton("معالجة الحركات", RetryPending));
            reviewCard.Child = reviewActions;
            Grid.SetRow(reviewCard, 2);
            invoiceBody.Children.Add(reviewCard);

            Grid.SetRow(invoiceBody, 4);
            root.Children.Add(invoiceBody);

            var activityTabs = new TabControl { Height = 105, Margin = new Thickness(0, 5, 0, 0), FontSize = 10 };
            var pendingTab = new TabItem { Header = "الحركات المعلقة" };
            pendingGrid = CreateGrid();
            pendingTab.Content = pendingGrid;
            activityTabs.Items.Add(pendingTab);
            var eventsTab = new TabItem { Header = "سجل الأحداث" };
            eventsGrid = CreateGrid();
            eventsTab.Content = eventsGrid;
            activityTabs.Items.Add(eventsTab);
            Grid.SetRow(activityTabs, 5);
            root.Children.Add(activityTabs);

            noteBox = new TextBox { Height = 30, MinWidth = 260, Margin = new Thickness(0, 5, 5, 0), Padding = new Thickness(7, 3, 7, 3), FontSize = 11, VerticalContentAlignment = VerticalAlignment.Center, ToolTip = "ملاحظة المدير أو سبب الفرق أو إعادة العد أو الإلغاء" };
            var footer = new DockPanel { Margin = new Thickness(0, 1, 0, 0) };
            footer.Children.Add(noteBox);
            var closeButton = MakeButton("إغلاق الشاشة", () => Close());
            DockPanel.SetDock(closeButton, Dock.Left);
            footer.Children.Add(closeButton);
            Grid.SetRow(footer, 6);
            root.Children.Add(footer);
            return root;
        }

        private static DataGrid CreateGrid()
        {
            var grid = new DataGrid
            {
                AutoGenerateColumns = true,
                IsReadOnly = true,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                SelectionUnit = DataGridSelectionUnit.FullRow,
                SelectionMode = DataGridSelectionMode.Single,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                RowHeight = 31,
                FontSize = 12,
                Margin = new Thickness(0, 5, 0, 0)
            };
            grid.AutoGeneratingColumn += (sender, args) =>
            {
                string name = args.PropertyName;
                if (name == "PUnitName" || name == "UUnitName" || name == "UnitsPerPackageSnapshot")
                {
                    args.Cancel = true;
                    return;
                }
                if (name == "Id" || name == "ProductId" || name == "BatchId" ||
                    name == "TargetType" || name == "TargetKey" || name == "AssignedUserId" ||
                    name == "CountedBy" || name == "PurchasePriceSnapshot" ||
                    name == "SourceRecordId" || name == "SessionId" || name == "EmployeeUserId")
                {
                    args.Cancel = true;
                    return;
                }
                if (name == "Unit" || name == "UnitSnapshot") args.Column.Header = "الوحدات (P/U)";
                else if (name == "BookQuantity") args.Column.Header = "الرصيد الدفتري (مكافئ P)";
                else if (name == "ActualQuantity") args.Column.Header = "الفعلي (مكافئ P)";
                else if (name == "ActualPQuantity") args.Column.Header = "الفعلي P";
                else if (name == "ActualUQuantity") args.Column.Header = "الفعلي U";
                else if (name == "VarianceQuantity") args.Column.Header = "الفرق (مكافئ P)";
                else if (name == "ItemStatus") args.Column.Header = "حالة البند";
                else if (name == "BatchNumber") args.Column.Header = "رقم التشغيلة";
                else if (name == "ExpiryDate") args.Column.Header = "الصلاحية";
                else if (name == "ProductName") args.Column.Header = "الصنف";
                else if (name == "ProductCode") args.Column.Header = "كود الصنف";
                else if (name == "PUnitName") args.Column.Header = "اسم الوحدة P";
                else if (name == "UUnitName") args.Column.Header = "اسم الوحدة U";
                else if (name == "UnitsPerPackageSnapshot" || name == "UnitsPerPackage") args.Column.Header = "معامل التحويل";
            };
            return grid;
        }

        private static Border MakePanel(string title, UIElement child)
        {
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(24, 63, 54))
            });
            panel.Children.Add(child);
            return new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(220, 228, 225)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(9),
                Child = panel
            };
        }

        private static Button MakeButton(string caption, Action action)
        {
            var button = new Button
            {
                Content = caption,
                Height = 34,
                MinWidth = 105,
                Margin = new Thickness(3),
                Padding = new Thickness(9, 4, 9, 4),
                Background = new SolidColorBrush(Color.FromRgb(31, 105, 85)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                FontWeight = FontWeights.SemiBold
            };
            button.Click += (sender, args) => action();
            return button;
        }

        private void BarcodeSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyItemFilter();
        }

        private void ApplyItemFilter()
        {
            if (itemsView == null) return;
            string value = barcodeSearchBox == null ? "" : barcodeSearchBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                itemsView.RowFilter = "";
                return;
            }
            string escaped = value.Replace("'", "''");
            itemsView.RowFilter = "ProductCode LIKE '%" + escaped + "%' OR Barcode LIKE '%" + escaped + "%' OR ProductName LIKE '%" + escaped + "%'";
        }

        private void LoadSessions(int preferredId = 0)
        {
            sessionsGrid.ItemsSource = queryService.GetSessions(currentUserId, true).DefaultView;
            if (sessionsGrid.Items.Count == 0)
            {
                selectedSessionId = 0;
                itemsGrid.ItemsSource = null;
                pendingGrid.ItemsSource = null;
                eventsGrid.ItemsSource = null;
                itemsView = null;
                selectedSessionText.Text = "لا توجد جلسات جرد حتى الآن";
                progressText.Text = "التقدم: —";
                return;
            }

            DataRowView selected = null;
            foreach (var value in sessionsGrid.Items)
            {
                var row = value as DataRowView;
                if (row != null && Convert.ToInt32(row["Id"]) ==
                    (preferredId > 0 ? preferredId : selectedSessionId))
                {
                    selected = row;
                    break;
                }
            }
            sessionsGrid.SelectedItem = selected ?? sessionsGrid.Items[0];
            if (sessionsGrid.SelectedItem != null) LoadSelectedSession();
        }

        private void SessionsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            LoadSelectedSession();
        }

        private void ItemsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateUnitLabels();
        }

        private void UpdateUnitLabels()
        {
            var item = itemsGrid == null ? null : itemsGrid.SelectedItem as DataRowView;
            if (item == null)
            {
                pUnitLabel.Text = "P";
                uUnitLabel.Text = "U";
                return;
            }
            string pName = item.Row.Table.Columns.Contains("PUnitName") ? Convert.ToString(item["PUnitName"]) : "";
            string uName = item.Row.Table.Columns.Contains("UUnitName") ? Convert.ToString(item["UUnitName"]) : "";
            pUnitLabel.Text = string.IsNullOrWhiteSpace(pName) ? "P" : pName + " (P)";
            uUnitLabel.Text = string.IsNullOrWhiteSpace(uName) ? "U" : uName + " (U)";
        }

        private void LoadSelectedSession()
        {
            var row = sessionsGrid.SelectedItem as DataRowView;
            if (row == null) return;
            selectedSessionId = Convert.ToInt32(row["Id"]);
            selectedSessionText.Text = "الجلسة: " + row["SessionNumber"] +
                " | الحالة: " + row["Status"] + " | السبب: " + row["Reason"];
            itemsView = queryService.GetItems(selectedSessionId, currentUserId).DefaultView;
            itemsGrid.ItemsSource = itemsView;
            ApplyItemFilter();
            pendingGrid.ItemsSource = queryService.GetPendingMovements(selectedSessionId, currentUserId).DefaultView;
            eventsGrid.ItemsSource = queryService.GetSessionEvents(selectedSessionId, currentUserId).DefaultView;
            int total = Convert.ToInt32(row["TotalItems"]);
            int counted = Convert.ToInt32(row["CountedItems"]);
            int approved = Convert.ToInt32(row["ApprovedItems"]);
            int pending = Convert.ToInt32(row["PendingMovements"]);
            int failed = Convert.ToInt32(row["FailedMovements"]);
            double percent = total == 0 ? 0 : counted * 100.0 / total;
            progressText.Text = string.Format(CultureInfo.CurrentCulture,
                "الإنجاز: {0:0.0}% | تم عدها: {1}/{2} | معتمد: {3} | حركات معلقة: {4} | فاشلة: {5}",
                percent, counted, total, approved, pending, failed);
            UpdateUnitLabels();
        }

        private void RefreshAll() { LoadSessions(selectedSessionId); }

        private void CreateFullSession()
        {
            if (string.IsNullOrWhiteSpace(reasonBox.Text))
            {
                Warn("اكتب سبب بدء الجرد أولًا.");
                return;
            }
            RunAction(() =>
            {
                int id = auditService.CreateSession(currentUserId, reasonBox.Text.Trim());
                reasonBox.Clear();
                LoadSessions(id);
                Info("تم إنشاء جلسة الجرد الشامل. أدخل العدد الفعلي لكل تشغيلة بوحدتي P وU.");
            });
        }

        private void CreateSelectedSession()
        {
            if (string.IsNullOrWhiteSpace(reasonBox.Text))
            {
                Warn("اكتب سبب بدء الجرد أولًا.");
                return;
            }
            var picker = new Window
            {
                Title = "اختيار التشغيلات للجرد المحدد",
                Width = 950,
                Height = 600,
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                FlowDirection = FlowDirection.RightToLeft,
                Background = Brushes.White
            };
            var layout = new DockPanel { Margin = new Thickness(12) };
            var choose = CreateGrid();
            choose.SelectionMode = DataGridSelectionMode.Extended;
            choose.ItemsSource = queryService.GetAvailableBatches(currentUserId).DefaultView;
            DockPanel.SetDock(choose, Dock.Top);
            layout.Children.Add(choose);
            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            buttons.Children.Add(MakeButton("إنشاء الجرد المحدد", () =>
            {
                var ids = choose.SelectedItems.Cast<DataRowView>()
                    .Select(x => Convert.ToInt32(x["BatchId"])).Distinct().ToList();
                if (ids.Count == 0)
                {
                    Warn("حدد تشغيلة واحدة على الأقل.");
                    return;
                }
                try
                {
                    int id = auditService.CreateSession(currentUserId, reasonBox.Text.Trim(), ids);
                    reasonBox.Clear();
                    picker.DialogResult = true;
                    LoadSessions(id);
                }
                catch (Exception ex) { Error(ex.Message); }
            }));
            buttons.Children.Add(MakeButton("إلغاء", () => picker.DialogResult = false));
            DockPanel.SetDock(buttons, Dock.Bottom);
            layout.Children.Add(buttons);
            picker.Content = layout;
            picker.ShowDialog();
        }


        private void AddItemsToOpenSession()
        {
            var session = sessionsGrid.SelectedItem as DataRowView;
            if (session == null || selectedSessionId <= 0)
            {
                Warn("اختر جلسة جرد مفتوحة أولًا.");
                return;
            }
            if (!string.Equals(Convert.ToString(session["Status"]), "Open", StringComparison.OrdinalIgnoreCase))
            {
                Warn("إضافة أصناف جديدة متاحة فقط قبل إرسال الجلسة للمراجعة.");
                return;
            }

            var available = queryService.GetAvailableBatches(currentUserId);
            var view = available.DefaultView;
            var picker = new Window
            {
                Title = "إضافة أصناف وتشغيلات إلى الجرد الحالي",
                Width = 1050,
                Height = 650,
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                FlowDirection = FlowDirection.RightToLeft,
                Background = Brushes.White
            };

            var layout = new DockPanel { Margin = new Thickness(12) };
            var search = new TextBox
            {
                Height = 36,
                Margin = new Thickness(0, 0, 0, 8),
                Padding = new Thickness(8),
                FontSize = 14,
                ToolTip = "ابحث باسم الصنف أو الكود أو الباركود أو رقم التشغيلة"
            };
            search.TextChanged += (sender, args) =>
            {
                string term = search.Text.Trim().Replace("'", "''");
                if (string.IsNullOrWhiteSpace(term))
                    view.RowFilter = "";
                else
                    view.RowFilter =
                        "ProductName LIKE '%" + term + "%' OR " +
                        "ProductCode LIKE '%" + term + "%' OR " +
                        "Barcode LIKE '%" + term + "%' OR " +
                        "BatchNumber LIKE '%" + term + "%' OR " +
                        "ExpiryDate LIKE '%" + term + "%'";
            };
            DockPanel.SetDock(search, Dock.Top);
            layout.Children.Add(search);

            var choose = CreateGrid();
            choose.SelectionMode = DataGridSelectionMode.Extended;
            choose.ItemsSource = view;
            DockPanel.SetDock(choose, Dock.Top);
            layout.Children.Add(choose);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 8, 0, 0)
            };
            buttons.Children.Add(MakeButton("إضافة المحدد للجلسة", () =>
            {
                var ids = choose.SelectedItems.Cast<object>()
                    .OfType<DataRowView>()
                    .Select(x => Convert.ToInt32(x["BatchId"]))
                    .Where(x => x > 0)
                    .Distinct()
                    .ToList();

                if (ids.Count == 0)
                {
                    Warn("حدد صنفًا أو تشغيلة واحدة على الأقل.");
                    return;
                }

                try
                {
                    int added = auditService.AddTargetsToOpenSession(
                        currentUserId, selectedSessionId, ids);
                    LoadSessions(selectedSessionId);
                    SelectNextUncountedItem(0);
                    progressText.Text += " | تمت إضافة " + added + " هدفًا";
                    picker.DialogResult = true;
                }
                catch (UnauthorizedAccessException ex) { Warn(ex.Message); }
                catch (Exception ex) { Error(ex.Message); }
            }));
            buttons.Children.Add(MakeButton("إلغاء", () => picker.DialogResult = false));
            DockPanel.SetDock(buttons, Dock.Bottom);
            layout.Children.Add(buttons);

            picker.Content = layout;
            picker.ShowDialog();
        }

        private void SaveSelectedCount()
        {
            var item = itemsGrid.SelectedItem as DataRowView;
            if (item == null)
            {
                Warn("حدد تشغيلة من جدول تفاصيل الجرد أولًا.");
                return;
            }
            int actualP, actualU;
            if (!int.TryParse(pQuantityBox.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out actualP) ||
                !int.TryParse(uQuantityBox.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out actualU))
            {
                Warn("أدخل عددًا صحيحًا غير سالب في حقلي P وU.");
                return;
            }
            if (actualP < 0 || actualU < 0)
            {
                Warn("الكميات لا يمكن أن تكون سالبة.");
                return;
            }
            int factor = Convert.ToInt32(item["UnitsPerPackageSnapshot"]);
            if (factor <= 1 && actualU != 0)
            {
                Warn("هذا الصنف لا يحتوي على وحدات U منفصلة؛ أدخل صفرًا في خانة U.");
                return;
            }
            if (factor > 1 && actualU >= factor)
            {
                Warn("عدد وحدات U يجب أن يكون أقل من معامل التحويل (" + factor + ").");
                return;
            }
            RunAction(() =>
            {
                int savedItemId = Convert.ToInt32(item["Id"]);
                auditService.SaveCount(currentUserId, selectedSessionId,
                    savedItemId, actualP, actualU);
                pQuantityBox.Clear();
                uQuantityBox.Clear();
                LoadSessions(selectedSessionId);
                SelectNextUncountedItem(savedItemId);
                barcodeSearchBox.Clear();
            });
        }

        private void SelectNextUncountedItem(int previousItemId)
        {
            foreach (var value in itemsGrid.Items)
            {
                var row = value as DataRowView;
                if (row == null) continue;
                string status = Convert.ToString(row["ItemStatus"]);
                int id = Convert.ToInt32(row["Id"]);
                if (id != previousItemId && (status == "Pending" || status == "RecountRequired"))
                {
                    itemsGrid.SelectedItem = row;
                    itemsGrid.ScrollIntoView(row);
                    pQuantityBox.Focus();
                    pQuantityBox.SelectAll();
                    return;
                }
            }
            pQuantityBox.Focus();
            Info("تم حفظ العد. لا توجد بنود أخرى غير معدودة.");
        }

        private void SubmitSelectedSession()
        {
            if (selectedSessionId <= 0) return;
            RunAction(() =>
            {
                auditService.SubmitSession(currentUserId, selectedSessionId);
                LoadSessions(selectedSessionId);
                Info("تم إرسال الجلسة للمراجعة.");
            });
        }

        private void ApproveSelectedItem()
        {
            var item = itemsGrid.SelectedItem as DataRowView;
            if (item == null) { Warn("حدد بند الجرد للمراجعة."); return; }
            RunAction(() =>
            {
                auditService.ApproveItem(currentUserId, selectedSessionId,
                    Convert.ToInt32(item["Id"]), noteBox.Text.Trim());
                noteBox.Clear();
                LoadSessions(selectedSessionId);
            });
        }

        private void ApproveSelectedItems()
        {
            var selected = itemsGrid.SelectedItems.Cast<object>().OfType<DataRowView>().ToList();
            if (selected.Count == 0) { Warn("حدد بندًا واحدًا على الأقل للاعتماد الجماعي."); return; }
            if (MessageBox.Show("سيتم اعتماد " + selected.Count +
                " بندًا في معاملة واحدة. إذا تعذر اعتماد أي بند فلن يتم اعتماد أي بند. هل تريد المتابعة؟",
                "تأكيد الاعتماد الجماعي", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;
            RunAction(() =>
            {
                auditService.ApproveItems(currentUserId, selectedSessionId,
                    selected.Select(x => Convert.ToInt32(x["Id"])), noteBox.Text.Trim());
                noteBox.Clear();
                LoadSessions(selectedSessionId);
                Info("تم اعتماد البنود المحددة.");
            });
        }

        private void CancelSelectedItems()
        {
            var selected = itemsGrid.SelectedItems.Cast<object>().OfType<DataRowView>().ToList();
            if (selected.Count == 0 || string.IsNullOrWhiteSpace(noteBox.Text))
            {
                Warn("حدد البنود المطلوب إلغاؤها واكتب سبب الإلغاء.");
                return;
            }
            if (MessageBox.Show("سيتم إلغاء البنود المحددة وتحرير التجميد المرتبط بها. هل تريد المتابعة؟",
                "تأكيد إلغاء البنود", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;
            RunAction(() =>
            {
                auditService.CancelItems(currentUserId, selectedSessionId,
                    selected.Select(x => Convert.ToInt32(x["Id"])), noteBox.Text.Trim());
                noteBox.Clear();
                LoadSessions(selectedSessionId);
            });
        }

        private void RequestSelectedRecount()
        {
            var item = itemsGrid.SelectedItem as DataRowView;
            if (item == null || string.IsNullOrWhiteSpace(noteBox.Text))
            {
                Warn("حدد بندًا واكتب سبب طلب إعادة العد.");
                return;
            }
            RunAction(() =>
            {
                auditService.RequestRecount(currentUserId, selectedSessionId,
                    Convert.ToInt32(item["Id"]), noteBox.Text.Trim());
                noteBox.Clear();
                LoadSelectedSession();
            });
        }

        private void ReopenSelectedItems()
        {
            var selected = itemsGrid.SelectedItems.Cast<object>().OfType<DataRowView>().ToList();
            if (selected.Count == 0 || string.IsNullOrWhiteSpace(noteBox.Text))
            {
                Warn("حدد البنود المعتمدة واكتب سبب إعادة المراجعة.");
                return;
            }
            RunAction(() =>
            {
                auditService.ReopenSelectedItems(currentUserId, selectedSessionId,
                    selected.Select(x => Convert.ToInt32(x["Id"])), noteBox.Text.Trim());
                noteBox.Clear();
                LoadSessions(selectedSessionId);
            });
        }

        private void CancelReopenedSelectedItems()
        {
            var selected = itemsGrid.SelectedItems.Cast<object>().OfType<DataRowView>().ToList();
            if (selected.Count == 0 || string.IsNullOrWhiteSpace(noteBox.Text))
            {
                Warn("حدد البنود التي أُعيدت مراجعتها واكتب سبب إلغاء المراجعة.");
                return;
            }

            if (MessageBox.Show(
                "سيتم إرجاع البنود المحددة إلى حالة الاعتماد السابقة، مع الإبقاء على تسوية المخزون القديمة دون عكسها أو تكرارها. هل تريد المتابعة؟",
                "تأكيد إلغاء المراجعة",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            RunAction(() =>
            {
                auditService.CancelReopenedItems(
                    currentUserId,
                    selectedSessionId,
                    selected.Select(x => Convert.ToInt32(x["Id"])),
                    noteBox.Text.Trim());

                noteBox.Clear();
                LoadSessions(selectedSessionId);
                Info("تم إلغاء المراجعة والعودة للاعتماد السابق دون عكس تسوية المخزون.");
            });
        }

        private void CloseSelectedSession()
        {
            if (selectedSessionId <= 0) return;
            if (MessageBox.Show("هل تريد إغلاق جلسة الجرد المحددة؟", "تأكيد الإغلاق",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            RunAction(() =>
            {
                auditService.CloseSession(currentUserId, selectedSessionId);
                LoadSessions(selectedSessionId);
                Info("تم إغلاق الجلسة.");
            });
        }

        private void CancelSelectedSession()
        {
            if (selectedSessionId <= 0 || string.IsNullOrWhiteSpace(reasonBox.Text))
            {
                Warn("اكتب سبب إلغاء الجلسة أولًا.");
                return;
            }
            if (MessageBox.Show("سيتم إلغاء الجلسة ورفع التجميد المرتبط بها. هل تريد المتابعة؟",
                "تأكيد الإلغاء", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            RunAction(() =>
            {
                auditService.CancelSession(currentUserId, selectedSessionId, reasonBox.Text.Trim());
                reasonBox.Clear();
                LoadSessions(selectedSessionId);
            });
        }

        private void RetryPending()
        {
            if (selectedSessionId <= 0) return;
            RunAction(() =>
            {
                auditService.RetryFailedPendingMovements(currentUserId, selectedSessionId);
                LoadSessions(selectedSessionId);
            });
        }

        private void OpenInventoryReports()
        {
            if (selectedSessionId <= 0) { Warn("اختر جلسة جرد أولًا لعرض تقاريرها."); return; }
            try
            {
                var window = new InventoryAuditReportsWindow(selectedSessionId, currentUserId);
                window.Owner = this;
                window.ShowDialog();
            }
            catch (UnauthorizedAccessException ex) { Warn(ex.Message); }
            catch (Exception ex) { Error(ex.Message); }
        }

        private void OpenTrackingSettings()
        {
            var window = new InventoryProductTrackingWindow(currentUserId);
            window.Owner = this;
            window.ShowDialog();
        }

        private void RunAction(Action action)
        {
            try { action(); }
            catch (UnauthorizedAccessException ex) { Warn(ex.Message); }
            catch (Exception ex) { Error(ex.Message); }
        }

        private void Warn(string message)
        {
            MessageBox.Show(this, message, "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private void Error(string message)
        {
            MessageBox.Show(this, message, "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        private void Info(string message)
        {
            MessageBox.Show(this, message, "الجرد", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}