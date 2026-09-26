using System.Text.Json;
using HomeBusinessAssistant.Application.Configuration;
using HomeBusinessAssistant.Application.Persistence;
using HomeBusinessAssistant.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace HomeBusinessAssistant.Infrastructure.Persistence.Repositories;

/// <summary>EF-backed system settings with canonical JSON and optimistic concurrency.</summary>
public sealed class SystemSettingRepository(IDbContextFactory<AssistantDbContext> contextFactory) : ISystemSettingRepository
{
    /// <inheritdoc />
    public async ValueTask<SystemSettingRecord?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        ValidateKey(key);
        await using AssistantDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        SystemSettingEntity? entity = await context.Set<SystemSettingEntity>().AsNoTracking()
            .SingleOrDefaultAsync(item => item.Key == key, cancellationToken)
            .ConfigureAwait(false);
        return entity is null ? null : PersistenceMapper.Map(entity);
    }

    /// <inheritdoc />
    public async ValueTask<SystemSettingRecord> SaveAsync(
        SystemSettingRecord setting,
        long? expectedConcurrencyToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(setting);
        ValidateKey(setting.Key);
        if (string.IsNullOrWhiteSpace(setting.SchemaVersion) || setting.SchemaVersion.Length > 32)
        {
            throw new ArgumentException("The setting schema version is invalid.", nameof(setting));
        }

        using JsonDocument document = JsonDocument.Parse(setting.ValueJson, new JsonDocumentOptions { MaxDepth = 64 });
        IReadOnlyList<ConfigurationValidationError> secretErrors = ConfigurationSecretPolicy.Validate(document.RootElement);
        if (secretErrors.Count > 0)
        {
            throw new ConfigurationValidationException(secretErrors);
        }

        string canonicalJson = CanonicalJson.Serialize(document.RootElement);
        await using AssistantDbContext context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        SystemSettingEntity? entity = await context.Set<SystemSettingEntity>()
            .SingleOrDefaultAsync(item => item.Key == setting.Key, cancellationToken)
            .ConfigureAwait(false);
        if (entity is null)
        {
            if (expectedConcurrencyToken.HasValue)
            {
                throw new DbUpdateConcurrencyException("The setting no longer exists.");
            }

            entity = new SystemSettingEntity
            {
                Key = setting.Key,
                ValueJson = canonicalJson,
                SchemaVersion = setting.SchemaVersion,
                UpdatedAtUtc = setting.UpdatedAtUtc.ToUniversalTime(),
                ConcurrencyToken = 1,
            };
            context.Add(entity);
        }
        else
        {
            if (!expectedConcurrencyToken.HasValue || entity.ConcurrencyToken != expectedConcurrencyToken.Value)
            {
                throw new DbUpdateConcurrencyException("The setting changed before it could be saved.");
            }

            entity.ValueJson = canonicalJson;
            entity.SchemaVersion = setting.SchemaVersion;
            entity.UpdatedAtUtc = setting.UpdatedAtUtc.ToUniversalTime();
            entity.ConcurrencyToken++;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return PersistenceMapper.Map(entity);
    }

    private static void ValidateKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key)
            || key.Length > 128
            || key.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_')))
        {
            throw new ArgumentException("The system setting key is invalid.", nameof(key));
        }
    }
}
