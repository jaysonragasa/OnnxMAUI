using EssentialsAI.ViewModels;

namespace EssentialsAI.Views;

public partial class AiPage : ContentPage
{
	public AiPage(AiViewModel viewModel)
	{
		InitializeComponent();
		BindingContext = viewModel;
	}
}
