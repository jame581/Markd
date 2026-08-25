using System;
using Markd.ViewModels;
using Microsoft.Maui.Controls;

namespace Markd.Pages
{
    public partial class ExportImportPage : ContentPage
    {
        private readonly ExportImportViewModel _viewModel;

        public ExportImportPage(ExportImportViewModel vm)
        {
            InitializeComponent();
            _viewModel = vm;
            BindingContext = vm;
        }

        private async void OnImportJsonClicked(object? sender, EventArgs e)
        {
            var confirmed = await DisplayAlert(
                "Replace current data?",
                "Importing a JSON backup will replace your current categories, occasions, milestones, and settings.",
                "Replace data",
                "Cancel");

            if (!confirmed)
            {
                _viewModel.Status = "Import canceled.";
                return;
            }

            await _viewModel.ImportJsonAsync();
        }
    }
}
