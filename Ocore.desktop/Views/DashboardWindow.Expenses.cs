using System;
using System.Data.SQLite;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Ocore.desktop.Data;
using Ocore.desktop.Services;

namespace Ocore.desktop.Views
{
    public partial class DashboardWindow
    {
        private bool kpiButtonAdded;

        private void ExpensesButton_Loaded(object sender, RoutedEventArgs e)
        {
            bool canUseExpenses =
                string.Equals(currentRole, "Manager", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(currentRole, "Pharmacist", StringComparison.OrdinalIgnoreCase);
            ExpensesButton.Visibility = canUseExpenses ? Visibility.Visible : Visibility.Collapsed;
            AddKpiNavigationButton();
        }

        private void AddKpiNavigationButton()
        {
            if (kpiButtonAdded || !(ExpensesButton.Parent is StackPanel)) return;
            var panel = (StackPanel)ExpensesButton.Parent;
            var button = new Button
            {
                Content = "📈   مؤشرات الأداء KPI",
                Margin = new Thickness(10, 3, 10, 3),
                Padding = new Thickness(10),
                Background = new SolidColorBrush(Color.FromRgb(36, 107, 88)),
                Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold,
                BorderThickness = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Right,
                Visibility = permissionService.HasPermission(currentRole, "Reports.View")
                    ? Visibility.Visible : Visibility.Collapsed
            };
            button.Click += KpiButton_Click;
            int index = panel.Children.IndexOf(ExpensesButton);
            panel.Children.Insert(index >= 0 ? index + 1 : panel.Children.Count, button);
            kpiButtonAdded = true;
        }

        private void KpiButton_Click(object sender, RoutedEventArgs e)
        {
            if (!permissionService.HasPermission(currentRole, "Reports.View"))
            {
                MessageBox.Show("ليس لديك صلاحية عرض مؤشرات الأداء.", "صلاحيات المستخدم",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            try { OpenIndependentWindow(new KpiDashboardWindow(currentUserId)); }
            catch (Exception ex)
            {
                Ocore.desktop.ErrorDialog.Show("فتح لوحة مؤشرات الأداء", ex);
            }
        }

        private void ExpensesButton_Click(object sender, RoutedEventArgs e)
        {
            if (!string.Equals(currentRole, "Manager", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(currentRole, "Pharmacist", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("ليس لديك صلاحية الدخول إلى شاشة المصروفات.",
                    "صلاحيات المستخدم", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            try { OpenIndependentWindow(new ExpensesWindow(currentUserId, currentRole)); }
            catch (Exception ex)
            {
                Ocore.desktop.ErrorDialog.Show("فتح شاشة المصروفات", ex);
            }
        }
    }

    internal sealed class KpiDashboardWindow : Window
    {
        private readonly int userId;
        private readonly KpiDashboardService service = new KpiDashboardService();
        private ComboBox period;
        private DatePicker fromPicker, toPicker;
        private StackPanel customPanel, cards, daily, comparisonChart, topProducts, alerts;
        private TextBlock comparison, warning;

        public KpiDashboardWindow(int userId)
        {
            this.userId = userId;
            Title = "O CORE - مؤشرات الأداء KPI";
            Width = 1100; Height = 740; MinWidth = 820; MinHeight = 600;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            FlowDirection = FlowDirection.RightToLeft;
            Background = B("#F3F6F8");
            BuildUi();
            RefreshData();
        }

        private void BuildUi()
        {
            var root = new DockPanel { Margin = new Thickness(18) };
            var header = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
            header.Children.Add(new TextBlock
            {
                Text = "مؤشرات الأداء الرئيسية",
                FontSize = 25,
                FontWeight = FontWeights.Bold,
                Foreground = B("#183F36")
            });
            header.Children.Add(new TextBlock
            {
                Text = "تحليل مالي وتشغيلي حسب الفترة المحددة",
                Foreground = B("#64736F"),
                Margin = new Thickness(0, 5, 0, 0)
            });
            DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);

            var filters = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
            filters.Children.Add(new TextBlock
            {
                Text = "الفترة:",
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            });
            period = new ComboBox { Width = 160, Height = 32, Margin = new Thickness(0, 0, 10, 0) };
            period.Items.Add("اليوم"); period.Items.Add("آخر 7 أيام");
            period.Items.Add("الشهر الحالي"); period.Items.Add("فترة مخصصة");
            period.SelectedIndex = 0; period.SelectionChanged += PeriodChanged; filters.Children.Add(period);

            customPanel = new StackPanel { Orientation = Orientation.Horizontal, Visibility = Visibility.Collapsed };
            fromPicker = new DatePicker { Width = 140, SelectedDate = DateTime.Today };
            toPicker = new DatePicker { Width = 140, SelectedDate = DateTime.Today };
            customPanel.Children.Add(new TextBlock { Text = "من", VerticalAlignment = VerticalAlignment.Center });
            customPanel.Children.Add(fromPicker);
            customPanel.Children.Add(new TextBlock { Text = "إلى", VerticalAlignment = VerticalAlignment.Center });
            customPanel.Children.Add(toPicker); filters.Children.Add(customPanel);

            var refresh = new Button
            {
                Content = "تحديث المؤشرات",
                Padding = new Thickness(14, 7, 14, 7),
                Margin = new Thickness(8, 0, 0, 0),
                Background = B("#246B58"),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0)
            };
            refresh.Click += (s, e) => RefreshData(); filters.Children.Add(refresh);
            DockPanel.SetDock(filters, Dock.Top); root.Children.Add(filters);

            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var body = new StackPanel(); cards = new StackPanel(); body.Children.Add(cards);
            comparison = new TextBlock
            {
                FontWeight = FontWeights.SemiBold,
                Foreground = B("#183F36"),
                Margin = new Thickness(2, 6, 2, 12)
            }; body.Children.Add(comparison);
            warning = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Foreground = B("#9A6418"),
                Background = B("#FFF7E6"),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 14)
            };
            body.Children.Add(warning);
            body.Children.Add(new TextBlock
            {
                Text = "المبيعات اليومية داخل الفترة",
                FontSize = 17,
                FontWeight = FontWeights.Bold,
                Foreground = B("#183F36"),
                Margin = new Thickness(0, 4, 0, 8)
            });
            daily = new StackPanel(); body.Children.Add(daily);
            body.Children.Add(new TextBlock
            {
                Text = "مقارنة المؤشرات المالية",
                FontSize = 17,
                FontWeight = FontWeights.Bold,
                Foreground = B("#183F36"),
                Margin = new Thickness(0, 18, 0, 8)
            });
            comparisonChart = new StackPanel(); body.Children.Add(comparisonChart);
            body.Children.Add(new TextBlock
            {
                Text = "أفضل 5 منتجات مبيعًا",
                FontSize = 17,
                FontWeight = FontWeights.Bold,
                Foreground = B("#183F36"),
                Margin = new Thickness(0, 18, 0, 8)
            });
            topProducts = new StackPanel(); body.Children.Add(topProducts);
            body.Children.Add(new TextBlock
            {
                Text = "التنبيهات الذكية",
                FontSize = 17,
                FontWeight = FontWeights.Bold,
                Foreground = B("#183F36"),
                Margin = new Thickness(0, 18, 0, 8)
            });
            alerts = new StackPanel(); body.Children.Add(alerts);
            body.Children.Add(new TextBlock
            {
                Text = "ملاحظة: المرتجعات تخصم في تاريخ تسجيلها. تكلفة المرتجع تخصم من تكلفة البضاعة فقط عند إعادته للمخزون. قيمة المخزون بالتكلفة تشمل التشغيلات المنتهية إن وجدت.",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Foreground = B("#64736F"),
                Margin = new Thickness(0, 14, 0, 8)
            });
            scroll.Content = body; root.Children.Add(scroll); Content = root;
        }

        private void PeriodChanged(object sender, SelectionChangedEventArgs e)
        {
            if (customPanel != null) customPanel.Visibility = period.SelectedIndex == 3
                ? Visibility.Visible : Visibility.Collapsed;
        }

        private void GetDates(out DateTime from, out DateTime to)
        {
            DateTime today = DateTime.Today;
            switch (period.SelectedIndex)
            {
                case 1: from = today.AddDays(-6); to = today; break;
                case 2: from = new DateTime(today.Year, today.Month, 1); to = today; break;
                case 3: from = fromPicker.SelectedDate ?? today; to = toPicker.SelectedDate ?? today; break;
                default: from = today; to = today; break;
            }
        }

        private void RefreshData()
        {
            try
            {
                DateTime from, to; GetDates(out from, out to);
                if (to.Date < from.Date)
                {
                    MessageBox.Show("تاريخ النهاية يجب ألا يسبق تاريخ البداية.");
                    return;
                }
                var d = service.Get(userId, from, to);
                int span = (to.Date - from.Date).Days + 1;
                var prev = service.Get(userId, from.AddDays(-span), from.AddDays(-1));

                cards.Children.Clear();
                AddRow(Card("صافي المبيعات", d.NetSales, "#16865D", prev.NetSales, true),
                    Card("تكلفة البضاعة المباعة", d.Cost, "#B7791F", prev.Cost, false),
                    Card("المصروفات التشغيلية", d.Expenses, "#B7791F", prev.Expenses, false));
                AddRow(Card("صافي الربح الفعلي", d.NetProfit, d.NetProfit >= 0 ? "#16865D" : "#A83232",
                        d.Complete && prev.Complete ? (double?)prev.NetProfit : null, true),
                    Card("عدد فواتير البيع", d.Invoices.ToString("N0"), "#183F36", d.Invoices, prev.Invoices, true),
                    Card("متوسط الفاتورة", d.Average, "#183F36", prev.Average, true));
                AddRow(Card("إجمالي المرتجعات", d.Returns, "#A83232", prev.Returns, false),
                    Card("القيمة الدفترية للمخزون", d.StockValue, "#183F36"),
                    Card("المبيعات قبل المرتجعات", d.GrossSales, "#183F36", prev.GrossSales, true));

                comparison.Text = "تتم مقارنة كل مؤشر بالفترة السابقة المماثلة في عدد الأيام.";
                warning.Text = d.Complete
                    ? "اكتملت مطابقة كميات التشغيلات مع بنود البيع والمرتجعات."
                    : "تنبيه: توجد بنود بيع أو مرتجعات غير مكتملة التوزيع على التشغيلات؛ صافي الربح المعروض غير نهائي.";
                DrawDaily(d);
                DrawComparison(d);
                DrawTopProducts(from, to);
                DrawAlerts(d, prev);
            }
            catch (Exception ex)
            {
                Ocore.desktop.ErrorDialog.Show("حساب مؤشرات الأداء KPI", ex);
            }
        }

        private void AddRow(params Border[] items)
        {
            var row = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, 0, 8) };
            foreach (var item in items) row.Children.Add(item);
            cards.Children.Add(row);
        }

        private Border Card(string title, double value, string color)
        {
            return BuildCard(title, value.ToString("N2"), color, null, null, true);
        }

        private Border Card(string title, double value, string color, double? previousValue, bool increaseIsPositive)
        {
            return BuildCard(title, value.ToString("N2"), color, value, previousValue, increaseIsPositive);
        }

        private Border Card(string title, string value, string color, double currentValue,
            double? previousValue, bool increaseIsPositive)
        {
            return BuildCard(title, value, color, currentValue, previousValue, increaseIsPositive);
        }

        private Border BuildCard(string title, string value, string color, double? currentValue,
            double? previousValue, bool increaseIsPositive)
        {
            var stack = new StackPanel();
            stack.Children.Add(new Border
            {
                Height = 4,
                Width = 38,
                Background = B(color),
                CornerRadius = new CornerRadius(2),
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 0, 0, 10)
            });
            stack.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 13,
                Foreground = B("#64736F"),
                TextWrapping = TextWrapping.Wrap
            });
            stack.Children.Add(new TextBlock
            {
                Text = value,
                FontSize = 22,
                FontWeight = FontWeights.Bold,
                Foreground = B(color),
                Margin = new Thickness(0, 8, 0, 0)
            });

            if (currentValue.HasValue && previousValue.HasValue)
                stack.Children.Add(BuildTrend(currentValue.Value, previousValue.Value, increaseIsPositive));

            return new Border
            {
                Background = Brushes.White,
                BorderBrush = B("#E1EAE5"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(16),
                Margin = new Thickness(5),
                MinHeight = 112,
                Child = stack
            };
        }

        private TextBlock BuildTrend(double current, double previous, bool increaseIsPositive)
        {
            string text;
            string foreground;
            string background;

            if (Math.Abs(current - previous) < 0.000001)
            {
                text = "• دون تغيير عن الفترة السابقة";
                foreground = "#64736F";
                background = "#F1F4F2";
            }
            else if (Math.Abs(previous) < 0.000001)
            {
                text = "جديد مقارنة بالفترة السابقة";
                bool positive = current > 0 ? increaseIsPositive : !increaseIsPositive;
                foreground = positive ? "#16865D" : "#A83232";
                background = positive ? "#EAF6F0" : "#FCEEEE";
            }
            else
            {
                bool increased = current > previous;
                double change = Math.Abs((current - previous) / Math.Abs(previous) * 100.0);
                bool positive = increased == increaseIsPositive;
                text = (increased ? "▲ " : "▼ ") + change.ToString("N1") + "% عن الفترة السابقة";
                foreground = positive ? "#16865D" : "#A83232";
                background = positive ? "#EAF6F0" : "#FCEEEE";
            }

            return new TextBlock
            {
                Text = text,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = B(foreground),
                Background = B(background),
                Padding = new Thickness(7, 4, 7, 4),
                Margin = new Thickness(0, 9, 0, 0),
                TextWrapping = TextWrapping.Wrap
            };
        }

        private void DrawDaily(KpiData d)
        {
            daily.Children.Clear();
            if (d.Days.Count == 0)
            {
                daily.Children.Add(new TextBlock
                {
                    Text = "لا توجد مبيعات مسجلة في الفترة المحددة.",
                    Foreground = B("#64736F")
                }); return;
            }
            double max = 1; foreach (var item in d.Days) if (item.Amount > max) max = item.Amount;
            foreach (var item in d.Days)
            {
                var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(95) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
                var date = new TextBlock { Text = item.Date, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(date, 0); row.Children.Add(date);
                var track = new Border
                {
                    Height = 12,
                    Background = B("#E8EFEC"),
                    CornerRadius = new CornerRadius(6),
                    VerticalAlignment = VerticalAlignment.Center
                };
                track.Child = new Border
                {
                    Width = Math.Max(2, 400 * item.Amount / max),
                    Height = 12,
                    Background = B("#16865D"),
                    CornerRadius = new CornerRadius(6),
                    HorizontalAlignment = HorizontalAlignment.Left
                };
                Grid.SetColumn(track, 1); row.Children.Add(track);
                var amount = new TextBlock
                {
                    Text = item.Amount.ToString("N2"),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(8, 0, 0, 0)
                };
                Grid.SetColumn(amount, 2); row.Children.Add(amount); daily.Children.Add(row);
            }
        }


        private void DrawComparison(KpiData d)
        {
            comparisonChart.Children.Clear();

            double max = Math.Max(1, Math.Max(
                Math.Max(d.GrossSales, d.Cost),
                Math.Max(d.Expenses, Math.Abs(d.NetProfit))));

            AddComparisonBar("المبيعات قبل المرتجعات", d.GrossSales, max, "#16865D");
            AddComparisonBar("تكلفة البضاعة", d.Cost, max, "#B7791F");
            AddComparisonBar("المصروفات", d.Expenses, max, "#64736F");

            if (d.Complete)
                AddComparisonBar("صافي الربح", d.NetProfit, max,
                    d.NetProfit >= 0 ? "#16865D" : "#A83232");
            else
                comparisonChart.Children.Add(new TextBlock
                {
                    Text = "صافي الربح غير معروض لأن توزيع بعض البنود على التشغيلات غير مكتمل.",
                    Foreground = B("#A83232"),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(2, 5, 2, 8)
                });
        }

        private void AddComparisonBar(string title, double value, double max, string color)
        {
            var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(145) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(115) });

            var label = new TextBlock
            {
                Text = title,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetColumn(label, 0);
            row.Children.Add(label);

            var track = new Border
            {
                Height = 13,
                Background = B("#E8EFEC"),
                CornerRadius = new CornerRadius(6),
                VerticalAlignment = VerticalAlignment.Center
            };
            track.Child = new Border
            {
                Width = Math.Max(2, 320 * Math.Abs(value) / max),
                Height = 13,
                Background = B(color),
                CornerRadius = new CornerRadius(6),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            Grid.SetColumn(track, 1);
            row.Children.Add(track);

            var amount = new TextBlock
            {
                Text = value.ToString("N2"),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(8, 0, 0, 0)
            };
            Grid.SetColumn(amount, 2);
            row.Children.Add(amount);
            comparisonChart.Children.Add(row);
        }

        private void DrawAlerts(KpiData current, KpiData previous)
        {
            alerts.Children.Clear();
            int count = 0;

            if (previous.NetSales > 0 &&
                current.NetSales < previous.NetSales * 0.80)
            {
                double decrease = (previous.NetSales - current.NetSales) / previous.NetSales * 100.0;
                AddAlert("انخفاض المبيعات",
                    "انخفض صافي المبيعات بنسبة " + decrease.ToString("N1", CultureInfo.CurrentCulture) +
                    "% مقارنة بالفترة السابقة.",
                    "#A83232");
                count++;
            }

            if (previous.Expenses > 0 &&
                current.Expenses > previous.Expenses * 1.20)
            {
                double increase = (current.Expenses - previous.Expenses) / previous.Expenses * 100.0;
                AddAlert("ارتفاع المصروفات",
                    "زادت المصروفات التشغيلية بنسبة " + increase.ToString("N1", CultureInfo.CurrentCulture) +
                    "% مقارنة بالفترة السابقة.",
                    "#B7791F");
                count++;
            }

            if (current.Complete && previous.Complete && previous.NetProfit > 0 &&
                current.NetProfit < previous.NetProfit * 0.80)
            {
                double decrease = (previous.NetProfit - current.NetProfit) / previous.NetProfit * 100.0;
                AddAlert("انخفاض صافي الربح",
                    "انخفض صافي الربح بنسبة " + decrease.ToString("N1", CultureInfo.CurrentCulture) +
                    "% مقارنة بالفترة السابقة.",
                    "#A83232");
                count++;
            }

            if (current.GrossSales > 0 &&
                current.Returns / current.GrossSales > 0.10)
            {
                double ratio = current.Returns / current.GrossSales * 100.0;
                AddAlert("ارتفاع المرتجعات",
                    "تمثل المرتجعات " + ratio.ToString("N1", CultureInfo.CurrentCulture) +
                    "% من إجمالي المبيعات خلال الفترة المحددة.",
                    "#B7791F");
                count++;
            }

            if (count == 0)
            {
                alerts.Children.Add(new TextBlock
                {
                    Text = "لا توجد تنبيهات وفق الحدود المحددة للفترة الحالية.",
                    Foreground = B("#64736F"),
                    Background = Brushes.White,
                    Padding = new Thickness(12),
                    TextWrapping = TextWrapping.Wrap
                });
            }
        }

        private void AddAlert(string title, string detail, string color)
        {
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = title,
                FontWeight = FontWeights.Bold,
                Foreground = B(color),
                FontSize = 14
            });
            stack.Children.Add(new TextBlock
            {
                Text = detail,
                Foreground = B("#4B5B55"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0)
            });

            alerts.Children.Add(new Border
            {
                Background = Brushes.White,
                BorderBrush = B(color),
                BorderThickness = new Thickness(1, 1, 4, 1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12),
                Margin = new Thickness(2, 3, 2, 3),
                Child = stack
            });
        }

        private void DrawTopProducts(DateTime from, DateTime to)
        {
            topProducts.Children.Clear();

            var reportService = new SalesReportsService();
            var products = reportService.GetTopSellingProducts(userId, from, to);

            if (products == null || products.Count == 0)
            {
                topProducts.Children.Add(new TextBlock
                {
                    Text = "لا توجد منتجات مباعة في الفترة المحددة.",
                    Foreground = B("#64736F")
                });
                return;
            }

            int shown = 0;
            foreach (var product in products)
            {
                if (shown >= 5) break;

                var card = new Border
                {
                    Background = Brushes.White,
                    BorderBrush = B("#E1EAE5"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(9),
                    Padding = new Thickness(12),
                    Margin = new Thickness(2, 3, 2, 3)
                };

                var stack = new StackPanel();
                stack.Children.Add(new TextBlock
                {
                    Text = (shown + 1).ToString() + ". " + product.ProductName,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = B("#183F36"),
                    TextWrapping = TextWrapping.Wrap
                });
                stack.Children.Add(new TextBlock
                {
                    Text = "صافي الكمية: " + product.NetQuantity.ToString("N2")
                        + "    |    صافي المبيعات: " + product.NetSales.ToString("N2"),
                    Foreground = B("#64736F"),
                    Margin = new Thickness(0, 5, 0, 0),
                    TextWrapping = TextWrapping.Wrap
                });

                card.Child = stack;
                topProducts.Children.Add(card);
                shown++;
            }
        }

        private static SolidColorBrush B(string hex)

        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        }
    }

    internal sealed class KpiDashboardService
    {
        private readonly string cs;
        public KpiDashboardService()
        {
            var db = new DatabaseManager();
            cs = "Data Source=" + db.GetDatabasePath() + ";Version=3;";

            // Initialize the expense table before KPI opens its read connection.
            ExpenseService.EnsureDatabaseSchema(cs);
        }

        public KpiData Get(int userId, DateTime fromDate, DateTime toDate)
        {
            if (toDate.Date < fromDate.Date) throw new ArgumentException("نطاق التاريخ غير صحيح.");
            var d = new KpiData();
            using (var c = new SQLiteConnection(cs))
            {
                c.Open();
                string userRole = null;
                bool isActive = false;
                using (var cmd = new SQLiteCommand("SELECT Role, IsActive FROM Users WHERE Id=@Id;", c))
                {
                    cmd.Parameters.AddWithValue("@Id", userId);
                    using (var r = cmd.ExecuteReader())
                    {
                        if (r.Read())
                        {
                            userRole = Convert.ToString(r["Role"]);
                            isActive = Convert.ToInt32(r["IsActive"]) == 1;
                        }
                    }
                }

                if (!isActive)
                    throw new UnauthorizedAccessException("ليس لديك صلاحية عرض مؤشرات الأداء.");

                bool hasReportPermission = string.Equals(
                    userRole, "Manager", StringComparison.OrdinalIgnoreCase);

                if (!hasReportPermission)
                {
                    using (var cmd = new SQLiteCommand(@"
SELECT COUNT(*)
FROM RolePermissions rp
INNER JOIN Roles r ON r.Id = rp.RoleId
INNER JOIN Permissions p ON p.Id = rp.PermissionId
WHERE r.RoleCode = @Role
AND p.PermissionCode = @Permission
AND rp.IsAllowed = 1;", c))
                    {
                        cmd.Parameters.AddWithValue("@Role", userRole);
                        cmd.Parameters.AddWithValue("@Permission", "Reports.View");
                        hasReportPermission = Convert.ToInt32(cmd.ExecuteScalar()) > 0;
                    }
                }

                if (!hasReportPermission)
                    throw new UnauthorizedAccessException("ليس لديك صلاحية عرض مؤشرات الأداء.");

                string from = fromDate.Date.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                string to = toDate.Date.AddDays(1).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                Action<SQLiteCommand> dates = cmd =>
                {
                    cmd.Parameters.AddWithValue("@FromDate", from);
                    cmd.Parameters.AddWithValue("@ToDate", to);
                };

                using (var cmd = new SQLiteCommand(@"SELECT COUNT(*) AS N, COALESCE(SUM(Total),0) AS V
FROM SalesInvoices WHERE Status='Completed' AND CreatedAt>=@FromDate AND CreatedAt<@ToDate;", c))
                {
                    dates(cmd); using (var r = cmd.ExecuteReader()) if (r.Read())
                    { d.Invoices = Convert.ToInt32(r["N"]); d.GrossSales = Convert.ToDouble(r["V"]); }
                }
                using (var cmd = new SQLiteCommand(@"SELECT COALESCE(SUM(CASE
WHEN LOWER(COALESCE(sri.TaxStatus,'Legacy'))='calculated'
THEN COALESCE(sri.TaxableAmount,0)+COALESCE(sri.VatAmount,0)+COALESCE(sri.ScheduleTaxAmount,0)
ELSE COALESCE(sri.Total,0)-COALESCE(sri.DiscountAmount,0) END),0)
FROM SalesReturnItems sri INNER JOIN SalesReturns sr ON sr.Id=sri.ReturnId
WHERE sr.Status='Completed' AND sr.CreatedAt>=@FromDate AND sr.CreatedAt<@ToDate;", c))
                { dates(cmd); d.Returns = Convert.ToDouble(cmd.ExecuteScalar()); }

                using (var cmd = new SQLiteCommand(@"SELECT COALESCE(SUM(a.Quantity*b.PurchasePrice),0)
FROM SalesInvoiceBatchAllocations a
JOIN SalesInvoiceItems si ON si.Id=a.InvoiceItemId
JOIN SalesInvoices inv ON inv.Id=si.InvoiceId
JOIN Batches b ON b.Id=a.BatchId
WHERE inv.Status='Completed' AND inv.CreatedAt>=@FromDate AND inv.CreatedAt<@ToDate;", c))
                { dates(cmd); d.Cost = Convert.ToDouble(cmd.ExecuteScalar()); }

                using (var cmd = new SQLiteCommand(@"SELECT COALESCE(SUM(a.Quantity*b.PurchasePrice),0)
FROM SalesReturnBatchAllocations a
JOIN SalesReturnItems sri ON sri.Id=a.ReturnItemId
JOIN SalesReturns sr ON sr.Id=sri.ReturnId
JOIN Batches b ON b.Id=a.BatchId
WHERE sr.Status='Completed' AND sr.CreatedAt>=@FromDate AND sr.CreatedAt<@ToDate AND a.Restocked=1;", c))
                { dates(cmd); d.Cost -= Convert.ToDouble(cmd.ExecuteScalar()); }

                using (var cmd = new SQLiteCommand(@"SELECT COALESCE(SUM(Amount),0) FROM ExpenseTransactions
WHERE IsVoided=0 AND CreatedAt>=@FromDate AND CreatedAt<@ToDate;", c))
                { dates(cmd); d.Expenses = Convert.ToDouble(cmd.ExecuteScalar()); }

                using (var cmd = new SQLiteCommand("SELECT COALESCE(SUM(Quantity*PurchasePrice),0) FROM Batches WHERE Quantity>0;", c))
                    d.StockValue = Convert.ToDouble(cmd.ExecuteScalar());

                using (var cmd = new SQLiteCommand(@"SELECT COUNT(*) FROM SalesInvoiceItems si
JOIN SalesInvoices inv ON inv.Id=si.InvoiceId
WHERE inv.Status='Completed' AND inv.CreatedAt>=@FromDate AND inv.CreatedAt<@ToDate
AND (si.PackageQuantity<=0 OR ABS(si.PackageQuantity-COALESCE(
(SELECT SUM(a.Quantity) FROM SalesInvoiceBatchAllocations a WHERE a.InvoiceItemId=si.Id),0))>0.000001);", c))
                { dates(cmd); d.IncompleteSales = Convert.ToInt32(cmd.ExecuteScalar()); }

                using (var cmd = new SQLiteCommand(@"SELECT COUNT(*) FROM SalesReturnItems sri
JOIN SalesReturns sr ON sr.Id=sri.ReturnId
WHERE sr.Status='Completed' AND sr.CreatedAt>=@FromDate AND sr.CreatedAt<@ToDate
AND (sri.PackageQuantity<=0 OR ABS(sri.PackageQuantity-COALESCE(
(SELECT SUM(a.Quantity) FROM SalesReturnBatchAllocations a WHERE a.ReturnItemId=sri.Id),0))>0.000001);", c))
                { dates(cmd); d.IncompleteReturns = Convert.ToInt32(cmd.ExecuteScalar()); }

                using (var cmd = new SQLiteCommand(@"SELECT substr(CreatedAt,1,10) AS D, COALESCE(SUM(Total),0) AS V
FROM SalesInvoices WHERE Status='Completed' AND CreatedAt>=@FromDate AND CreatedAt<@ToDate
GROUP BY substr(CreatedAt,1,10) ORDER BY D;", c))
                {
                    dates(cmd); using (var r = cmd.ExecuteReader()) while (r.Read())
                        d.Days.Add(new KpiDay { Date = Convert.ToString(r["D"]), Amount = Convert.ToDouble(r["V"]) });
                }
            }
            d.NetSales = d.GrossSales - d.Returns;
            d.NetProfit = d.Complete ? d.NetSales - d.Cost - d.Expenses : 0;
            d.Average = d.Invoices == 0 ? 0 : d.GrossSales / d.Invoices;
            return d;
        }
    }

    internal sealed class KpiData
    {
        public int Invoices, IncompleteSales, IncompleteReturns;
        public double GrossSales, Returns, NetSales, Cost, Expenses, NetProfit, Average, StockValue;
        public bool Complete { get { return IncompleteSales == 0 && IncompleteReturns == 0; } }
        public System.Collections.Generic.List<KpiDay> Days = new System.Collections.Generic.List<KpiDay>();
    }
    internal sealed class KpiDay { public string Date; public double Amount; }
}