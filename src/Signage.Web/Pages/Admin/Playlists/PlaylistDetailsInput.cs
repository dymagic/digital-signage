using System.ComponentModel.DataAnnotations;

namespace Signage.Web.Pages.Admin.Playlists;

public sealed class PlaylistDetailsInput
{
    [Display(Name = "Playlists.Field.Name")]
    public string Name { get; set; } = "";

    [Display(Name = "Playlists.Field.Description")]
    public string? Description { get; set; }
}
