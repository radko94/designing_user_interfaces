namespace Phonebook2_0.Models;

public sealed class Contact
{
    public string Name { get; }
    public string Phone { get; }
    public string PictureUrl { get; }

    public Contact(string name, string phone, string pictureUrl)
    {
        Name = name;
        Phone = phone;
        PictureUrl = pictureUrl;
    }
}
