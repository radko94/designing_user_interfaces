using System.ComponentModel;
using SecureMesh.App.Services;
using SecureMesh.Core.Domain;
using Contact = SecureMesh.Core.Domain.Contact;

namespace SecureMesh.App.Pages;

public partial class NewGroupPage : ContentPage
{
    private readonly GroupChatService _groups;
    private readonly ContactsService _contacts;
    private readonly List<SelectableContact> _items = new();

    public NewGroupPage()
    {
        InitializeComponent();
        _groups = ServiceHelper.Get<GroupChatService>();
        _contacts = ServiceHelper.Get<ContactsService>();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        _items.Clear();
        _items.AddRange(_contacts.ListContacts()
            .OrderBy(c => c.Nickname, StringComparer.CurrentCulture)
            .Select(c => new SelectableContact(c)));
        ContactsView.ItemsSource = _items;
    }

    private async void OnCreateClicked(object? sender, EventArgs e)
    {
        var name = GroupNameEntry.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            await DisplayAlertAsync("Липсва име", "Въведете име на групата.", "Добре");
            return;
        }

        var members = _items.Where(i => i.IsSelected).Select(i => i.Contact).ToList();
        if (members.Count == 0)
        {
            await DisplayAlertAsync("Няма участници", "Изберете поне един участник.", "Добре");
            return;
        }

        try
        {
            _groups.CreateGroup(name, members);
            await Shell.Current.GoToAsync("..");
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Грешка", $"Групата не беше създадена: {ex.Message}", "Добре");
        }
    }

    private sealed class SelectableContact : INotifyPropertyChanged
    {
        private bool _isSelected;

        public SelectableContact(Contact contact) => Contact = contact;

        public Contact Contact { get; }
        public string Name => Contact.Nickname;

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value)
                    return;
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
