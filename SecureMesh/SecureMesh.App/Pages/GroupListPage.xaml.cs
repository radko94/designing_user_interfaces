using SecureMesh.App.Services;
using SecureMesh.Core.Domain;

namespace SecureMesh.App.Pages;

public partial class GroupListPage : ContentPage
{
    private readonly GroupChatService _groups;

    public GroupListPage()
    {
        InitializeComponent();
        _groups = ServiceHelper.Get<GroupChatService>();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        Refresh();
    }

    private void Refresh()
    {
        var items = _groups.ListGroups()
            .OrderBy(g => g.Name, StringComparer.CurrentCulture)
            .Select(g =>
            {
                var memberCount = _groups.ListMembers(g.Id).Count;
                return new GroupItem(g.Id, g.Name, memberCount);
            })
            .ToList();

        GroupsView.ItemsSource = items;
    }

    private async void OnGroupSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not GroupItem item)
            return;

        GroupsView.SelectedItem = null;
        await Shell.Current.GoToAsync($"{nameof(GroupChatPage)}?groupId={item.Id}");
    }

    private async void OnAddGroupClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync(nameof(NewGroupPage));
    }

    private sealed record GroupItem(int Id, string Name, int MemberCount)
    {
        public string MemberCountLabel => MemberCount == 1 ? "1 участник" : $"{MemberCount} участници";
    }
}
