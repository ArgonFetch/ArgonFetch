using ArgonFetch.Application.Queries;
using FluentValidation;

namespace ArgonFetch.Application.Validators
{
    public class GetPlaylistQueryValidator : AbstractValidator<GetPlaylistQuery>
    {
        public GetPlaylistQueryValidator()
        {
            RuleFor(x => x.Url)
            .NotEmpty().NotNull().WithMessage("Url is required")
            .MinimumLength(4).WithMessage("Url must be at least 4 characters");
        }
    }
}
