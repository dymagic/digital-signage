using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Signage.Web.Features.Users;

namespace Signage.Web;

/// <summary>Razor Pages, localization (R-64) and authorization conventions for the admin site.</summary>
public static class AdminUi
{
    public const string DefaultCulture = "en";

    public static IServiceCollection AddSignageAdminUi(this IServiceCollection services)
    {
        services.AddLocalization(o => o.ResourcesPath = "Resources");
        services.Configure<RouteOptions>(o => o.LowercaseUrls = true);

        services.AddRazorPages(o =>
            {
                o.Conventions.AuthorizeFolder("/Admin", Policies.Editor);
                o.Conventions.AllowAnonymousToPage("/Admin/Login");
                o.Conventions.AllowAnonymousToPage("/Admin/Setup");
                o.Conventions.AllowAnonymousToPage("/Admin/Forbidden");
                o.Conventions.AuthorizeFolder("/Admin/Users", Policies.Admin);
                o.Conventions.AuthorizeFolder("/Admin/Settings", Policies.Admin);
                o.Conventions.AuthorizePage("/Admin/Screens/Create", Policies.Admin);
                o.Conventions.AuthorizePage("/Admin/Screens/Delete", Policies.Admin);
                o.Conventions.AuthorizePage("/Admin/Screens/Pair", Policies.Admin);
                o.Conventions.AuthorizePage("/Admin/Groups/Create", Policies.Admin);
                o.Conventions.AuthorizePage("/Admin/Groups/Delete", Policies.Admin);
                o.Conventions.ConfigureFilter(new MustChangePasswordFilter());
            })
            .AddViewLocalization()
            .AddDataAnnotationsLocalization(o => o.DataAnnotationLocalizerProvider = (_, factory) => factory.Create(typeof(SharedResource)));

        // Model-binding messages ("The value 'x' is not valid") also come from the resource file.
        services.AddOptions<MvcOptions>().Configure<IStringLocalizer<SharedResource>>((o, l) =>
            {
                // Validation lives in the services (with field-specific messages) and in explicit [Required] attributes.
                // Without this, every non-nullable string would be implicitly required, and a blank name would hit the
                // generic "invalid value" path instead of "Enter a name".
                o.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;

                var messages = o.ModelBindingMessageProvider;
                messages.SetValueIsInvalidAccessor(v => l["Validation.ValueInvalid", v]);
                messages.SetAttemptedValueIsInvalidAccessor((v, f) => l["Validation.ValueInvalid", v]);
                messages.SetValueMustBeANumberAccessor(f => l["Validation.MustBeNumber"]);
                messages.SetValueMustNotBeNullAccessor(v => l["Validation.Required"]);
                messages.SetMissingBindRequiredValueAccessor(f => l["Validation.Required"]);
                messages.SetMissingKeyOrValueAccessor(() => l["Validation.Required"]);
                messages.SetMissingRequestBodyRequiredValueAccessor(() => l["Validation.Required"]);
                messages.SetNonPropertyAttemptedValueIsInvalidAccessor(v => l["Validation.ValueInvalid", v]);
                messages.SetNonPropertyUnknownValueIsInvalidAccessor(() => l["Validation.ValueInvalid", ""]);
                messages.SetNonPropertyValueMustBeANumberAccessor(() => l["Validation.MustBeNumber"]);
                messages.SetUnknownValueIsInvalidAccessor(f => l["Validation.ValueInvalid", ""]);
            });

        return services;
    }
}
