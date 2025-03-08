using ArgonFetch.Application.Queries;
using ArgonFetch.Application.Validators.ValidationHelpers;
using FluentValidation;

namespace ArgonFetch.Application.Validators
{
    public class ProxyRangeQueryValidator : AbstractValidator<ProxyRangeQuery>
    {
        public ProxyRangeQueryValidator()
        {
            RuleFor(x => x.Url)
                .NotEmpty().NotNull().WithMessage("Url is required")
                .Must(UrlValidation.IsValidUrl).WithMessage("Url must be a valid URL");

            RuleFor(x => x.Start)
                .GreaterThanOrEqualTo(0).WithMessage("Start must be a non-negative integer");

            RuleFor(x => x.End)
                .GreaterThan(x => x.Start).WithMessage("End must be greater than Start");
        }
    }
}
