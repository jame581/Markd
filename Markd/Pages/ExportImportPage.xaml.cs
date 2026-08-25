using System;
using Microsoft.Maui.Controls;
using Microsoft.Extensions.DependencyInjection;
using Markd.ViewModels;

namespace Markd.Pages
{
    public partial class ExportImportPage : ContentPage
    {
        public ExportImportPage(ExportImportViewModel vm)
        {
            InitializeComponent();
            BindingContext = vm;
        }
    }
}
