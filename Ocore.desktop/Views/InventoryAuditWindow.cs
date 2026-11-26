
using System;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Ocore.desktop.Services;

namespace Ocore.desktop.Views
{
    public sealed class InventoryAuditWindow : Window
    {
        private readonly int currentUserId;
        private readonly bool isManager;
        private readonly InventoryAuditService auditService;
        private readonly InventoryAuditQueryService queryService;

        private DataGrid sessionsGrid;
        private DataGrid itemsGrid;
        private DataGrid pendingGrid;
        private DataGrid eventsGrid;
        private ComboBox employeeCombo;
        private TextBox reasonBox;
        private TextBox quantityBox;
        private TextBox noteBox;
        private TextBox barcodeSearchBox;
        private DataView itemsView;
        private TextBlock progressText;
        private TextBlock selectedSessionText;
        private string activeLockToken;
        private int activeLockItemId;
        private int selectedSessionId;

        public InventoryAuditWindow(int userId, string role)
        {
            currentUserId = userId;
            isManager = string.Equals(
                role, "Manager", StringComparison.OrdinalIgnoreCase);

            auditService = new InventoryAuditService();
            queryService = new InventoryAuditQueryService();

            Title = isManager ? "إدارة جلسات الجرد" : "جرد المخزون";
            Width = 1220;
            Height = 790;
            MinWidth = 950;
            MinHeight = 650;
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
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.2, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var heading = new StackPanel
            {
                Margin = new Thickness(0, 0, 0, 10)
            };

            heading.Children.Add(new TextBlock
            {
                Text = isManager
                    ? "إدارة الجرد ومراجعة الفروق"
                    : "تنفيذ جرد المخزون",
                FontSize = 23,
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

            var actions = new WrapPanel
            {
                Margin = new Thickness(0, 0, 0, 10)
            };

            barcodeSearchBox = new TextBox
            {
                Width = 190,
                Height = 34,
                Margin = new Thickness(3),
                Padding = new Thickness(7),
                ToolTip = "امسح باركود الصنف أو اكتب كود الصنف لعرض تشغيلاته"
            };

            barcodeSearchBox.TextChanged += BarcodeSearchBox_TextChanged;
            actions.Children.Add(barcodeSearchBox);

            if (isManager)
            {
                reasonBox = new TextBox
                {
                    Width = 210,
                    Height = 34,
                    Margin = new Thickness(3),
                    Padding = new Thickness(7),
                    ToolTip = "سبب بدء الجرد أو سبب الإلغاء/إعادة العد"
                };

                actions.Children.Add(reasonBox);

                actions.Children.Add(MakeButton(
                    "إعدادات تتبع الأصناف", OpenTrackingSettings));

                actions.Children.Add(MakeButton(
                    "تقارير الجرد", OpenInventoryReports));

                actions.Children.Add(MakeButton(
                    "بدء جرد شامل", CreateFullSession));

                actions.Children.Add(MakeButton(
                    "بدء جرد محدد", CreateSelectedSession));

                employeeCombo = new ComboBox
                {
                    Width = 180,
                    Height = 34,
                    Margin = new Thickness(3),
                    DisplayMemberPath = "FullName",
                    SelectedValuePath = "Id"
                };

                employeeCombo.ItemsSource =
                    queryService.GetEmployees(currentUserId).DefaultView;

                actions.Children.Add(employeeCombo);

                actions.Children.Add(MakeButton(
                    "إسناد البند للموظف", AssignSelectedItem));

                actions.Children.Add(MakeButton(
                    "اعتماد هدف الجرد", ApproveSelectedItem));

                actions.Children.Add(MakeButton(
                    "اعتماد المحدد جماعيًا", ApproveSelectedItems));

                actions.Children.Add(MakeButton(
                    "إلغاء البنود المحددة", CancelSelectedItems));

                actions.Children.Add(MakeButton(
                    "طلب إعادة عد", RequestSelectedRecount));

                actions.Children.Add(MakeButton(
                    "إعادة مراجعة المحدد", ReopenSelectedItems));

                actions.Children.Add(MakeButton(
                    "إغلاق الجلسة", CloseSelectedSession));

                actions.Children.Add(MakeButton(
                    "إلغاء الجلسة", CancelSelectedSession));

                actions.Children.Add(MakeButton(
                    "إعادة معالجة الحركات", RetryPending));
            }
            else
            {
                quantityBox = new TextBox
                {
                    Width = 130,
                    Height = 34,
                    Margin = new Thickness(3),
                    Padding = new Thickness(7),
                    ToolTip = "أدخل الكمية الفعلية بوحدة P. الكسور يجب أن تتوافق مع معامل التحويل U داخل P؛ مثال: 1.5 عند وجود 4 U داخل P."
                };

                actions.Children.Add(quantityBox);

                actions.Children.Add(MakeButton(
                    "بدء عد الهدف", StartSelectedCount));

                actions.Children.Add(MakeButton(
                    "حفظ الكمية الفعلية", SaveSelectedCount));

                actions.Children.Add(MakeButton(
                    "إرسال الجلسة للمراجعة", SubmitSelectedSession));
            }

            actions.Children.Add(MakeButton("تحديث", RefreshAll));

            Grid.SetRow(actions, 1);
            root.Children.Add(actions);

            var main = new Grid
            {
                Margin = new Thickness(0, 0, 0, 8)
            };

            main.ColumnDefinitions.Add(
                new ColumnDefinition
                {
                    Width = new GridLength(0.95, GridUnitType.Star)
                });

            main.ColumnDefinitions.Add(
                new ColumnDefinition
                {
                    Width = new GridLength(1.65, GridUnitType.Star)
                });

            sessionsGrid = CreateGrid();
            sessionsGrid.SelectionChanged += SessionsGrid_SelectionChanged;

            var sessionsPanel = MakePanel("جلسات الجرد", sessionsGrid);

            Grid.SetColumn(sessionsPanel, 0);
            main.Children.Add(sessionsPanel);

            itemsGrid = CreateGrid(!isManager);

            itemsGrid.SelectionMode = isManager
                ? DataGridSelectionMode.Extended
                : DataGridSelectionMode.Single;

            var itemsPanel = MakePanel(
                isManager
                    ? "تفاصيل التشغيلات ومراجعة النتائج"
                    : "التشغيلات المسندة والمتاحة للعد",
                itemsGrid);

            Grid.SetColumn(itemsPanel, 1);
            itemsPanel.Margin = new Thickness(8, 0, 0, 0);
            main.Children.Add(itemsPanel);

            Grid.SetRow(main, 2);
            root.Children.Add(main);

            var lower = new Grid();

            lower.ColumnDefinitions.Add(
                new ColumnDefinition
                {
                    Width = new GridLength(1, GridUnitType.Star)
                });

            lower.ColumnDefinitions.Add(
                new ColumnDefinition
                {
                    Width = new GridLength(1, GridUnitType.Star)
                });

            pendingGrid = CreateGrid();

            var pendingPanel = MakePanel(
                "الحركات المعلقة أثناء الجرد",
                pendingGrid);

            Grid.SetColumn(pendingPanel, 0);
            lower.Children.Add(pendingPanel);

            eventsGrid = CreateGrid();

            var eventsPanel = MakePanel(
                "سجل أحداث الجلسة",
                eventsGrid);

            eventsPanel.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(eventsPanel, 1);
            lower.Children.Add(eventsPanel);

            if (!isManager)
            {
                pendingPanel.Visibility = Visibility.Collapsed;
                eventsPanel.Visibility = Visibility.Collapsed;
            }

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

            if (isManager)
            {
                noteBox = new TextBox
                {
                    Width = 330,
                    Height = 34,
                    Margin = new Thickness(20, 0, 5, 0),
                    Padding = new Thickness(7),
                    ToolTip = "ملاحظة المدير أو سبب إعادة العد"
                };

                footer.Children.Add(noteBox);
            }

            footer.Children.Add(MakeButton("إغلاق الشاشة", () => Close()));

            Grid.SetRow(footer, 4);
            root.Children.Add(footer);

            return root;
        }

        private static DataGrid CreateGrid(bool hideSensitiveAuditColumns = false)
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

                if (name == "Id" ||
                    name == "ProductId" ||
                    name == "BatchId" ||
                    name == "TargetType" ||
                    name == "TargetKey" ||
                    name == "AssignedUserId" ||
                    name == "CountedBy" ||
                    name == "PurchasePriceSnapshot" ||
                    name == "SourceRecordId" ||
                    name == "SessionId" ||
                    name == "EmployeeUserId")
                {
                    args.Cancel = true;
                    return;
                }

                if (hideSensitiveAuditColumns &&
                    (name == "BookQuantity" ||
                     name == "VarianceQuantity" ||
                     name == "ManagerNote"))
                {
                    args.Cancel = true;
                    return;
                }

                if (name == "Unit")
                    args.Column.Header = "الوحدة الأساسية P / الوحدات الصغيرة U";
                else if (name == "BookQuantity")
                    args.Column.Header = "الرصيد الدفتري (P)";
                else if (name == "ActualQuantity")
                    args.Column.Header = "الكمية الفعلية (P)";
                else if (name == "VarianceQuantity")
                    args.Column.Header = "الفرق (P)";
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

        private void BarcodeSearchBox_TextChanged(
            object sender,
            TextChangedEventArgs e)
        {
            ApplyItemFilter();
        }

        private void ApplyItemFilter()
        {
            if (itemsView == null)
                return;

            string value = barcodeSearchBox == null
                ? ""
                : barcodeSearchBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(value))
            {
                itemsView.RowFilter = "";
                return;
            }

            string escaped = value.Replace("'", "''");

            itemsView.RowFilter =
                "ProductCode = '" + escaped +
                "' OR Barcode = '" + escaped + "'";
        }

        private void LoadSessions(int preferredId = 0)
        {
            sessionsGrid.ItemsSource =
                queryService.GetSessions(currentUserId, isManager).DefaultView;

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

            foreach (var item in sessionsGrid.Items)
            {
                var row = item as DataRowView;

                if (row != null &&
                    Convert.ToInt32(row["Id"]) ==
                    (preferredId > 0 ? preferredId : selectedSessionId))
                {
                    selected = row;
                    break;
                }
            }

            sessionsGrid.SelectedItem =
                selected ?? sessionsGrid.Items[0];

            if (sessionsGrid.SelectedItem != null)
                LoadSelectedSession();
        }

        private void SessionsGrid_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            LoadSelectedSession();
        }

        private void LoadSelectedSession()
        {
            var row = sessionsGrid.SelectedItem as DataRowView;

            if (row == null)
                return;

            selectedSessionId = Convert.ToInt32(row["Id"]);

            selectedSessionText.Text =
                "الجلسة: " + row["SessionNumber"] +
                " | الحالة: " + row["Status"] +
                " | السبب: " + row["Reason"];

            itemsView = queryService.GetItems(
                selectedSessionId,
                currentUserId).DefaultView;

            itemsGrid.ItemsSource = itemsView;

            ApplyItemFilter();

            if (isManager)
            {
                pendingGrid.ItemsSource =
                    queryService.GetPendingMovements(
                        selectedSessionId,
                        currentUserId).DefaultView;

                eventsGrid.ItemsSource =
                    queryService.GetSessionEvents(
                        selectedSessionId,
                        currentUserId).DefaultView;
            }
            else
            {
                pendingGrid.ItemsSource = null;
                eventsGrid.ItemsSource = null;
            }

            int total = Convert.ToInt32(row["TotalItems"]);
            int counted = Convert.ToInt32(row["CountedItems"]);
            int approved = Convert.ToInt32(row["ApprovedItems"]);
            int pending = Convert.ToInt32(row["PendingMovements"]);
            int failed = Convert.ToInt32(row["FailedMovements"]);

            double percent = total == 0
                ? 0
                : counted * 100.0 / total;

            progressText.Text = string.Format(
                CultureInfo.CurrentCulture,
                "الإنجاز: {0:0.0}% | تم عدها: {1}/{2} | معتمد: {3} | حركات معلقة: {4} | فاشلة: {5}",
                percent, counted, total, approved, pending, failed);
        }

        private void RefreshAll()
        {
            LoadSessions(selectedSessionId);
        }

        private void CreateFullSession()
        {
            if (!isManager)
                return;

            if (string.IsNullOrWhiteSpace(reasonBox.Text))
            {
                Warn("اكتب سبب بدء الجرد أولًا.");
                return;
            }

            RunAction(() =>
            {
                int id = auditService.CreateSession(
                    currentUserId,
                    reasonBox.Text.Trim());

                reasonBox.Clear();
                LoadSessions(id);

                Info("تم إنشاء جلسة الجرد الشامل. يمكنك الآن إسناد أهداف الجرد للموظفين.");
            });
        }

        private void CreateSelectedSession()
        {
            if (!isManager)
                return;

            if (string.IsNullOrWhiteSpace(reasonBox.Text))
            {
                Warn("اكتب سبب بدء الجرد أولًا.");
                return;
            }

            var picker = new Window
            {
                Title = "اختيار التشغيلات لتحديد أهداف الجرد الجزئي",
                Width = 900,
                Height = 570,
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                FlowDirection = FlowDirection.RightToLeft,
                Background = Brushes.White
            };

            var layout = new DockPanel
            {
                Margin = new Thickness(12)
            };

            var choose = CreateGrid();
            choose.SelectionMode = DataGridSelectionMode.Extended;
            choose.ItemsSource =
                queryService.GetAvailableBatches(currentUserId).DefaultView;

            DockPanel.SetDock(choose, Dock.Top);
            layout.Children.Add(choose);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Left
            };

            var confirm = MakeButton("إنشاء الجرد المحدد", () =>
            {
                var ids = choose.SelectedItems
                    .Cast<DataRowView>()
                    .Select(x => Convert.ToInt32(x["BatchId"]))
                    .Distinct()
                    .ToList();

                if (ids.Count == 0)
                {
                    Warn("حدد تشغيلة واحدة على الأقل.");
                    return;
                }

                try
                {
                    int id = auditService.CreateSession(
                        currentUserId,
                        reasonBox.Text.Trim(),
                        ids);

                    reasonBox.Clear();
                    picker.DialogResult = true;
                    LoadSessions(id);
                }
                catch (Exception ex)
                {
                    Error(ex.Message);
                }
            });

            buttons.Children.Add(confirm);
            buttons.Children.Add(
                MakeButton("إلغاء", () => picker.DialogResult = false));

            DockPanel.SetDock(buttons, Dock.Bottom);
            layout.Children.Add(buttons);

            picker.Content = layout;
            picker.ShowDialog();
        }

        private void AssignSelectedItem()
        {
            if (!isManager)
                return;

            var item = itemsGrid.SelectedItem as DataRowView;
            var employee = employeeCombo.SelectedItem as DataRowView;

            if (item == null || employee == null)
            {
                Warn("حدد هدف جرد وموظفًا أولًا.");
                return;
            }

            RunAction(() =>
            {
                auditService.AssignItem(
                    currentUserId,
                    selectedSessionId,
                    Convert.ToInt32(item["Id"]),
                    Convert.ToInt32(employee["Id"]));

                LoadSelectedSession();
            });
        }

        private void ApproveSelectedItem()
        {
            if (!isManager)
                return;

            var item = itemsGrid.SelectedItem as DataRowView;

            if (item == null)
            {
                Warn("حدد هدف الجرد للمراجعة.");
                return;
            }

            RunAction(() =>
            {
                auditService.ApproveItem(
                    currentUserId,
                    selectedSessionId,
                    Convert.ToInt32(item["Id"]),
                    noteBox.Text.Trim());

                noteBox.Clear();
                LoadSessions(selectedSessionId);
            });
        }

        private void ApproveSelectedItems()
        {
            if (!isManager || selectedSessionId <= 0)
                return;

            var selected = itemsGrid.SelectedItems
                .Cast<object>()
                .OfType<DataRowView>()
                .ToList();

            if (selected.Count == 0)
            {
                Warn("حدد بندًا واحدًا على الأقل للاعتماد الجماعي.");
                return;
            }

            if (MessageBox.Show(
                "سيتم اعتماد " + selected.Count +
                " بندًا في عملية واحدة. إذا تعذر اعتماد أي بند فلن يتم اعتماد أي بند. هل تريد المتابعة؟",
                "تأكيد الاعتماد الجماعي",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                return;
            }

            RunAction(() =>
            {
                auditService.ApproveItems(
                    currentUserId,
                    selectedSessionId,
                    selected.Select(x => Convert.ToInt32(x["Id"])),
                    noteBox.Text.Trim());

                noteBox.Clear();
                LoadSessions(selectedSessionId);

                Info("تم اعتماد البنود المحددة بالكامل داخل معاملة واحدة.");
            });
        }

        private void CancelSelectedItems()
        {
            if (!isManager || selectedSessionId <= 0)
                return;

            var selected = itemsGrid.SelectedItems
                .Cast<object>()
                .OfType<DataRowView>()
                .ToList();

            if (selected.Count == 0)
            {
                Warn("حدد بندًا واحدًا على الأقل للإلغاء.");
                return;
            }

            if (string.IsNullOrWhiteSpace(noteBox.Text))
            {
                Warn("اكتب سبب إلغاء البنود في خانة الملاحظة.");
                return;
            }

            if (MessageBox.Show(
                "سيتم إلغاء " + selected.Count +
                " بندًا، وتحرير أقفال العد ومعالجة الحركات المرتبطة بها. لن يتم اعتماد فروق هذه البنود. هل تريد المتابعة؟",
                "تأكيد إلغاء البنود",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                return;
            }

            RunAction(() =>
            {
                auditService.CancelItems(
                    currentUserId,
                    selectedSessionId,
                    selected.Select(x => Convert.ToInt32(x["Id"])),
                    noteBox.Text.Trim());

                noteBox.Clear();
                LoadSessions(selectedSessionId);

                Info("تم إلغاء البنود المحددة وتسجيل الأسباب.");
            });
        }

        private void RequestSelectedRecount()
        {
            if (!isManager)
                return;

            var item = itemsGrid.SelectedItem as DataRowView;

            if (item == null)
            {
                Warn("حدد تشغيلة لإعادة العد.");
                return;
            }

            if (string.IsNullOrWhiteSpace(noteBox.Text))
            {
                Warn("اكتب سبب طلب إعادة العد في خانة الملاحظة أسفل الشاشة.");
                return;
            }

            RunAction(() =>
            {
                auditService.RequestRecount(
                    currentUserId,
                    selectedSessionId,
                    Convert.ToInt32(item["Id"]),
                    noteBox.Text.Trim());

                noteBox.Clear();
                LoadSelectedSession();
            });
        }

        private void CloseSelectedSession()
        {
            if (!isManager || selectedSessionId <= 0)
                return;

            if (MessageBox.Show(
                "هل تريد إغلاق جلسة الجرد المحددة؟",
                "تأكيد الإغلاق",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                return;
            }

            RunAction(() =>
            {
                auditService.CloseSession(
                    currentUserId,
                    selectedSessionId);

                LoadSessions(selectedSessionId);
                Info("تم إغلاق الجلسة.");
            });
        }

        private void CancelSelectedSession()
        {
            if (!isManager || selectedSessionId <= 0)
                return;

            if (string.IsNullOrWhiteSpace(reasonBox.Text))
            {
                Warn("اكتب سبب الإلغاء في خانة السبب أعلى الشاشة.");
                return;
            }

            if (MessageBox.Show(
                "سيتم إلغاء الجلسة ورفع التجميد. هل تريد المتابعة؟",
                "تأكيد الإلغاء",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                return;
            }

            RunAction(() =>
            {
                auditService.CancelSession(
                    currentUserId,
                    selectedSessionId,
                    reasonBox.Text.Trim());

                reasonBox.Clear();
                LoadSessions(selectedSessionId);
            });
        }

        private void ReopenSelectedItems()
        {
            if (!isManager)
                return;

            var selected = itemsGrid.SelectedItems
                .Cast<object>()
                .OfType<DataRowView>()
                .ToList();

            if (selected.Count == 0 ||
                string.IsNullOrWhiteSpace(noteBox.Text))
            {
                Warn("اختر التشغيلات المعتمدة واكتب سبب إعادة المراجعة.");
                return;
            }

            RunAction(() =>
            {
                auditService.ReopenSelectedItems(
                    currentUserId,
                    selectedSessionId,
                    selected.Select(x => Convert.ToInt32(x["Id"])),
                    noteBox.Text.Trim());

                noteBox.Clear();
                LoadSessions(selectedSessionId);
            });
        }

        private void RetryPending()
        {
            if (!isManager || selectedSessionId <= 0)
                return;

            RunAction(() =>
            {
                auditService.RetryFailedPendingMovements(
                    currentUserId,
                    selectedSessionId);

                LoadSessions(selectedSessionId);
            });
        }

        private void StartSelectedCount()
        {
            if (isManager)
                return;

            var item = itemsGrid.SelectedItem as DataRowView;

            if (item == null)
            {
                Warn("حدد التشغيلة التي تريد عدها.");
                return;
            }

            RunAction(() =>
            {
                activeLockItemId = Convert.ToInt32(item["Id"]);

                activeLockToken = auditService.StartCounting(
                    currentUserId,
                    selectedSessionId,
                    activeLockItemId);

                quantityBox.Text = "";

                Info("تم حجز التشغيلة لك. أدخل الكمية الفعلية بوحدة P. مثال: إذا كانت P تحتوي 4 U وكان لديك علبة وشريطان فأدخل 1.5، ثم اضغط حفظ.");
            });
        }

        private void SaveSelectedCount()
        {
            if (isManager)
                return;

            if (string.IsNullOrWhiteSpace(activeLockToken) ||
                activeLockItemId <= 0)
            {
                Warn("ابدأ عد التشغيلة أولًا.");
                return;
            }

            double quantity;

            bool valid =
                double.TryParse(
                    quantityBox.Text.Trim(),
                    NumberStyles.Number,
                    CultureInfo.CurrentCulture,
                    out quantity)
                ||
                double.TryParse(
                    quantityBox.Text.Trim(),
                    NumberStyles.Number,
                    CultureInfo.InvariantCulture,
                    out quantity);

            if (!valid ||
                double.IsNaN(quantity) ||
                double.IsInfinity(quantity) ||
                quantity < 0)
            {
                Warn("أدخل كمية صحيحة لا تقل عن صفر.");
                return;
            }

            RunAction(() =>
            {
                auditService.SaveCount(
                    currentUserId,
                    selectedSessionId,
                    activeLockItemId,
                    activeLockToken,
                    quantity);

                activeLockToken = null;
                activeLockItemId = 0;
                quantityBox.Clear();

                LoadSelectedSession();

                Info("تم حفظ نتيجة العد. يمكنك متابعة باقي التشغيلات.");
            });
        }

        private void SubmitSelectedSession()
        {
            if (isManager || selectedSessionId <= 0)
                return;

            RunAction(() =>
            {
                auditService.SubmitSession(
                    currentUserId,
                    selectedSessionId);

                LoadSessions(selectedSessionId);
                Info("تم إرسال الجلسة للمراجعة.");
            });
        }

        private void OpenInventoryReports()
        {
            if (!isManager || selectedSessionId <= 0)
                return;

            try
            {
                var window = new InventoryAuditReportsWindow(
                    selectedSessionId,
                    currentUserId);

                window.Owner = this;
                window.ShowDialog();
            }
            catch (UnauthorizedAccessException ex)
            {
                Warn(ex.Message);
            }
            catch (Exception ex)
            {
                Error(ex.Message);
            }
        }

        private void OpenTrackingSettings()
        {
            if (!isManager)
                return;

            var window =
                new InventoryProductTrackingWindow(currentUserId);

            window.Owner = this;
            window.ShowDialog();
        }

        private void RunAction(Action action)
        {
            try
            {
                action();
            }
            catch (UnauthorizedAccessException ex)
            {
                Warn(ex.Message);
            }
            catch (Exception ex)
            {
                Error(ex.Message);
            }
        }

        private void Warn(string message)
        {
            MessageBox.Show(
                this,
                message,
                "تنبيه",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        private void Error(string message)
        {
            MessageBox.Show(
                this,
                message,
                "خطأ",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

        private void Info(string message)
        {
            MessageBox.Show(
                this,
                message,
                "الجرد",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }
}
