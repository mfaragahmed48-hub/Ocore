using System;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Input;
using Ocore.desktop.Services;

namespace Ocore.desktop.Views
{
    public sealed class InventoryAuditWindow : Window
    {
        private readonly int currentUserId;
        private readonly InventoryAuditService auditService;
        private readonly InventoryAuditQueryService queryService;
        private DataGrid sessionsGrid, itemsGrid, pendingGrid, eventsGrid;
        private TextBox reasonBox, noteBox;
        private DataView itemsView;
        private DataTable availableBatchesTable, availableProductsTable;
        private readonly Dictionary<ComboBox, DataView> auditProductViews = new Dictionary<ComboBox, DataView>();
        private TextBlock progressText, selectedSessionText;
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
            Loaded += (sender, args) => Dispatcher.BeginInvoke(new Action(FocusEntrySearchRow), DispatcherPriority.Loaded);
            LoadAvailableProducts();
            LoadSessions();
        }

        private UIElement BuildLayout()
        {
            var root = new Grid
            {
                Margin = new Thickness(8),
                Background = new SolidColorBrush(Color.FromRgb(238, 242, 240))
            };

            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(125) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var primary = new SolidColorBrush(Color.FromRgb(36, 75, 65));
            var line = new SolidColorBrush(Color.FromRgb(205, 214, 210));
            var headerBackground = new SolidColorBrush(Color.FromRgb(229, 235, 232));

            // شريط علوي بسيط بدل تصميم الفاتورة والبطاقات.
            var toolbar = new DockPanel
            {
                LastChildFill = false,
                Margin = new Thickness(0, 0, 0, 6)
            };

            var title = new TextBlock
            {
                Text = "الجرد — جدول الأصناف",
                FontSize = 17,
                FontWeight = FontWeights.Bold,
                Foreground = primary,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 0, 14, 0)
            };
            DockPanel.SetDock(title, Dock.Right);
            toolbar.Children.Add(title);

            var toolbarButtons = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            toolbarButtons.Children.Add(MakeButton("جلسة جرد جديدة", StartNewInvoice));
            toolbarButtons.Children.Add(MakeButton("إضافة صف", AddInvoiceSearchRow));
            toolbarButtons.Children.Add(MakeButton("تحديث", RefreshAll));
            toolbarButtons.Children.Add(MakeButton("التقارير", OpenInventoryReports));
            toolbarButtons.Children.Add(MakeButton("إعدادات التتبع", OpenTrackingSettings));
            DockPanel.SetDock(toolbarButtons, Dock.Left);
            toolbar.Children.Add(toolbarButtons);
            Grid.SetRow(toolbar, 0);
            root.Children.Add(toolbar);

            // معلومات الجلسة في سطر واحد لتوفير مساحة أكبر للجدول.
            var sessionBar = new Grid
            {
                Background = Brushes.White,
                Margin = new Thickness(0, 0, 0, 5)
            };
            sessionBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            sessionBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
            sessionBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            sessionBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });

            selectedSessionText = new TextBlock
            {
                Text = "اختر جلسة جرد أو ابدأ جلسة جديدة",
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = primary,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 5, 8, 5),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(selectedSessionText, 0);
            sessionBar.Children.Add(selectedSessionText);

            progressText = new TextBlock
            {
                Text = "التقدم: —",
                FontSize = 11,
                Foreground = primary,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(5)
            };
            Grid.SetColumn(progressText, 1);
            sessionBar.Children.Add(progressText);

            reasonBox = new TextBox
            {
                Width = 185,
                Height = 28,
                Margin = new Thickness(4),
                Padding = new Thickness(6, 2, 6, 2),
                FontSize = 11,
                VerticalContentAlignment = VerticalAlignment.Center,
                ToolTip = "سبب بدء الجرد أو سبب إلغاء الجلسة"
            };
            Grid.SetColumn(reasonBox, 2);
            sessionBar.Children.Add(reasonBox);

            sessionsGrid = CreateGrid();
            sessionsGrid.Height = 64;
            sessionsGrid.MinHeight = 58;
            sessionsGrid.MaxHeight = 68;
            sessionsGrid.Margin = new Thickness(4, 2, 4, 2);
            sessionsGrid.SelectionChanged += SessionsGrid_SelectionChanged;
            sessionsGrid.Background = Brushes.White;
            sessionsGrid.BorderBrush = line;
            sessionsGrid.BorderThickness = new Thickness(1);
            Grid.SetColumn(sessionsGrid, 3);
            sessionBar.Children.Add(sessionsGrid);

            Grid.SetRow(sessionBar, 1);
            root.Children.Add(sessionBar);

            // مساحة العمل الرئيسية: جدول فقط، بخطوط واضحة ورؤوس ثابتة.
            var gridHost = new Border
            {
                Background = Brushes.White,
                BorderBrush = line,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(1)
            };

            itemsGrid = CreateItemsGrid();
            itemsGrid.SelectionMode = DataGridSelectionMode.Extended;
            itemsGrid.SelectionChanged += ItemsGrid_SelectionChanged;
            itemsGrid.CellEditEnding += ItemsGrid_CellEditEnding;
            itemsGrid.PreviewKeyDown += ItemsGrid_PreviewKeyDown;
            itemsGrid.MinHeight = 220;
            itemsGrid.RowHeight = 31;
            itemsGrid.FontSize = 11;
            itemsGrid.Background = Brushes.White;
            itemsGrid.Foreground = new SolidColorBrush(Color.FromRgb(35, 45, 40));
            itemsGrid.BorderThickness = new Thickness(0);
            itemsGrid.HorizontalGridLinesBrush = line;
            itemsGrid.VerticalGridLinesBrush = line;
            itemsGrid.AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(247, 249, 248));
            itemsGrid.GridLinesVisibility = DataGridGridLinesVisibility.All;
            itemsGrid.HeadersVisibility = DataGridHeadersVisibility.Column;
            itemsGrid.CanUserReorderColumns = true;
            itemsGrid.CanUserResizeColumns = true;
            itemsGrid.CanUserSortColumns = true;
            itemsGrid.IsReadOnly = false;
            gridHost.Child = itemsGrid;
            Grid.SetRow(gridHost, 2);
            root.Children.Add(gridHost);

            var actions = new WrapPanel
            {
                Margin = new Thickness(0, 6, 0, 4),
                Orientation = Orientation.Horizontal
            };
            actions.Children.Add(MakeButton("إرسال للمراجعة", SubmitSelectedSession));
            actions.Children.Add(MakeButton("اعتماد البند", ApproveSelectedItem));
            actions.Children.Add(MakeButton("اعتماد المحدد", ApproveSelectedItems));
            actions.Children.Add(MakeButton("إلغاء البنود", CancelSelectedItems));
            actions.Children.Add(MakeButton("طلب إعادة عد", RequestSelectedRecount));
            actions.Children.Add(MakeButton("إعادة مراجعة", ReopenSelectedItems));
            actions.Children.Add(MakeButton("إلغاء المراجعة", CancelReopenedSelectedItems));
            actions.Children.Add(MakeButton("إغلاق الجلسة", CloseSelectedSession));
            actions.Children.Add(MakeButton("إلغاء الجلسة", CancelSelectedSession));
            actions.Children.Add(MakeButton("معالجة الحركات", RetryPending));
            Grid.SetRow(actions, 3);
            root.Children.Add(actions);

            var activityTabs = new TabControl
            {
                Height = 125,
                Margin = new Thickness(0, 2, 0, 4),
                FontSize = 10
            };
            var pendingTab = new TabItem { Header = "الحركات المعلقة" };
            pendingGrid = CreateGrid();
            pendingTab.Content = pendingGrid;
            activityTabs.Items.Add(pendingTab);

            var eventsTab = new TabItem { Header = "سجل الأحداث" };
            eventsGrid = CreateGrid();
            eventsTab.Content = eventsGrid;
            activityTabs.Items.Add(eventsTab);

            Grid.SetRow(activityTabs, 4);
            root.Children.Add(activityTabs);

            var footer = new DockPanel { Margin = new Thickness(0, 1, 0, 0) };
            noteBox = new TextBox
            {
                Height = 28,
                MinWidth = 260,
                Margin = new Thickness(0, 0, 5, 0),
                Padding = new Thickness(6, 2, 6, 2),
                FontSize = 11,
                VerticalContentAlignment = VerticalAlignment.Center,
                ToolTip = "ملاحظة المدير أو سبب الفرق أو إعادة العد أو الإلغاء"
            };
            footer.Children.Add(noteBox);
            var closeButton = MakeButton("إغلاق الشاشة", () => Close());
            DockPanel.SetDock(closeButton, Dock.Left);
            footer.Children.Add(closeButton);
            Grid.SetRow(footer, 5);
            root.Children.Add(footer);

            return root;
        }

        private DataGrid CreateItemsGrid()
        {
            var grid = new DataGrid
            {
                AutoGenerateColumns = true,
                IsReadOnly = false,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                SelectionUnit = DataGridSelectionUnit.FullRow,
                SelectionMode = DataGridSelectionMode.Extended,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                GridLinesVisibility = DataGridGridLinesVisibility.All,
                RowHeight = 38,
                FontSize = 11,
                Margin = new Thickness(0, 5, 0, 0),
                AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(246, 249, 247)),
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            grid.AutoGeneratingColumn += ItemsGrid_AutoGeneratingColumn;
            return grid;
        }

        private void ItemsGrid_AutoGeneratingColumn(object sender, DataGridAutoGeneratingColumnEventArgs args)
        {
            string name = args.PropertyName;
            if (name == "IsEntryRow") { args.Cancel = true; return; }
            if (name == "ProductName")
            {
                args.Cancel = true;

                // احتفظ بعمود واحد فقط للصنف حتى لو أعاد WPF توليد الأعمدة عند تغيير مصدر البيانات.
                var existingProductColumns = itemsGrid.Columns
                    .Where(column => string.Equals(column.SortMemberPath, "ProductName", StringComparison.Ordinal))
                    .ToList();

                if (existingProductColumns.Count > 0)
                {
                    var firstColumn = existingProductColumns[0];
                    firstColumn.Header = "الصنف";

                    foreach (var duplicateColumn in existingProductColumns.Skip(1))
                        itemsGrid.Columns.Remove(duplicateColumn);

                    firstColumn.DisplayIndex = 0;
                    return;
                }

                var productColumn = new DataGridTemplateColumn
                {
                    Header = "الصنف",
                    Width = new DataGridLength(2.2, DataGridLengthUnitType.Star),
                    MinWidth = 230,
                    CellTemplate = CreateAuditProductDisplayTemplate(),
                    CellEditingTemplate = CreateAuditProductTemplate(),
                    SortMemberPath = "ProductName"
                };

                itemsGrid.Columns.Add(productColumn);
                productColumn.DisplayIndex = 0;
                return;
            }
            if (name == "PUnitName" || name == "UUnitName" || name == "UnitsPerPackageSnapshot" ||
                name == "Id" || name == "ProductId" || name == "BatchId" ||
                name == "TargetType" || name == "TargetKey" || name == "AssignedUserId" ||
                name == "CountedBy" || name == "PurchasePriceSnapshot" ||
                name == "SourceRecordId" || name == "SessionId" || name == "EmployeeUserId")
            { args.Cancel = true; return; }

            args.Column.IsReadOnly = name != "ActualPQuantity" && name != "ActualUQuantity";
            if (name == "ActualPQuantity") { args.Column.Header = "الفعلي P"; args.Column.Width = 75; }
            else if (name == "ActualUQuantity") { args.Column.Header = "الفعلي U"; args.Column.Width = 75; }
            else if (name == "Unit" || name == "UnitSnapshot") args.Column.Header = "الوحدات (P/U)";
            else if (name == "BookQuantity") args.Column.Header = "الرصيد الدفتري";
            else if (name == "ActualQuantity") args.Column.Header = "الفعلي المكافئ P";
            else if (name == "VarianceQuantity") args.Column.Header = "الفرق";
            else if (name == "ItemStatus") args.Column.Header = "حالة البند";
            else if (name == "BatchNumber") args.Column.Header = "رقم التشغيلة";
            else if (name == "ExpiryDate") args.Column.Header = "الصلاحية";
            else if (name == "ProductCode") args.Column.Header = "كود الصنف";
            else if (name == "TargetKind") args.Column.Header = "نوع التتبع";
            else if (name == "AssignedEmployee") args.Column.Header = "المسؤول";
            else if (name == "CountedByName") args.Column.Header = "تم العد بواسطة";
            else if (name == "ManagerNote") args.Column.Header = "ملاحظة المدير";
        }

        private DataTemplate CreateAuditProductDisplayTemplate()
        {
            var factory = new FrameworkElementFactory(typeof(TextBlock));
            factory.SetBinding(TextBlock.TextProperty, new Binding("ProductName"));
            factory.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            factory.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
            factory.SetValue(TextBlock.MarginProperty, new Thickness(5, 3, 5, 3));
            return new DataTemplate { VisualTree = factory };
        }

        private DataTemplate CreateAuditProductTemplate()
        {
            var factory = new FrameworkElementFactory(typeof(ComboBox));
            factory.SetValue(ComboBox.IsEditableProperty, true);
            factory.SetValue(ComboBox.IsTextSearchEnabledProperty, false);
            factory.SetValue(ComboBox.StaysOpenOnEditProperty, true);
            factory.SetValue(ComboBox.DisplayMemberPathProperty, "ProductName");
            factory.SetValue(ComboBox.SelectedValuePathProperty, "ProductId");
            factory.SetValue(ComboBox.MinWidthProperty, 210.0);
            factory.SetValue(ComboBox.HeightProperty, 30.0);
            factory.SetValue(ComboBox.VerticalContentAlignmentProperty, VerticalAlignment.Center);
            factory.AddHandler(FrameworkElement.LoadedEvent, new RoutedEventHandler(AuditProductComboBox_Loaded));
            factory.AddHandler(Selector.SelectionChangedEvent, new SelectionChangedEventHandler(AuditProductComboBox_SelectionChanged));
            return new DataTemplate { VisualTree = factory };
        }

        private void LoadAvailableProducts()
        {
            try
            {
                availableBatchesTable = queryService.GetAvailableBatches(currentUserId);
                availableProductsTable = availableBatchesTable.DefaultView.ToTable(true, "ProductId", "ProductName", "ProductCode", "Barcode");
            }
            catch (Exception ex) { Error("تعذر تحميل قائمة أصناف الجرد: " + ex.Message); }
        }

        private void AuditProductComboBox_Loaded(object sender, RoutedEventArgs e)
        {
            var combo = sender as ComboBox;
            var row = combo == null ? null : combo.DataContext as DataRowView;
            if (combo == null || row == null || availableProductsTable == null) return;
            combo.Tag = "initializing";
            combo.IsEnabled = IsEntryRow(row);
            var view = new DataView(availableProductsTable);
            auditProductViews[combo] = view;
            combo.ItemsSource = view;
            if (!IsEntryRow(row) && row.Row.Table.Columns.Contains("ProductId"))
                combo.SelectedValue = row["ProductId"] == DBNull.Value ? null : row["ProductId"];
            combo.ApplyTemplate();
            var editor = combo.Template.FindName("PART_EditableTextBox", combo) as TextBox;
            if (editor != null)
            {
                editor.TextChanged -= AuditProductSearchTextChanged;
                editor.TextChanged += AuditProductSearchTextChanged;
            }
            combo.Tag = "ready";
        }

        private void AuditProductSearchTextChanged(object sender, TextChangedEventArgs e)
        {
            var editor = sender as TextBox;
            var combo = editor == null ? null : FindAncestorComboBox(editor);
            if (combo == null || !auditProductViews.ContainsKey(combo)) return;
            var row = combo.DataContext as DataRowView;
            if (row == null || !IsEntryRow(row)) return;
            string term = editor.Text.Trim().Replace("'", "''");
            auditProductViews[combo].RowFilter = string.IsNullOrWhiteSpace(term) ? "" :
                "ProductName LIKE '%" + term + "%' OR ProductCode LIKE '%" + term + "%' OR Barcode LIKE '%" + term + "%'";
            if (combo.Items.Count > 0) combo.IsDropDownOpen = true;
        }

        private static ComboBox FindAncestorComboBox(DependencyObject child)
        {
            DependencyObject parent = child;
            while (parent != null && !(parent is ComboBox))
                parent = System.Windows.Media.VisualTreeHelper.GetParent(parent);
            return parent as ComboBox;
        }

        private void AuditProductComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var combo = sender as ComboBox;
            if (combo == null || !string.Equals(Convert.ToString(combo.Tag), "ready", StringComparison.Ordinal)) return;
            var entryRow = combo.DataContext as DataRowView;
            var product = combo.SelectedItem as DataRowView;
            if (entryRow == null || !IsEntryRow(entryRow) || product == null) return;
            combo.Tag = "processing";
            int productId = Convert.ToInt32(product["ProductId"]);
            Dispatcher.BeginInvoke(new Action(() => AddProductToInvoiceRow(productId, combo)), DispatcherPriority.Background);
        }

        private void AddProductToInvoiceRow(int productId, ComboBox sourceCombo)
        {
            if (availableBatchesTable == null)
            {
                if (sourceCombo != null) sourceCombo.Tag = "ready";
                Warn("قائمة الأصناف غير جاهزة. اضغط تحديث ثم حاول مرة أخرى.");
                return;
            }
            var batchIds = availableBatchesTable.Rows.Cast<DataRow>()
                .Where(row => Convert.ToInt32(row["ProductId"]) == productId)
                .Select(row => Convert.ToInt32(row["BatchId"])).Where(id => id > 0).Distinct().ToList();
            if (batchIds.Count == 0)
            {
                if (sourceCombo != null) sourceCombo.Tag = "ready";
                Warn("لا توجد تشغيلات متاحة لهذا الصنف.");
                return;
            }
            var selectedSession = sessionsGrid.SelectedItem as DataRowView;
            if (selectedSessionId > 0 &&
                (selectedSession == null || !string.Equals(Convert.ToString(selectedSession["Status"]), "Open", StringComparison.OrdinalIgnoreCase)))
            {
                if (sourceCombo != null) sourceCombo.Tag = "ready";
                Warn("هذه الجلسة ليست مفتوحة. اضغط «فاتورة جرد جديدة» لبدء جرد جديد.");
                return;
            }
            try
            {
                if (selectedSessionId <= 0)
                {
                    string reason = string.IsNullOrWhiteSpace(reasonBox.Text) ? "جرد سريع من فاتورة الجرد" : reasonBox.Text.Trim();
                    selectedSessionId = auditService.CreateSession(currentUserId, reason, batchIds);
                }
                else auditService.AddTargetsToOpenSession(currentUserId, selectedSessionId, batchIds);

                LoadSessions(selectedSessionId);
                var first = itemsGrid.Items.Cast<object>().OfType<DataRowView>()
                    .FirstOrDefault(item => !IsEntryRow(item) &&
                        Convert.ToInt32(item["ProductId"]) == productId &&
                        (Convert.ToString(item["ItemStatus"]) == "Pending" || Convert.ToString(item["ItemStatus"]) == "RecountRequired"));
                if (first != null)
                {
                    itemsGrid.SelectedItem = first;
                    itemsGrid.ScrollIntoView(first);
                    var pColumn = itemsGrid.Columns.FirstOrDefault(column => column.SortMemberPath == "ActualPQuantity");
                    if (pColumn != null)
                    {
                        itemsGrid.CurrentCell = new DataGridCellInfo(first, pColumn);
                        itemsGrid.BeginEdit();
                    }
                }
            }
            catch (UnauthorizedAccessException ex) { if (sourceCombo != null) sourceCombo.Tag = "ready"; Warn(ex.Message); }
            catch (Exception ex) { if (sourceCombo != null) sourceCombo.Tag = "ready"; Error(ex.Message); }
        }

        private static bool IsEntryRow(DataRowView row)
        {
            return row != null && row.Row.Table.Columns.Contains("IsEntryRow") &&
                   row["IsEntryRow"] != DBNull.Value && Convert.ToBoolean(row["IsEntryRow"]);
        }

        private DataTable CreateEmptyEntryTable()
        {
            var table = new DataTable();
            table.Columns.Add("Id", typeof(int));
            table.Columns.Add("ProductName", typeof(string));
            table.Columns.Add("ProductCode", typeof(string));
            table.Columns.Add("Barcode", typeof(string));
            table.Columns.Add("TargetKind", typeof(string));
            table.Columns.Add("BatchNumber", typeof(string));
            table.Columns.Add("ExpiryDate", typeof(string));
            table.Columns.Add("Unit", typeof(string));
            table.Columns.Add("PUnitName", typeof(string));
            table.Columns.Add("UUnitName", typeof(string));
            table.Columns.Add("UnitsPerPackageSnapshot", typeof(int));
            table.Columns.Add("BookQuantity", typeof(double));
            table.Columns.Add("ActualQuantity", typeof(double));
            table.Columns.Add("ActualPQuantity", typeof(int));
            table.Columns.Add("ActualUQuantity", typeof(int));
            table.Columns.Add("VarianceQuantity", typeof(double));
            table.Columns.Add("ItemStatus", typeof(string));
            table.Columns.Add("AssignedUserId", typeof(int));
            table.Columns.Add("AssignedEmployee", typeof(string));
            table.Columns.Add("CountedBy", typeof(int));
            table.Columns.Add("CountedByName", typeof(string));
            table.Columns.Add("ManagerNote", typeof(string));
            table.Columns.Add("PurchasePriceSnapshot", typeof(double));
            table.Columns.Add("BatchId", typeof(int));
            table.Columns.Add("ProductId", typeof(int));
            table.Columns.Add("TargetType", typeof(string));
            table.Columns.Add("TargetKey", typeof(string));
            table.Columns.Add("IsEntryRow", typeof(bool));
            AppendEntryRow(table);
            return table;
        }

        private void AppendEntryRow(DataTable table)
        {
            if (table == null) return;
            if (!table.Columns.Contains("IsEntryRow")) table.Columns.Add("IsEntryRow", typeof(bool));
            var row = table.NewRow();
            foreach (DataColumn column in table.Columns)
            {
                if (column.ColumnName == "IsEntryRow") row[column] = true;
                else if (column.ColumnName == "Id" || column.ColumnName == "ProductId" || column.ColumnName == "BatchId")
                    row[column] = Convert.ChangeType(-1, column.DataType, CultureInfo.InvariantCulture);
                else if (column.DataType == typeof(string)) row[column] = "";
                else if (column.DataType == typeof(bool)) row[column] = false;
                else if (column.DataType.IsValueType) row[column] = Activator.CreateInstance(column.DataType);
            }
            if (table.Columns.Contains("ItemStatus")) row["ItemStatus"] = "";
            if (table.Columns.Contains("ProductName")) row["ProductName"] = "";
            table.Rows.Add(row);
        }

        private void AddInvoiceSearchRow()
        {
            if (selectedSessionId > 0)
            {
                var session = sessionsGrid.SelectedItem as DataRowView;
                if (session == null || !string.Equals(Convert.ToString(session["Status"]), "Open", StringComparison.OrdinalIgnoreCase))
                {
                    Warn("إضافة سطر بحث متاحة في الجلسة المفتوحة فقط.");
                    return;
                }
            }
            if (itemsView == null)
            {
                itemsView = CreateEmptyEntryTable().DefaultView;
                itemsGrid.ItemsSource = itemsView;
            }
            var entry = itemsGrid.Items.Cast<object>().OfType<DataRowView>().LastOrDefault(IsEntryRow);
            if (entry == null)
            {
                AppendEntryRow(itemsView.Table);
                entry = itemsGrid.Items.Cast<object>().OfType<DataRowView>().LastOrDefault(IsEntryRow);
            }
            if (entry != null)
            {
                itemsGrid.SelectedItem = entry;
                itemsGrid.ScrollIntoView(entry);
                var searchColumn = itemsGrid.Columns.FirstOrDefault(c => Convert.ToString(c.Header) == "الصنف");
                if (searchColumn != null)
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        itemsGrid.CurrentCell = new DataGridCellInfo(entry, searchColumn);
                        itemsGrid.BeginEdit();
                    }), DispatcherPriority.Background);
                }
            }
        }

        private void FocusEntrySearchRow()
        {
            var entry = itemsGrid.Items.Cast<object>().OfType<DataRowView>().LastOrDefault(IsEntryRow);
            if (entry == null) return;
            var searchColumn = itemsGrid.Columns.FirstOrDefault(c => Convert.ToString(c.Header) == "الصنف");
            if (searchColumn == null) return;
            itemsGrid.SelectedItem = entry;
            itemsGrid.ScrollIntoView(entry);
            itemsGrid.CurrentCell = new DataGridCellInfo(entry, searchColumn);
            itemsGrid.BeginEdit();
        }

        private void StartNewInvoice()
        {
            selectedSessionId = 0;
            sessionsGrid.SelectedItem = null;
            selectedSessionText.Text = "فاتورة جرد جديدة — اختر أول صنف من سطر البحث";
            progressText.Text = "لم تبدأ الجلسة بعد";
            itemsView = CreateEmptyEntryTable().DefaultView;
            itemsGrid.ItemsSource = itemsView;
            pendingGrid.ItemsSource = null;
            eventsGrid.ItemsSource = null;
            Dispatcher.BeginInvoke(new Action(FocusEntrySearchRow), DispatcherPriority.Loaded);
        }

        private void ItemsGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit) return;
            string property = e.Column.SortMemberPath;
            if (property != "ActualPQuantity" && property != "ActualUQuantity") return;
            var row = e.Row.Item as DataRowView;
            if (row == null || IsEntryRow(row)) return;
            Dispatcher.BeginInvoke(new Action(() => SaveEditedCount(row)), DispatcherPriority.Background);
        }

        private void SaveEditedCount(DataRowView row)
        {
            try
            {
                string pText = Convert.ToString(row["ActualPQuantity"]);
                string uText = Convert.ToString(row["ActualUQuantity"]);
                int actualP, actualU;
                if (string.IsNullOrWhiteSpace(pText) || string.IsNullOrWhiteSpace(uText)) return;
                if (!int.TryParse(pText, NumberStyles.None, CultureInfo.InvariantCulture, out actualP) ||
                    !int.TryParse(uText, NumberStyles.None, CultureInfo.InvariantCulture, out actualU) ||
                    actualP < 0 || actualU < 0)
                { Warn("أدخل عددًا صحيحًا غير سالب في حقلي P وU."); return; }
                int factor = Convert.ToInt32(row["UnitsPerPackageSnapshot"]);
                if (factor <= 1 && actualU != 0) { Warn("هذا الصنف لا يحتوي على وحدات U منفصلة؛ أدخل صفرًا."); return; }
                if (factor > 1 && actualU >= factor) { Warn("عدد وحدات U يجب أن يكون أقل من معامل التحويل (" + factor + ")."); return; }
                int itemId = Convert.ToInt32(row["Id"]);
                auditService.SaveCount(currentUserId, selectedSessionId, itemId, actualP, actualU);
                LoadSessions(selectedSessionId);
                SelectNextUncountedItem(itemId);
            }
            catch (Exception ex) { Error("تعذر حفظ كمية السطر: " + ex.Message); }
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





        private void LoadSessions(int preferredId = 0)
        {
            sessionsGrid.ItemsSource = queryService.GetSessions(currentUserId, true).DefaultView;
            if (sessionsGrid.Items.Count == 0)
            {
                selectedSessionId = 0;
                selectedSessionText.Text = "فاتورة جرد جديدة — اختر أول صنف من سطر البحث";
                progressText.Text = "لم تبدأ الجلسة بعد";
                itemsView = CreateEmptyEntryTable().DefaultView;
                itemsGrid.ItemsSource = itemsView;
                pendingGrid.ItemsSource = null;
                eventsGrid.ItemsSource = null;
                Dispatcher.BeginInvoke(new Action(FocusEntrySearchRow), DispatcherPriority.Loaded);
                return;
            }
            DataRowView selected = null;
            foreach (var value in sessionsGrid.Items)
            {
                var row = value as DataRowView;
                if (row != null && Convert.ToInt32(row["Id"]) == (preferredId > 0 ? preferredId : selectedSessionId))
                { selected = row; break; }
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
            // يتم إدخال P وU مباشرة داخل صف التشغيلة.
        }



        private void LoadSelectedSession()
        {
            var row = sessionsGrid.SelectedItem as DataRowView;
            if (row == null) return;
            selectedSessionId = Convert.ToInt32(row["Id"]);
            selectedSessionText.Text = "الجلسة: " + row["SessionNumber"] +
                " | الحالة: " + row["Status"] + " | السبب: " + row["Reason"];

            DataTable itemTable = queryService.GetItems(selectedSessionId, currentUserId);
            if (!itemTable.Columns.Contains("IsEntryRow")) itemTable.Columns.Add("IsEntryRow", typeof(bool));
            bool isOpen = string.Equals(Convert.ToString(row["Status"]), "Open", StringComparison.OrdinalIgnoreCase);
            if (isOpen) AppendEntryRow(itemTable);
            itemsView = itemTable.DefaultView;
            // البنود الملغاة/المحذوفة تبقى محفوظة في السجل ولا تظهر ضمن جدول العمل.
            itemsView.RowFilter = "ItemStatus IS NULL OR ItemStatus <> 'Cancelled'";
            itemsGrid.ItemsSource = itemsView;

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
        }

        private void RefreshAll() { LoadAvailableProducts(); LoadSessions(selectedSessionId); }

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
                Info("تم إنشاء جلسة الجرد الشامل. أدخل العدد الفعلي P وU في كل سطر.");
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



        private void SelectNextUncountedItem(int previousItemId)
        {
            foreach (var value in itemsGrid.Items)
            {
                var row = value as DataRowView;
                if (row == null || IsEntryRow(row)) continue;
                string status = Convert.ToString(row["ItemStatus"]);
                int id = Convert.ToInt32(row["Id"]);
                if (id != previousItemId && (status == "Pending" || status == "RecountRequired"))
                {
                    itemsGrid.SelectedItem = row;
                    itemsGrid.ScrollIntoView(row);
                    var pColumn = itemsGrid.Columns.FirstOrDefault(column => column.SortMemberPath == "ActualPQuantity");
                    if (pColumn != null)
                    {
                        itemsGrid.CurrentCell = new DataGridCellInfo(row, pColumn);
                        itemsGrid.BeginEdit();
                    }
                    return;
                }
            }
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
            if (item == null || IsEntryRow(item)) { Warn("حدد بند جرد فعليًا للمراجعة."); return; }
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
            var selected = itemsGrid.SelectedItems.Cast<object>().OfType<DataRowView>().Where(x => !IsEntryRow(x)).ToList();
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

        private void ItemsGrid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Delete) return;
            // لا نعترض Delete أثناء تحرير نص داخل خلية أو قائمة اختيار.
            if (Keyboard.FocusedElement is TextBoxBase || Keyboard.FocusedElement is ComboBox) return;
            var selected = itemsGrid.SelectedItems.Cast<object>().OfType<DataRowView>()
                .Where(row => !IsEntryRow(row)).ToList();
            if (selected.Count == 0) return;
            e.Handled = true;
            DeleteSelectedItems();
        }

        private void DeleteSelectedItems()
        {
            var selected = itemsGrid.SelectedItems.Cast<object>().OfType<DataRowView>()
                .Where(row => !IsEntryRow(row)).ToList();
            if (selected.Count == 0 || selectedSessionId <= 0) return;
            var session = sessionsGrid.SelectedItem as DataRowView;
            if (session == null || !string.Equals(Convert.ToString(session["Status"]), "Open", StringComparison.OrdinalIgnoreCase))
            {
                Warn("يمكن حذف البنود من جلسة جرد مفتوحة فقط.");
                return;
            }
            if (MessageBox.Show(
                "سيتم حذف البنود المحددة من جدول الجرد. لن تتغير أرصدة المخزون أو بيانات التشغيل. هل تريد المتابعة؟",
                "تأكيد حذف بنود الجرد", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;
            var itemIds = selected.Select(row => Convert.ToInt32(row["Id"])).ToList();
            RunAction(() =>
            {
                auditService.DeleteItems(currentUserId, selectedSessionId, itemIds);
                LoadSessions(selectedSessionId);
                Info("تم حذف " + itemIds.Count + " بند من جدول الجرد.");
            });
        }

        private void CancelSelectedItems()
        {
            var selected = itemsGrid.SelectedItems.Cast<object>().OfType<DataRowView>().Where(x => !IsEntryRow(x)).ToList();
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
            if (item == null || IsEntryRow(item) || string.IsNullOrWhiteSpace(noteBox.Text))
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
            var selected = itemsGrid.SelectedItems.Cast<object>().OfType<DataRowView>().Where(x => !IsEntryRow(x)).ToList();
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
            var selected = itemsGrid.SelectedItems.Cast<object>().OfType<DataRowView>().Where(x => !IsEntryRow(x)).ToList();
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
            if (selectedSessionId <= 0)
            {
                Warn("اختر جلسة جرد أولًا.");
                return;
            }
            if (MessageBox.Show("سيتم إلغاء الجلسة ورفع التجميد المرتبط بها. هل تريد المتابعة؟",
                "تأكيد الإلغاء", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            RunAction(() =>
            {
                auditService.CancelSession(currentUserId, selectedSessionId, string.Empty);
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