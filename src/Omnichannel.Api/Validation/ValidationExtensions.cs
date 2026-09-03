using System.ComponentModel.DataAnnotations;

namespace Omnichannel.Api.Validation;

internal static class ValidationExtensions
{
    public static bool TryValidate<T>(this T model, out IReadOnlyList<string> errors)
    {
        var context = new ValidationContext(model!);
        var results = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(model!, context, results, validateAllProperties: true);
        errors = [.. results.Select(r => r.ErrorMessage ?? "Invalid value.")];
        return isValid;
    }
}
