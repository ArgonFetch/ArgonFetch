﻿using ArgonFetch.Application.Queries;
using ArgonFetch.Application.Validators.ValidationHelpers;
using FluentValidation;

namespace ArgonFetch.Application.Validators
{
    public class ProxyHeadQueryValidator : AbstractValidator<ProxyHeadQuery>
    {
        public ProxyHeadQueryValidator()
        {
            RuleFor(x => x.Url)
                .NotEmpty().NotNull().WithMessage("Url is required")
                .Must(UrlValidation.IsValidUrl).WithMessage("Url must be a valid URL");
        }
    }
}
