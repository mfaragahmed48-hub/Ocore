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
            var root = new Grid { Margin = new Thickness(14) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.15, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            heading.Children.Add(new TextBlock
            {
                Text = "إدارة الجرد: اختيار التشغيلات، العد بوحدتي P وU، ومراجعة الفروق",
                FontSize = 21,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(18, 61, 53))
            });
            selectedSessionText = new TextBlock
            {
                Text = "اختر جلسة جرد لعرض تفاصيلها",
                Margin = new Thickness(0, 5, 0, 0),
                Foreground = Brushes.DimGray
            };
            heading.Children.Add(selectedSessionText);
            Grid.SetRow(heading, 0);
            root.Children.Add(heading);

            var actions = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
            barcodeSearchBox = new TextBox
            {
                Width = 180,
                Height = 34,
                Margin = new Thickness(3),
                Padding = new Thickness(7),
                ToolTip = "ابحث بكود الصنف أو الباركود"
            };
            barcodeSearchBox.TextChanged += BarcodeSearchBox_TextChanged;
            actions.Children.Add(barcodeSearchBox);

            reasonBox = new TextBox
            {
                Width = 190,
                Height = 34,
                Margin = new Thickness(3),
                Padding = new Thickness(7),
                ToolTip = "سبب بدء الجرد أو سبب إلغاء الجلسة"
            };
            actions.Children.Add(reasonBox);
            actions.Children.Add(MakeButton("بدء جرد شامل", CreateFullSession));
            actions.Children.Add(MakeButton("بدء جرد محدد", CreateSelectedSession));

            pUnitLabel = new TextBlock
            {
                Text = "P",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(3, 0, 0, 0),
                FontWeight = FontWeights.Bold
            };
            pQuantityBox = new TextBox
            {
                Width = 75,
                Height = 34,
                Margin = new Thickness(3),
                Padding = new Thickness(7),
                ToolTip = "العدد الفعلي للوحدة الأساسية P"
            };
            uUnitLabel = new TextBlock
            {
                Text = "U",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(3, 0, 0, 0),
                FontWeight = FontWeights.Bold
            };
            uQuantityBox = new TextBox
            {
                Width = 75,
                Height = 34,
                Margin = new Thickness(3),
                Padding = new Thickness(7),
                ToolTip = "العدد الفعلي للوحدات الصغيرة U"
            };
            actions.Children.Add(pUnitLabel);
            actions.Children.Add(pQuantityBox);
            actions.Children.Add(uUnitLabel);
            actions.Children.Add(uQuantityBox);
            actions.Children.Add(MakeButton("حفظ العد P/U", SaveSelectedCount));
            actions.Children.Add(MakeButton("إرسال الجلسة للمراجعة", SubmitSelectedSession));
            actions.Children.Add(MakeButton("اعتماد البند", ApproveSelectedItem));
            actions.Children.Add(MakeButton("اعتماد المحدد جماعيًا", ApproveSelectedItems));
            actions.Children.Add(MakeButton("إلغاء البنود المحددة", CancelSelectedItems));
            actions.Children.Add(MakeButton("طلب إعادة عد", RequestSelectedRecount));
            actions.Children.Add(MakeButton("إعادة مراجعة المحدد", ReopenSelectedItems));
            actions.Children.Add(MakeButton("إغلاق الجلسة", CloseSelectedSession));
            actions.Children.Add(MakeButton("إلغاء الجلسة", CancelSelectedSession));
            actions.Children.Add(MakeButton("إعادة معالجة الحركات", RetryPending));
            actions.Children.Add(MakeButton("تقارير الجرد", OpenInventoryReports));
            actions.Children.Add(MakeButton("إعدادات تتبع الأصناف", OpenTrackingSettings));
            actions.Children.Add(MakeButton("تحديث", RefreshAll));
            Grid.SetRow(actions, 1);
            root.Children.Add(actions);

            var main = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.9, GridUnitType.Star) });
            main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.8, GridUnitType.Star) });
            sessionsGrid = CreateGrid();
            sessionsGrid.SelectionChanged += SessionsGrid_SelectionChanged;
            var sessionsPanel = MakePanel("جلسات الجرد", sessionsGrid);
            Grid.SetColumn(sessionsPanel, 0);
            main.Children.Add(sessionsPanel);

            itemsGrid = CreateGrid();
            itemsGrid.SelectionMode = DataGridSelectionMode.Extended;
            itemsGrid.SelectionChanged += ItemsGrid_SelectionChanged;
            var itemsPanel = MakePanel("تفاصيل التشغيلات والعد الفعلي والمراجعة", itemsGrid);
            itemsPanel.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(itemsPanel, 1);
            main.Children.Add(itemsPanel);
            Grid.SetRow(main, 2);
            root.Children.Add(main);

            var lower = new Grid();
            lower.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            lower.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            pendingGrid = CreateGrid();
            var pendingPanel = MakePanel("الحركات المعلقة أثناء الجرد", pendingGrid);
            Grid.SetColumn(pendingPanel, 0);
            lower.Children.Add(pendingPanel);
            eventsGrid = CreateGrid();
            var eventsPanel = MakePanel("سجل أحداث الجلسة", eventsGrid);
            eventsPanel.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(eventsPanel, 1);
            lower.Children.Add(eventsPanel);
            Grid.SetRow(lower, 3);
            root.Children.Add(lower);

            var footer = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 8, 0, 0)
            };
            progressText = new TextBlock
            {
                Text = "التقدم: —",
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(18, 61, 53))
            };
            footer.Children.Add(progressText);
            noteBox = new TextBox
            {
                Width = 330,
                Height = 34,
                Margin = new Thickness(20, 0, 5, 0),
                Padding = new Thickness(7),
                ToolTip = "ملاحظة المدير أو سبب الفرق أو إعادة العد"
            };
            footer.Children.Add(noteBox);
            footer.Children.Add(MakeButton("إغلاق الشاشة", () => Close()));
            Grid.SetRow(footer, 4);
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
            itemsView.RowFilter = "ProductCode = '" + escaped + "' OR Barcode = '" + escaped + "'";
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
                auditService.SaveCount(currentUserId, selectedSessionId,
                    Convert.ToInt32(item["Id"]), actualP, actualU);
                pQuantityBox.Clear();
                uQuantityBox.Clear();
                LoadSessions(selectedSessionId);
                Info("تم حفظ العد الفعلي بوحدتي P وU.");
            });
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