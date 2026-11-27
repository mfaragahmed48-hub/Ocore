
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Ocore.desktop.Models;
using Ocore.desktop.Services;

namespace Ocore.desktop.Views
{
    public partial class TaxManagementWindow : Window
    {
        private readonly int currentUserId;
        private readonly TaxProfileService taxService;

        private ObservableCollection<TaxProfile> profiles;
        private ObservableCollection<TaxProfileRate> rates;

        private TaxProfile currentProfile;
        private bool isLoading;

        public TaxManagementWindow(int userId)
        {
            InitializeComponent();

            currentUserId = userId;
            taxService = new TaxProfileService();

            profiles = new ObservableCollection<TaxProfile>();
            rates = new ObservableCollection<TaxProfileRate>();

            ProfilesDataGrid.ItemsSource = profiles;
            RatesDataGrid.ItemsSource = rates;

            LoadProfiles();
            StartNewProfile();
        }

        private string GetComboTag(ComboBox comboBox)
        {
            ComboBoxItem item = comboBox.SelectedItem as ComboBoxItem;

            return item == null
                ? ""
                : Convert.ToString(item.Tag);
        }

        private void SelectComboTag(ComboBox comboBox, string tag)
        {
            foreach (ComboBoxItem item in comboBox.Items)
            {
                if (string.Equals(
                    Convert.ToString(item.Tag),
                    tag,
                    StringComparison.OrdinalIgnoreCase))
                {
                    comboBox.SelectedItem = item;
                    return;
                }
            }

            comboBox.SelectedIndex = 0;
        }

        private void LoadProfiles(int? selectedProfileId = null)
        {
            try
            {
                isLoading = true;

                var result = taxService.GetAllProfiles(true);

                profiles.Clear();

                foreach (var profile in result)
                    profiles.Add(profile);

                if (selectedProfileId.HasValue)
                {
                    var selected = profiles.FirstOrDefault(
                        x => x.Id == selectedProfileId.Value);

                    if (selected != null)
                    {
                        ProfilesDataGrid.SelectedItem = selected;
                        ProfilesDataGrid.ScrollIntoView(selected);
                        LoadProfile(selected);
                    }
                }
            }
            catch (Exception ex)
            {
                ErrorDialog.Show("تعذر تحميل التصنيفات الضريبية", ex);
            }
            finally
            {
                isLoading = false;
            }
        }

        private void ProfilesDataGrid_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (isLoading)
                return;

            TaxProfile selected =
                ProfilesDataGrid.SelectedItem as TaxProfile;

            if (selected != null)
                LoadProfile(selected);
        }

        private void LoadProfile(TaxProfile profile)
        {
            if (profile == null)
                return;

            currentProfile = taxService.GetProfileById(profile.Id);

            if (currentProfile == null)
                return;

            CodeTextBox.Text = currentProfile.Code;
            NameTextBox.Text = currentProfile.Name;
            DescriptionTextBox.Text = currentProfile.Description;
            IsActiveCheckBox.IsChecked = currentProfile.IsActive;

            SelectComboTag(
                TaxTypeComboBox,
                currentProfile.TaxType);

            rates.Clear();

            foreach (var rate in currentProfile.Rates)
                rates.Add(CloneRate(rate));

            bool systemProfile = currentProfile.IsSystem;

            CodeTextBox.IsReadOnly = systemProfile;
            NameTextBox.IsReadOnly = systemProfile;
            DescriptionTextBox.IsReadOnly = systemProfile;
            TaxTypeComboBox.IsEnabled = !systemProfile;
            IsActiveCheckBox.IsEnabled = !systemProfile;

            SaveProfileButton.IsEnabled = !systemProfile;
            DeactivateProfileButton.IsEnabled =
                !systemProfile && currentProfile.IsActive;

            ConfigureRateControls();

            StatusTextBlock.Text = systemProfile
                ? "هذا تصنيف نظامي محمي ولا يمكن تعديله."
                : "تم تحميل التصنيف. السجلات الضريبية المحفوظة لا تقبل تعديل النسبة أو تاريخ البداية.";
        }

        private TaxProfileRate CloneRate(TaxProfileRate source)
        {
            return new TaxProfileRate
            {
                Id = source.Id,
                TaxProfileId = source.TaxProfileId,
                ComponentCode = source.ComponentCode,
                ComponentName = source.ComponentName,
                ComponentType = source.ComponentType,
                RatePercent = source.RatePercent,
                CalculationOrder = source.CalculationOrder,
                IsCompound = source.IsCompound,
                EffectiveFrom = source.EffectiveFrom,
                EffectiveTo = source.EffectiveTo,
                IsActive = source.IsActive,
                CreatedBy = source.CreatedBy,
                CreatedAt = source.CreatedAt
            };
        }

        private void StartNewProfile()
        {
            currentProfile = new TaxProfile
            {
                Id = 0,
                IsActive = true,
                IsSystem = false,
                TaxType = "Standard"
            };

            CodeTextBox.Text = "";
            NameTextBox.Text = "";
            DescriptionTextBox.Text = "";
            IsActiveCheckBox.IsChecked = true;

            SelectComboTag(TaxTypeComboBox, "Standard");

            rates.Clear();

            CodeTextBox.IsReadOnly = false;
            NameTextBox.IsReadOnly = false;
            DescriptionTextBox.IsReadOnly = false;
            TaxTypeComboBox.IsEnabled = true;
            IsActiveCheckBox.IsEnabled = true;

            SaveProfileButton.IsEnabled = true;
            DeactivateProfileButton.IsEnabled = false;

            ClearNewRateFields();

            ReasonTextBox.Text = "";
            StatusTextBlock.Text = "إضافة تصنيف ضريبي جديد.";

            ConfigureRateControls();
        }

        private void NewProfileButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            ProfilesDataGrid.SelectedItem = null;
            StartNewProfile();
        }

        private void ConfigureRateControls()
        {
            bool isExempt =
                GetComboTag(TaxTypeComboBox) == "Exempt";

            bool systemProfile =
                currentProfile != null && currentProfile.IsSystem;

            ComponentCodeTextBox.IsEnabled =
                !isExempt && !systemProfile;

            ComponentNameTextBox.IsEnabled =
                !isExempt && !systemProfile;

            ComponentTypeComboBox.IsEnabled =
                !isExempt && !systemProfile;

            RatePercentTextBox.IsEnabled =
                !isExempt && !systemProfile;

            CalculationOrderTextBox.IsEnabled =
                !isExempt && !systemProfile;

            EffectiveFromTextBox.IsEnabled =
                !isExempt && !systemProfile;

            IsCompoundCheckBox.IsEnabled =
                !isExempt && !systemProfile;

            AddRateButton.IsEnabled =
                !isExempt && !systemProfile;

            CloseRateDateTextBox.IsEnabled =
                !systemProfile;

            CloseRateButton.IsEnabled =
                !systemProfile;

            if (isExempt)
            {
                StatusTextBlock.Text =
                    "التصنيف المعفى لا يحتاج إلى مكونات أو نسب ضريبية.";
            }
        }

        private void TaxTypeComboBox_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (currentProfile == null)
                return;

            ConfigureRateControls();
        }

        private void ClearNewRateFields()
        {
            ComponentCodeTextBox.Text = "";
            ComponentNameTextBox.Text = "";
            RatePercentTextBox.Text = "";
            CalculationOrderTextBox.Text = "1";
            EffectiveFromTextBox.Text =
                DateTime.Today.ToString(
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture);

            IsCompoundCheckBox.IsChecked = false;
            SelectComboTag(ComponentTypeComboBox, "VAT");
        }

        private bool TryParseDate(
            string value,
            out DateTime date)
        {
            return DateTime.TryParseExact(
                value.Trim(),
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out date);
        }

        private void AddRateButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            try
            {
                if (GetComboTag(TaxTypeComboBox) == "Exempt")
                {
                    MessageBox.Show(
                        "التصنيف المعفى لا يحتاج إلى إضافة نسب.",
                        "إدارة الضرائب",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                string code = ComponentCodeTextBox.Text.Trim();
                string name = ComponentNameTextBox.Text.Trim();
                string type = GetComboTag(ComponentTypeComboBox);

                double rateValue;
                int orderValue;
                DateTime effectiveFrom;

                if (string.IsNullOrWhiteSpace(code) ||
                    string.IsNullOrWhiteSpace(name))
                {
                    throw new ArgumentException(
                        "أدخل كود المكون واسمه.");
                }

                if (!double.TryParse(
                    RatePercentTextBox.Text.Trim(),
                    NumberStyles.Number,
                    CultureInfo.InvariantCulture,
                    out rateValue))
                {
                    throw new ArgumentException(
                        "أدخل نسبة ضريبية صحيحة باستخدام النقطة العشرية عند الحاجة.");
                }

                if (rateValue < 0 ||
                    double.IsNaN(rateValue) ||
                    double.IsInfinity(rateValue))
                {
                    throw new ArgumentException(
                        "النسبة الضريبية غير صالحة.");
                }

                if (!int.TryParse(
                    CalculationOrderTextBox.Text.Trim(),
                    out orderValue) ||
                    orderValue < 1)
                {
                    throw new ArgumentException(
                        "ترتيب الحساب يجب أن يكون رقمًا صحيحًا يبدأ من 1.");
                }

                if (!TryParseDate(
                    EffectiveFromTextBox.Text,
                    out effectiveFrom))
                {
                    throw new ArgumentException(
                        "تاريخ بداية السريان يجب أن يكون بصيغة yyyy-MM-dd.");
                }

                if (string.IsNullOrWhiteSpace(type))
                    throw new ArgumentException("اختر نوع المكون الضريبي.");

                var newRate = new TaxProfileRate
                {
                    Id = 0,
                    TaxProfileId = currentProfile.Id,
                    ComponentCode = code,
                    ComponentName = name,
                    ComponentType = type,
                    RatePercent = rateValue,
                    CalculationOrder = orderValue,
                    IsCompound = IsCompoundCheckBox.IsChecked == true,
                    EffectiveFrom = effectiveFrom.Date,
                    EffectiveTo = null,
                    IsActive = true
                };

                rates.Add(newRate);

                ClearNewRateFields();

                StatusTextBlock.Text =
                    "تمت إضافة المكون إلى قائمة الحفظ. اضغط حفظ التصنيف لتسجيله في قاعدة البيانات.";
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "بيانات المكون الضريبي",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void CloseRateButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            try
            {
                TaxProfileRate selected =
                    RatesDataGrid.SelectedItem as TaxProfileRate;

                if (selected == null)
                {
                    throw new InvalidOperationException(
                        "حدد سجل النسبة المطلوب إغلاق فترة سريانه.");
                }

                if (selected.Id <= 0)
                {
                    throw new InvalidOperationException(
                        "لا يمكن إغلاق فترة سجل لم يتم حفظه بعد.");
                }

                if (selected.EffectiveTo.HasValue)
                {
                    throw new InvalidOperationException(
                        "فترة هذا السجل مغلقة بالفعل.");
                }

                DateTime endDate;

                if (!TryParseDate(
                    CloseRateDateTextBox.Text,
                    out endDate))
                {
                    throw new ArgumentException(
                        "أدخل تاريخ النهاية بصيغة yyyy-MM-dd.");
                }

                if (endDate.Date < selected.EffectiveFrom.Date)
                {
                    throw new ArgumentException(
                        "تاريخ النهاية لا يمكن أن يسبق تاريخ بداية السريان.");
                }

                MessageBoxResult confirmation =
                    MessageBox.Show(
                        "سيتم إغلاق فترة سريان هذا المكون بتاريخ " +
                        endDate.ToString("yyyy-MM-dd") +
                        ".\nلن يتم تعديل النسبة أو حذف السجل.\n\nهل تريد المتابعة؟",
                        "تأكيد إغلاق فترة السريان",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question,
                        MessageBoxResult.No);

                if (confirmation != MessageBoxResult.Yes)
                    return;

                selected.EffectiveTo = endDate.Date;
                RatesDataGrid.Items.Refresh();

                StatusTextBlock.Text =
                    "تم تحديد تاريخ نهاية السريان. احفظ التصنيف لتسجيل التغيير في سجل التدقيق.";
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "إغلاق فترة السريان",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void SaveProfileButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            try
            {
                if (currentProfile == null)
                    StartNewProfile();

                string taxType = GetComboTag(TaxTypeComboBox);

                if (string.IsNullOrWhiteSpace(taxType))
                    throw new ArgumentException("اختر نوع المعاملة الضريبية.");

                var profile = new TaxProfile
                {
                    Id = currentProfile.Id,
                    Code = CodeTextBox.Text.Trim(),
                    Name = NameTextBox.Text.Trim(),
                    TaxType = taxType,
                    Description = DescriptionTextBox.Text.Trim(),
                    IsActive = IsActiveCheckBox.IsChecked == true,
                    IsSystem = currentProfile.IsSystem,
                    Rates = rates.Select(CloneRate).ToList()
                };

                int savedId = taxService.SaveProfile(
                    profile,
                    currentUserId,
                    ReasonTextBox.Text.Trim());

                MessageBox.Show(
                    "تم حفظ التصنيف الضريبي ومكوناته بنجاح.",
                    "إدارة الضرائب",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                LoadProfiles(savedId);
            }
            catch (UnauthorizedAccessException ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "صلاحيات غير كافية",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                ErrorDialog.Show("تعذر حفظ التصنيف الضريبي", ex);
            }
        }

        private void DeactivateProfileButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            try
            {
                if (currentProfile == null ||
                    currentProfile.Id <= 0)
                {
                    return;
                }

                if (currentProfile.IsSystem)
                {
                    throw new InvalidOperationException(
                        "لا يمكن تعطيل التصنيفات النظامية.");
                }

                MessageBoxResult confirmation =
                    MessageBox.Show(
                        "سيتم تعطيل التصنيف الضريبي دون حذف سجلاته أو نسبه.\n\nهل تريد المتابعة؟",
                        "تأكيد تعطيل التصنيف",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning,
                        MessageBoxResult.No);

                if (confirmation != MessageBoxResult.Yes)
                    return;

                taxService.DeactivateProfile(
                    currentProfile.Id,
                    currentUserId,
                    ReasonTextBox.Text.Trim());

                MessageBox.Show(
                    "تم تعطيل التصنيف مع الاحتفاظ بسجلاته.",
                    "إدارة الضرائب",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                LoadProfiles();
                StartNewProfile();
            }
            catch (UnauthorizedAccessException ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "صلاحيات غير كافية",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                ErrorDialog.Show("تعذر تعطيل التصنيف", ex);
            }
        }

        private void CloseButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            Close();
        }
    }
}
