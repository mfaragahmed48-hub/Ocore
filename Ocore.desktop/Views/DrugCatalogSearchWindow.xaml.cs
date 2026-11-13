
using Ocore.desktop.Models;
using Ocore.desktop.Services;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Ocore.desktop.Views
{
    public partial class DrugCatalogSearchWindow : Window
    {
        private readonly DrugCatalogService drugCatalogService;

        public DrugCatalogItem SelectedDrug { get; private set; }

        public DrugCatalogSearchWindow()
        {
            InitializeComponent();

            drugCatalogService = new DrugCatalogService();
        }

        private void SearchDrugs()
        {
            try
            {
                string searchText = SearchTextBox.Text.Trim();

                if (string.IsNullOrWhiteSpace(searchText))
                {
                    DrugsDataGrid.ItemsSource = null;
                    ResultsCountTextBlock.Text = "عدد النتائج: 0";
                    return;
                }

                List<DrugCatalogItem> results =
                    drugCatalogService.SearchDrugs(searchText);

                DrugsDataGrid.ItemsSource = results;

                ResultsCountTextBlock.Text =
                    "عدد النتائج: " + results.Count;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "حدث خطأ أثناء البحث في دليل الأدوية:\n" + ex.Message,
                    "خطأ",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void SearchTextBox_TextChanged(
            object sender,
            TextChangedEventArgs e)
        {
            SearchDrugs();
        }

        private void SearchButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            SearchDrugs();
        }

        private void SelectDrugButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            SelectDrug();
        }

        private void DrugsDataGrid_MouseDoubleClick(
            object sender,
            MouseButtonEventArgs e)
        {
            SelectDrug();
        }

        private void SelectDrug()
        {
            SelectedDrug = DrugsDataGrid.SelectedItem as DrugCatalogItem;

            if (SelectedDrug == null)
            {
                MessageBox.Show(
                    "من فضلك اختر دواء من الجدول أولاً.",
                    "تنبيه",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            DialogResult = true;
            Close();
        }

        private void CloseButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
