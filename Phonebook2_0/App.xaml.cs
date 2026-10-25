using System.Collections.ObjectModel;
using Phonebook2_0.Models;

namespace Phonebook2_0;

public partial class App : Application
{
    // Shared collection of contacts.
    public static ObservableCollection<Models.Contact> Contacts { get; } = new();

    public App()
    {
        InitializeComponent();
    }

    protected override Window CreateWindow(
        IActivationState? activationState)
    {
        return new Window(
            new NavigationPage(new MainPage())
        );
    }
}
