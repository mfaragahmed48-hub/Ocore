
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Ocore.desktop.Services;

namespace Ocore.desktop.Views
{
    public partial class DashboardWindow : Window
    {
        private ShiftHandoverService shiftHandoverService;

        private ShiftHandoverService HandoverService
        {
            get
            {
                if (shiftHandoverService == null)
                    shiftHandoverService = new ShiftHandoverService();

                return shiftHandoverService;
            }
        }

        // ==========================================
        // تحديث أزرار التسليم والاستلام ومراجعة الشيفت
        // ==========================================

        private void DashboardWindow_ShiftHandover_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            Activated -= DashboardWindow_ShiftHandover_Activated;
            Activated += DashboardWindow_ShiftHandover_Activated;

            RefreshShiftHandoverButtons();
        }

        private void DashboardWindow_ShiftHandover_Activated(
            object sender,
            EventArgs e)
        {
            RefreshShiftHandoverButtons();
        }

        private void RefreshShiftHandoverButtons()
        {
            try
            {
                bool canOperate =
                    string.Equals(
                        currentRole,
                        "Pharmacist",
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        currentRole,
                        "Manager",
                        StringComparison.OrdinalIgnoreCase);

                // تحديد صلاحية المدير وإظهار زر مراجعة الشيفت
                bool isManager = string.Equals(
                    currentRole,
                    "Manager",
                    StringComparison.OrdinalIgnoreCase);

                var currentShift = shiftService.GetCurrentShift();

                ReviewCurrentShiftButton.Visibility =
                    isManager
                        ? Visibility.Visible
                        : Visibility.Collapsed;

                ReviewCurrentShiftButton.IsEnabled =
                    isManager && currentShift != null;

                var info = HandoverService.GetCurrentHandoverInfo();

                if (info == null)
                {
                    HandoverShiftButton.IsEnabled = false;
                    TakeOverShiftButton.IsEnabled = false;
                    return;
                }

                bool isCurrentResponsible =
                    info.CurrentResponsibleUserId == currentUserId;

                HandoverShiftButton.IsEnabled =
                    canOperate && isCurrentResponsible;

                TakeOverShiftButton.IsEnabled =
                    canOperate && !isCurrentResponsible;

                HandoverShiftButton.ToolTip =
                    isCurrentResponsible
                        ? "تسليم مسؤولية الشيفت إلى صيدلي آخر"
                        : "التسليم متاح للمسؤول الحالي عن الشيفت فقط";

                TakeOverShiftButton.ToolTip =
                    !isCurrentResponsible
                        ? "استلام مسؤولية الشيفت من المسؤول السابق"
                        : "أنت المسؤول الحالي عن الشيفت";
            }
            catch (Exception ex)
            {
                HandoverShiftButton.IsEnabled = false;
                TakeOverShiftButton.IsEnabled = false;

                ReviewCurrentShiftButton.IsEnabled = false;
                ReviewCurrentShiftButton.Visibility =
                    Visibility.Collapsed;

                System.Diagnostics.Debug.WriteLine(
                    "RefreshShiftHandoverButtons error: " + ex.Message);
            }
        }

        // ==========================================
        // تسليم الشيفت إلى صيدلي آخر
        // ==========================================

        private void HandoverShiftButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            try
            {
                var info = HandoverService.GetCurrentHandoverInfo();

                if (info == null)
                {
                    MessageBox.Show(
                        "لا يوجد شيفت مفتوح لتسليمه.",
                        "تسليم الشيفت",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    RefreshShiftHandoverButtons();
                    return;
                }

                if (info.CurrentResponsibleUserId != currentUserId)
                {
                    MessageBox.Show(
                        "لا يمكنك تسليم الشيفت لأنك لست المسؤول الحالي عنه.",
                        "تسليم الشيفت",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    RefreshShiftHandoverButtons();
                    return;
                }

                ShowShiftTransferDialog(false);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "تعذر تجهيز عملية تسليم الشيفت:\n\n" + ex.Message,
                    "O CORE",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        // ==========================================
        // استلام الشيفت اضطراريًا
        // ==========================================

        private void TakeOverShiftButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            try
            {
                var info = HandoverService.GetCurrentHandoverInfo();

                if (info == null)
                {
                    MessageBox.Show(
                        "لا يوجد شيفت مفتوح لاستلامه.",
                        "استلام الشيفت",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    RefreshShiftHandoverButtons();
                    return;
                }

                if (info.CurrentResponsibleUserId == currentUserId)
                {
                    MessageBox.Show(
                        "أنت المسؤول الحالي عن الشيفت بالفعل.",
                        "استلام الشيفت",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    RefreshShiftHandoverButtons();
                    return;
                }

                ShowShiftTransferDialog(true);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "تعذر تجهيز عملية استلام الشيفت:\n\n" + ex.Message,
                    "O CORE",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        // ==========================================
        // نافذة التسليم أو الاستلام
        // ==========================================

        private void ShowShiftTransferDialog(bool emergencyTakeover)
        {
            var info = HandoverService.GetCurrentHandoverInfo();

            if (info == null)
                throw new InvalidOperationException(
                    "لا يوجد شيفت مفتوح.");

            var users = HandoverService.GetEligibleReceivers()
                .Where(u => u.Id != currentUserId)
                .ToList();

            ComboBox receiverCombo = null;

            if (!emergencyTakeover && users.Count == 0)
            {
                MessageBox.Show(
                    "لا يوجد مستخدم آخر نشط ومصرح له باستلام الشيفت.",
                    "تسليم الشيفت",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            Window dialog = new Window
            {
                Title = emergencyTakeover
                    ? "استلام الشيفت"
                    : "تسليم الشيفت",

                Width = 500,
                Height = emergencyTakeover ? 360 : 410,
                MinWidth = 450,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize,
                FlowDirection = FlowDirection.RightToLeft,
                Background = new SolidColorBrush(
                    Color.FromRgb(243, 246, 248))
            };

            StackPanel root = new StackPanel
            {
                Margin = new Thickness(22)
            };

            root.Children.Add(new TextBlock
            {
                Text = emergencyTakeover
                    ? "استلام مسؤولية الشيفت الحالي"
                    : "تسليم مسؤولية الشيفت إلى صيدلي آخر",

                FontSize = 19,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(
                    Color.FromRgb(24, 63, 54)),

                Margin = new Thickness(0, 0, 0, 14)
            });

            root.Children.Add(new TextBlock
            {
                Text = "المسؤول الحالي: " +
                       info.CurrentResponsibleName,

                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(
                    Color.FromRgb(70, 90, 83)),

                Margin = new Thickness(0, 0, 0, 12)
            });

            if (!emergencyTakeover)
            {
                root.Children.Add(new TextBlock
                {
                    Text = "اختر الصيدلي المستلم",
                    FontSize = 13,
                    Foreground = new SolidColorBrush(
                        Color.FromRgb(100, 115, 111)),
                    Margin = new Thickness(0, 0, 0, 6)
                });

                receiverCombo = new ComboBox
                {
                    Height = 36,
                    FontSize = 14,
                    Padding = new Thickness(6),
                    DisplayMemberPath = "FullName",
                    ItemsSource = users,
                    SelectedIndex = 0
                };

                root.Children.Add(receiverCombo);
            }
            else
            {
                root.Children.Add(new TextBlock
                {
                    Text =
                        "سيتم تسجيل أنك استلمت الشيفت من المسؤول السابق " +
                        "دون إغلاق الشيفت أو تصفير حساباته.",

                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 13,
                    Foreground = new SolidColorBrush(
                        Color.FromRgb(100, 115, 111)),
                    Margin = new Thickness(0, 0, 0, 12)
                });
            }

            root.Children.Add(new TextBlock
            {
                Text = "ملاحظات",
                FontSize = 13,
                Foreground = new SolidColorBrush(
                    Color.FromRgb(100, 115, 111)),
                Margin = new Thickness(0, 14, 0, 6)
            });

            TextBox notesBox = new TextBox
            {
                Height = emergencyTakeover ? 65 : 75,
                FontSize = 13,
                Padding = new Thickness(8),
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,

                Text = emergencyTakeover
                    ? "استلام اضطراري بسبب انقطاع الكهرباء أو مغادرة المسؤول السابق."
                    : string.Empty
            };

            root.Children.Add(notesBox);

            StackPanel buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 18, 0, 0)
            };

            Button confirmButton = new Button
            {
                Content = emergencyTakeover
                    ? "تأكيد الاستلام"
                    : "تأكيد التسليم",

                Width = 145,
                Height = 40,
                Margin = new Thickness(0, 0, 10, 0),

                Background = new SolidColorBrush(
                    emergencyTakeover
                        ? Color.FromRgb(217, 164, 65)
                        : Color.FromRgb(36, 107, 88)),

                Foreground = emergencyTakeover
                    ? new SolidColorBrush(
                        Color.FromRgb(24, 63, 54))
                    : Brushes.White,

                FontWeight = FontWeights.Bold,
                BorderThickness = new Thickness(0)
            };

            Button cancelButton = new Button
            {
                Content = "إلغاء",
                Width = 95,
                Height = 40,

                Background = new SolidColorBrush(
                    Color.FromRgb(225, 231, 228)),

                Foreground = new SolidColorBrush(
                    Color.FromRgb(24, 63, 54)),

                BorderThickness = new Thickness(0)
            };

            confirmButton.Click += delegate
            {
                try
                {
                    bool success;

                    if (emergencyTakeover)
                    {
                        var confirm = MessageBox.Show(
                            "هل تؤكد استلام مسؤولية الشيفت من " +
                            info.CurrentResponsibleName +
                            "؟\n\nسيتم تسجيل اسمك ووقت الاستلام.",
                            "تأكيد الاستلام",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Question);

                        if (confirm != MessageBoxResult.Yes)
                            return;

                        success = HandoverService.TakeOverCurrentShift(
                            currentUserId,
                            notesBox.Text);
                    }
                    else
                    {
                        var selectedUser =
                            receiverCombo.SelectedItem as
                                ShiftHandoverService.ShiftUserInfo;

                        if (selectedUser == null)
                        {
                            MessageBox.Show(
                                "اختر الصيدلي الذي سيستلم الشيفت.",
                                "بيانات ناقصة",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);

                            return;
                        }

                        var confirm = MessageBox.Show(
                            "هل تريد تسليم الشيفت إلى " +
                            selectedUser.FullName +
                            "؟\n\nسيظل الشيفت مفتوحًا وتنتقل إليه المسؤولية.",
                            "تأكيد التسليم",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Question);

                        if (confirm != MessageBoxResult.Yes)
                            return;

                        success = HandoverService.HandoverCurrentShift(
                            currentUserId,
                            selectedUser.Id,
                            notesBox.Text);
                    }

                    if (!success)
                    {
                        MessageBox.Show(
                            "لم تكتمل العملية.",
                            "O CORE",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);

                        return;
                    }

                    dialog.DialogResult = true;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        "تعذر تنفيذ العملية:\n\n" + ex.Message,
                        "O CORE",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            };

            cancelButton.Click += delegate
            {
                dialog.DialogResult = false;
            };

            buttons.Children.Add(confirmButton);
            buttons.Children.Add(cancelButton);

            root.Children.Add(buttons);

            dialog.Content = root;

            bool? result = dialog.ShowDialog();

            if (result == true)
            {
                MessageBox.Show(
                    emergencyTakeover
                        ? "تم استلام الشيفت بنجاح. الشيفت ما زال مفتوحًا."
                        : "تم تسليم الشيفت بنجاح. الشيفت ما زال مفتوحًا.",

                    emergencyTakeover
                        ? "استلام الشيفت"
                        : "تسليم الشيفت",

                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }

            RefreshShiftHandoverButtons();
            RefreshShiftSummary();
        }
    }
}
