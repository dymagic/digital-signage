using System.ComponentModel.DataAnnotations;

namespace Signage.Web.Pages.Admin.Groups;

public sealed class GroupInput
{
    [Display(Name = "Groups.Field.Name")]
    public string Name { get; set; } = "";

    [Display(Name = "Groups.Field.Description")]
    public string? Description { get; set; }

    [Display(Name = "Groups.Field.SortOrder")]
    public int SortOrder { get; set; }
}
