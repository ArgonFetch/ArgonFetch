using ArgonFetch.Application.Queries;
using FluentValidation;

namespace ArgonFetch.Application.Validators
{
    public class ProxyResourceQueryValidator : AbstractValidator<ProxyResourceQuery>
    {
        public ProxyResourceQueryValidator()
        {
            RuleFor(x => x.Url)
            .NotEmpty().NotNull().WithMessage("Url is required")
            .MinimumLength(3).WithMessage("Url must be at least 3 characters");
        }
    }
}