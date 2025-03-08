using ArgonFetch.Application.Queries;
using ArgonFetch.Application.Validators.ValidationHelpers;
using FluentValidation;

namespace ArgonFetch.Application.Validators
{
    public class ProxyUrlQueryValidator : AbstractValidator<ProxyUrlQuery>
    {
        public ProxyUrlQueryValidator()
        {
            RuleFor(x => x.Url)
                .NotEmpty().NotNull().WithMessage("Url is required")
                .Must(UrlValidation.IsValidUrl).WithMessage("Url must be a valid URL");
        }
    }
}
