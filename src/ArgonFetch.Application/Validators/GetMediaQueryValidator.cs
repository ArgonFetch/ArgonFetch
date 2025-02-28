using ArgonFetch.Application.Queries;
using FluentValidation;

namespace ArgonFetch.Application.Validators
{
    public class GetMediaQueryValidator : AbstractValidator<GetMediaQuery>
    {
        public GetMediaQueryValidator()
        {
            RuleFor(x => x.Query)
            .NotEmpty().NotNull().WithMessage("Query is required")
            .MinimumLength(3).WithMessage("Query must be at least 3 characters");
        }
    }
}
