using MageBackend.Database;
using MediatR;
using Microsoft.EntityFrameworkCore;
using FluentValidation;

namespace MageBackend.Features.Feature.Commands
{
    public record CreateFeatureCommand : IRequest<CreateFeatureResult>
    {
        public string Id { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string? Description { get; init; }
    }

    public record CreateFeatureResult(bool Success, Database.Feature? Feature = null, string? Error = null);

    public class CreateFeatureHandler : IRequestHandler<CreateFeatureCommand, CreateFeatureResult>
    {
        private readonly ApplicationDbContext _context;

        public CreateFeatureHandler(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<CreateFeatureResult> Handle(CreateFeatureCommand command, CancellationToken cancellationToken)
        {
            var exists = await _context.Feature.AnyAsync(f => f.Id == command.Id, cancellationToken);
            if (exists) return new CreateFeatureResult(false, Error: "Feature already exists");

            var feature = new Database.Feature
            {
                Id = command.Id,
                Name = command.Name,
                Description = command.Description,
                Active = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.Feature.Add(feature);
            await _context.SaveChangesAsync(cancellationToken);

            return new CreateFeatureResult(true, Feature: feature);
        }
    }

    public class CreateFeatureCommandValidator : AbstractValidator<CreateFeatureCommand>
    {
        private const string RequiredMessage = "Id and Name are required";

        public CreateFeatureCommandValidator()
        {
            RuleFor(x => x.Id).NotEmpty().WithMessage(RequiredMessage);
            RuleFor(x => x.Name).NotEmpty().WithMessage(RequiredMessage);
        }
    }

    public record UpdateFeatureCommand : IRequest<UpdateFeatureResult>
    {
        public string Id { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string? Description { get; init; }
    }

    public record UpdateFeatureResult(bool Success, Database.Feature? Feature = null, string? Error = null, int StatusCode = 200);

    public class UpdateFeatureHandler : IRequestHandler<UpdateFeatureCommand, UpdateFeatureResult>
    {
        private readonly ApplicationDbContext _context;

        public UpdateFeatureHandler(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<UpdateFeatureResult> Handle(UpdateFeatureCommand command, CancellationToken cancellationToken)
        {
            var feature = await _context.Feature.AsTracking().FirstOrDefaultAsync(f => f.Id == command.Id, cancellationToken);
            if (feature == null) return new UpdateFeatureResult(false, Error: "Feature not found", StatusCode: 404);

            feature.Name = command.Name;
            feature.Description = command.Description;
            feature.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync(cancellationToken);
            return new UpdateFeatureResult(true, Feature: feature);
        }
    }

    public class UpdateFeatureCommandValidator : AbstractValidator<UpdateFeatureCommand>
    {
        public UpdateFeatureCommandValidator()
        {
            RuleFor(x => x.Name).NotEmpty().WithMessage("Name is required");
        }
    }

    public record DeleteFeatureCommand(string Id) : IRequest<DeleteFeatureResult>;

    public record DeleteFeatureResult(bool Success, string? Error = null, int StatusCode = 204);

    public class DeleteFeatureHandler : IRequestHandler<DeleteFeatureCommand, DeleteFeatureResult>
    {
        private readonly ApplicationDbContext _context;

        public DeleteFeatureHandler(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<DeleteFeatureResult> Handle(DeleteFeatureCommand command, CancellationToken cancellationToken)
        {
            var feature = await _context.Feature.AsTracking().FirstOrDefaultAsync(f => f.Id == command.Id, cancellationToken);
            if (feature == null) return new DeleteFeatureResult(false, Error: "Feature not found", StatusCode: 404);

            _context.Feature.Remove(feature);
            await _context.SaveChangesAsync(cancellationToken);

            return new DeleteFeatureResult(true);
        }
    }

    public record ToggleFeatureStatusCommand(string Id, bool Active) : IRequest<ToggleFeatureStatusResult>;

    public record ToggleFeatureStatusResult(bool Success, Database.Feature? Feature = null, string? Error = null, int StatusCode = 200);

    public class ToggleFeatureStatusHandler : IRequestHandler<ToggleFeatureStatusCommand, ToggleFeatureStatusResult>
    {
        private readonly ApplicationDbContext _context;

        public ToggleFeatureStatusHandler(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<ToggleFeatureStatusResult> Handle(ToggleFeatureStatusCommand command, CancellationToken cancellationToken)
        {
            var feature = await _context.Feature.AsTracking().FirstOrDefaultAsync(f => f.Id == command.Id, cancellationToken);
            if (feature == null) return new ToggleFeatureStatusResult(false, Error: "Feature not found", StatusCode: 404);

            feature.Active = command.Active;
            feature.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync(cancellationToken);
            return new ToggleFeatureStatusResult(true, Feature: feature);
        }
    }
}
